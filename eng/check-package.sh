#!/usr/bin/env bash
# Packs StateAlchemist and checks the package the way an application would use it: the runtime under lib/, the
# generator and the code fixes under analyzers/, and then a throwaway project that references the package from a
# local feed, generates a machine, runs it, and gets the analyzer's diagnostics. Nothing is published.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "== pack"
dotnet pack "$root/src/StateAlchemist" -c Release -o "$work/feed" --nologo | tail -1
package="$(ls "$work/feed"/*.nupkg)"
symbols="$(ls "$work/feed"/*.snupkg)"
echo "packed $(basename "$package") and $(basename "$symbols")"
version="$(basename "$package" .nupkg | sed 's/^StateAlchemist\.//')"

# A symbol package with no PDB in it would look like symbols and debug like nothing.
if ! unzip -Z1 "$symbols" | grep -q "lib/net8.0/StateAlchemist.pdb"; then
    echo "the symbol package has no portable PDB for net8.0" >&2
    unzip -Z1 "$symbols" >&2
    exit 1
fi

echo "== contents"
contents="$(unzip -Z1 "$package")"
for entry in \
    "lib/netstandard2.0/StateAlchemist.dll" \
    "lib/net8.0/StateAlchemist.dll" \
    "lib/net10.0/StateAlchemist.dll" \
    "lib/net11.0/StateAlchemist.dll" \
    "analyzers/dotnet/cs/StateAlchemist.Generators.dll" \
    "analyzers/dotnet/cs/StateAlchemist.CodeFixes.dll" \
    "PACKAGE.md"; do
    if ! grep -qx "$entry" <<<"$contents"; then
        echo "missing from the package: $entry" >&2
        echo "$contents" >&2
        exit 1
    fi
done

# A runtime library must not drag Roslyn in with it.
if unzip -p "$package" "StateAlchemist.nuspec" | grep -q "Microsoft.CodeAnalysis"; then
    echo "the package depends on Roslyn, which a runtime library must not" >&2
    exit 1
fi

echo "== consume"
mkdir -p "$work/app"

# On the oldest SDK the package claims to support, when that SDK is here. An analyzer compiled against a newer
# Roslyn than the host is refused with CS9057 and the machine is simply never generated, which is a break no test
# that runs on the newest SDK can see. CI installs 8.0.x for this job; a developer without it gets a note.
if dotnet --list-sdks | grep -q '^8\.'; then
    echo "   on the .NET 8 SDK, which is the floor"
    cat > "$work/app/global.json" <<XML
{ "sdk": { "version": "8.0.100", "rollForward": "latestFeature" } }
XML
else
    echo "   no .NET 8 SDK here: consuming with $(dotnet --version) instead, which does not test the floor" >&2
fi
cat > "$work/app/nuget.config" <<XML
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$work/feed" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
cat > "$work/app/App.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="StateAlchemist" Version="$version" />
  </ItemGroup>
</Project>
XML
cat > "$work/app/Program.cs" <<'CS'
using System;
using System.Threading.Tasks;
using StateAlchemist;

public struct Root : IRootState { public int Moves; }
[Initial] public struct Idle : IState<Root> { }
public struct Busy : IState<Root> { }

[Module]
public static class AppModule
{
    [Transition(From = typeof(Idle), To = typeof(Busy)), On(1)]
    public static void Start(ref Root root) => root.Moves++;

    [Transition(From = typeof(Root)), OnAny]
    public static void Ignore()
    {
    }
}

[Machine(Root = typeof(Root), Value = typeof(byte))]
[Include(typeof(AppModule))]
public sealed partial class AppMachine;

public static class Program
{
    public static async Task<int> Main()
    {
        var machine = new AppMachine();
        await machine.StartAsync();
        await machine.FireAsync((byte)1);
        machine.TryGetState<Root>(out var root);
        Console.WriteLine(AppMachine.Mermaid);
        return machine.IsIn<Busy>() && root.Moves == 1 ? 0 : 1;
    }
}
CS
dotnet run --project "$work/app" -c Release --nologo | head -20

echo "== the analyzer is in the package"
cat >> "$work/app/Program.cs" <<'CS'

[Module]
public static class BadModule
{
    [Transition(From = typeof(Idle)), On(2)]
    internal static void NotPublic(ref Idle self) { }
}
CS
# The build must now fail, with the analyzer's error and no other: it comes from the package, not from this tree.
dotnet build "$work/app" -c Release --nologo > "$work/build.log" 2>&1 || true
if grep -q "error SALCH0002" "$work/build.log"; then
    echo "SALCH0002 reported, as it must be"
else
    echo "the analyzer did not report SALCH0002 from the package" >&2
    tail -30 "$work/build.log" >&2
    exit 1
fi

echo "== ok: $package"

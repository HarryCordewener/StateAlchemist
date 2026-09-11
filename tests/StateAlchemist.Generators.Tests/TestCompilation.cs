using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StateAlchemist.Generators.Tests;

/// <summary>
/// Compiles application source the way an app build would: against every assembly this test process runs with —
/// the runtime, the contract machines and the samples — as metadata. Declarations the source includes therefore
/// arrive the way a real app's plugins do.
/// </summary>
internal static class TestCompilation
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => path.Length > 0)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

    public static CSharpCompilation Create(params string[] sources) => Create(LanguageVersion.Latest, sources);

    public static CSharpCompilation Create(LanguageVersion language, params string[] sources) => CSharpCompilation.Create(
        "App",
        sources.Select((source, i) => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(language), path: $"App{i}.cs")),
        References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: language >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable));

    /// <summary>A C# type name for <paramref name="type"/>, as source would write it.</summary>
    public static string Name(Type type) => "global::" + type.FullName!.Replace('+', '.');

    /// <summary>A <c>[Machine]</c> class, as an application would declare it.</summary>
    public static string Machine(string name, Type root, Type value, Type? context, Type[] modules, string options = "", string body = ";") =>
        $$"""
        [global::StateAlchemist.Machine(Root = typeof({{Name(root)}}), Value = typeof({{Name(value)}}){{(context is null ? "" : $", Context = typeof({Name(context)})")}}{{options}})]
        {{string.Concat(modules.Select(m => $"[global::StateAlchemist.Include(typeof({Name(m)}))]\n"))}}public sealed partial class {{name}}{{body}}
        """;
}

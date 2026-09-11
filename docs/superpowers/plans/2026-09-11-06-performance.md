# StateAlchemist Plan 6 — Performance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Meet spec §9 — and measure the rest honestly — so that a generated machine costs what a hand-written
`switch` costs on the paths where that can be measured at all, allocates nothing on every synchronous path, builds
a TNC-sized machine in well under a second, and publishes into a native binary with no warnings.

**Architecture:** Nothing about the semantics changes. The entry points of a machine without an inbox are flattened
so the common call — refuse, dispatch, return — passes through no `async` method and no helper. A machine with an
inbox keeps the design Plan 5 generated, with the allocations taken out of it: inputs are pooled and are themselves
the `IValueTaskSource` the caller awaits, a caller that finds the machine idle runs its own input inline instead of
waking a pump, and a call that never suspends returns a completed `ValueTask` with its input already back in the
pool. A call that does suspend finishes in one `async` method instead of a chain of them. Everything is proved by
allocation tests, which fail the build, and measured by benchmarks, which do not.

**Tech Stack:** BenchmarkDotNet 0.15.8 (in-process on `net11.0`), Stateless 5.20 as the outside baseline, the
NativeAOT toolchain for the publish check, TUnit for the allocation, size and construction tests.

**Spec:** [`docs/superpowers/specs/2026-09-11-statealchemist-design.md`](../specs/2026-09-11-statealchemist-design.md)
— §9 is this plan's acceptance criteria; §6.10 is the bounded inbox. The roadmap:
[`2026-09-11-00-roadmap.md`](2026-09-11-00-roadmap.md).

> **Validated (2026-09-11):** built on Plans 1–5 and run on net8.0, net10.0 and net11.0 — 939 tests, all passing,
> including seven allocation tests, the bounded-inbox test, the generator size and construction tests, and all 76
> contracts against generated machines. The AOT sample publishes with no warnings and runs. The code below is that
> code.

## Global Constraints

Plan 5's constraints hold — plain C# 7.3 in generated code, no reflection, no dictionaries, no warning — and one
more: **nothing in this plan may change what a machine does.** Every contract, every agreement test and every
allocation test is the definition of "unchanged"; a faster path that changes an order or an exception is a bug, not
a trade.

Measurements in this plan are from one machine (x64, .NET 11 RC1, in-process short jobs). They are recorded to say
what the shape of the cost is, not as thresholds for CI: the tests that fail a build are the allocation tests and
the two size tests, which are stable.

## File structure

```
samples/StateAlchemist.Samples/Performance/   new: the shapes §9 measures, as a module and a machine
benchmarks/StateAlchemist.Benchmarks/         new: the generated machine against a hand-written switch and Stateless
tests/StateAlchemist.Generated.Tests/Performance/    new: allocation tests, the bounded-inbox test
tests/StateAlchemist.Generators.Tests/Performance/   new: a TNC-sized machine — generation, incrementality, construction
samples/StateAlchemist.AotSample/             new: a machine in a native binary; CI publishes it with -warnaserror
src/StateAlchemist.Generators/
    MachineEmitter.Firing.cs    changed: the flattened entry, one async method to finish a suspended call
    MachineEmitter.Inbox.cs     changed: pooled inputs, the inline path, the bounded inbox
    MachineEmitter.Runs.cs      changed: the run-of-one buffer is made on first use, not in the constructor
    MachineEmitter.cs · .Lifecycle.cs   changed: that field, and the pooled builder on lifecycle actions
docs/concepts/                  changed: concurrency, actions and lifecycle say what things cost, measured
```

---

### Task 1: The shapes to measure, and the benchmarks that measure them

**Files:**
- Create: `samples/StateAlchemist.Samples/Performance/PerformanceMachine.cs`, `UnionPolyfill.cs`
- Create: `benchmarks/StateAlchemist.Benchmarks/` — `StateAlchemist.Benchmarks.csproj`, `Machines.cs`,
  `Baselines.cs`, `MachineBenchmarks.cs`, `Program.cs`
- Modify: `Directory.Packages.props`, `StateAlchemist.slnx`

**Interfaces:**
- Consumes: Plans 1–5. The sample module is a module like any other; the benchmark project includes it in four
  machines, one per concurrency shape.
- Produces: `PerformanceModule`, `PerformanceDecisionModule`, `Counters`, and the states `Root`, `Outer`, `Text`,
  `Other`, `Parked` — used by the benchmarks and by Task 2's allocation tests. `InlineMachine`,
  `UncheckedMachine`, `SerializedMachine`, `DecidingMachine` in the benchmark project.

- [ ] **Step 1: The shapes**

Every action counts instead of logging, so what a benchmark sees is the machine's own cost and not a list's.

`samples/StateAlchemist.Samples/Performance/PerformanceMachine.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Samples.Performance;

// The shapes spec §9 measures: a stay, a move across two levels with synchronous actions, a run over a 1 KB payload,
// and an action that suspends. Every action counts instead of logging, so what a benchmark or an allocation test
// sees is the machine's own cost.
//
// Root ─┬─ Outer [Initial] ─── Text [Initial]
//       └─ Other ─────────── Parked [Initial]

public sealed class Counters
{
    public int Exits;
    public int Entries;
    public int Completions;
    public int Suspensions;
}

public struct Root : IRootState
{
    public int Moves;
}

[Initial]
public struct Outer : IState<Root>
{
}

[Initial]
public struct Text : IState<Outer>
{
    public int Count;
    public int Length;
}

public struct Other : IState<Root>
{
}

[Initial]
public struct Parked : IState<Other>
{
}

/// <summary>A tick, for the typed-event path.</summary>
public readonly struct Tick : IEvent;

[Module]
public static class PerformanceModule
{
    public const byte Iac = 255;

    /// <summary>1: a stay with a synchronous transform.</summary>
    [Transition(From = typeof(Text)), On(1)]
    public static void Count(ref Text self) => self.Count++;

    /// <summary>2: across two levels — exits Text and Outer, enters Other and Parked.</summary>
    [Transition(From = typeof(Text), To = typeof(Parked)), On(2)]
    public static void Park(ref Root root) => root.Moves++;

    /// <summary>3: and back.</summary>
    [Transition(From = typeof(Parked), To = typeof(Text)), On(3)]
    public static void Resume(ref Root root) => root.Moves++;

    /// <summary>Any other value in Text is text: a run, up to the next IAC or command value.</summary>
    [Transition(From = typeof(Text)), OnAny, Run]
    public static void Capture(ref Text self, ReadOnlySpan<byte> run) => self.Length += run.Length;

    /// <summary>IAC is not text: the stop value of the run.</summary>
    [Transition(From = typeof(Text)), On(Iac)]
    public static void Escape(ref Text self) => self.Count++;

    /// <summary>Anything else while parked is ignored.</summary>
    [Transition(From = typeof(Other)), OnAny]
    public static void Ignore()
    {
    }

    /// <summary>A tick counts, wherever it arrives.</summary>
    [Transition(From = typeof(Root)), OnEvent(typeof(Tick))]
    public static void Ticked(ref Root root) => root.Moves++;

    /// <summary>4: a stay whose action suspends.</summary>
    [Transition(From = typeof(Text)), On(4)]
    public static class Suspend
    {
#if NET6_0_OR_GREATER
        // The machine's own async methods are pooled; an action that suspends should be too, or the benchmark
        // measures the sample's state machine instead of the machine's.
        [global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
#endif
        public static async ValueTask CompletedAsync(Counters counters)
        {
            await Task.Yield();
            counters.Suspensions++;
        }
    }

    [Exited(typeof(Text))]
    public static void LeaveText(Counters counters) => counters.Exits++;

    [Exited(typeof(Outer))]
    public static void LeaveOuter(Counters counters) => counters.Exits++;

    [Entered(typeof(Other))]
    public static void EnterOther(Counters counters) => counters.Entries++;

    [Entered(typeof(Parked))]
    public static void EnterParked(Counters counters) => counters.Entries++;
}

/// <summary>An async decision, which turns a machine that includes it into one with an inbox.</summary>
[Module]
public static class PerformanceDecisionModule
{
    public readonly record struct Yes;

    public readonly record struct No;

    public union Answer(Yes, No);

    /// <summary>9: decide, then stay.</summary>
    [Decision(From = typeof(Text)), On(9)]
    public static class Ask
    {
        public static ValueTask<Answer> DecideAsync(CancellationToken cancellation) => new(new Answer(new Yes()));

        [To(typeof(Parked))]
        public static void Complete(Yes outcome)
        {
        }

        [To(typeof(Parked))]
        public static void Complete(No outcome)
        {
        }
    }
}
```

`samples/StateAlchemist.Samples/Performance/UnionPolyfill.cs`:

```csharp
#if !NET11_0_OR_GREATER
// The two types the C# 15 compiler needs to treat a type as a union. .NET 11 ships them; below it, a library
// declares them itself, internally, as the language allows — which is what a declaring library targeting net8.0 does.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class UnionAttribute : Attribute;

    internal interface IUnion
    {
        object? Value { get; }
    }
}
#endif
```

- [ ] **Step 2: The benchmark project**

`benchmarks/StateAlchemist.Benchmarks/StateAlchemist.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Spec §9: the generated machine against a hand-written switch of the same shape, and against Stateless.
         Run it in Release, passing BenchmarkDotNet a filter; see Program.cs. -->
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>StateAlchemist.Benchmarks</RootNamespace>
    <IsPackable>false</IsPackable>
    <!-- The unchecked machine has the sample's suspending action; the benchmarks await every call, as SALCH0209 asks. -->
    <NoWarn>$(NoWarn);SALCH0209</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" />
    <PackageReference Include="Stateless" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\StateAlchemist\StateAlchemist.csproj" />
    <ProjectReference Include="..\..\src\StateAlchemist.Generators\StateAlchemist.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\..\samples\StateAlchemist.Samples\StateAlchemist.Samples.csproj" />
  </ItemGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Bcl.AsyncInterfaces" Version="10.0.12" />
    <PackageVersion Include="System.Memory" Version="4.6.3" />
    <PackageVersion Include="System.IO.Pipelines" Version="10.0.12" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
    <PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />
    <PackageVersion Include="Stateless" Version="5.20.0" />
    <PackageVersion Include="PolySharp" Version="1.16.0" />
    <PackageVersion Include="TUnit" Version="1.66.27" />
    <PackageVersion Include="TUnit.Core" Version="1.66.27" />
    <PackageVersion Include="TUnit.Assertions" Version="1.66.27" />
    <PackageVersion Include="PublicApiGenerator" Version="11.5.4" />
    <PackageVersion Include="Verify.TUnit" Version="32.0.0" />
  </ItemGroup>
</Project>
```

`StateAlchemist.slnx`:

```xml
<Solution>
  <Folder Name="/benchmarks/">
    <Project Path="benchmarks/StateAlchemist.Benchmarks/StateAlchemist.Benchmarks.csproj" />
  </Folder>
  <Folder Name="/samples/">
    <Project Path="samples/StateAlchemist.AotSample/StateAlchemist.AotSample.csproj" />
    <Project Path="samples/StateAlchemist.Samples/StateAlchemist.Samples.csproj" />
  </Folder>
  <Folder Name="/src/">
    <Project Path="src/StateAlchemist.Generators/StateAlchemist.Generators.csproj" />
    <Project Path="src/StateAlchemist.Model/StateAlchemist.Model.csproj" />
    <Project Path="src/StateAlchemist.Reference/StateAlchemist.Reference.csproj" />
    <Project Path="src/StateAlchemist/StateAlchemist.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/StateAlchemist.Contracts/StateAlchemist.Contracts.csproj" />
    <Project Path="tests/StateAlchemist.Generated.Tests/StateAlchemist.Generated.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Generators.Tests/StateAlchemist.Generators.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Model.Tests/StateAlchemist.Model.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Reference.Tests/StateAlchemist.Reference.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Tests/StateAlchemist.Tests.csproj" />
  </Folder>
</Solution>
```

The solution file also carries Task 6's AOT sample; adding it now costs nothing and saves editing the file twice.

- [ ] **Step 3: The machines, the baselines and the benchmarks**

`benchmarks/StateAlchemist.Benchmarks/Machines.cs`:

```csharp
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

// Each machine reads back what its transitions changed, so a benchmark can consume the result of firing it.
// TryGetText is what any consumer would call: a check that the state is active, then a copy of its data.

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule))]
public sealed partial class InlineMachine
{
    public int Count { get { TryGetText(out var text); return text.Count; } }

    public int Length { get { TryGetText(out var text); return text.Length; } }
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters), Concurrency = Concurrency.Unchecked)]
[Include(typeof(PerformanceModule))]
public sealed partial class UncheckedMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters), Concurrency = Concurrency.Serialized)]
[Include(typeof(PerformanceModule))]
public sealed partial class SerializedMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule)), Include(typeof(PerformanceDecisionModule))]
public sealed partial class DecidingMachine;
```

`benchmarks/StateAlchemist.Benchmarks/Baselines.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Stateless;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

/// <summary>
/// The performance sample written by hand: the fastest a machine of this shape can be — a switch on the state, then
/// on the value — with the same data, the same actions and the same results. Spec §9 holds the generated machine to
/// within twice this.
/// </summary>
public sealed class HandWrittenMachine(Counters counters)
{
    private enum Leaf : byte { Text, Parked }

    private Leaf _leaf;
    private Root _root;
    private Text _text;

    public int Count => _text.Count;

    public int Length => _text.Length;

    public ValueTask FireAsync(byte value)
    {
        switch (_leaf)
        {
            case Leaf.Text:
                switch (value)
                {
                    case 1: _text.Count++; return default;
                    case 2:
                        _root.Moves++;
                        _leaf = Leaf.Parked;
                        counters.Exits++;
                        counters.Exits++;
                        counters.Entries++;
                        counters.Entries++;
                        _text = default;
                        return default;
                    case PerformanceModule.Iac: _text.Count++; return default;
                    default: _text.Length++; return default;
                }

            default:
                if (value == 3)
                {
                    _root.Moves++;
                    _leaf = Leaf.Text;
                }

                return default;
        }
    }

    public ValueTask FireAsync(ReadOnlyMemory<byte> values)
    {
        var span = values.Span;
        for (var i = 0; i < span.Length;)
        {
            if (_leaf == Leaf.Text && span[i] is not (1 or 2 or 4 or PerformanceModule.Iac))
            {
                var stop = span.Slice(i).IndexOfAny((byte)1, (byte)2, PerformanceModule.Iac);
                var length = stop < 0 ? span.Length - i : stop;
                _text.Length += length;
                i += length;
                continue;
            }

            _ = FireAsync(span[i++]);
        }

        return default;
    }
}

/// <summary>The same shape in Stateless 5.20, fired synchronously — what TNC runs today.</summary>
public sealed class StatelessMachine
{
    private enum State { Root, Outer, Text, Other, Parked }

    private readonly StateMachine<State, byte> _machine = new(State.Text);
    private Root _root;
    private Text _text;

    public StatelessMachine(Counters counters)
    {
        _machine.Configure(State.Outer).SubstateOf(State.Root).OnExit(() => counters.Exits++);
        var text = _machine.Configure(State.Text).SubstateOf(State.Outer)
            .InternalTransition(1, () => _text.Count++)
            .Permit(2, State.Parked)
            .InternalTransition(PerformanceModule.Iac, () => _text.Count++)
            .OnExit(() => { counters.Exits++; _text = default; });
        var other = _machine.Configure(State.Other).SubstateOf(State.Root).OnEntry(() => counters.Entries++);
        _machine.Configure(State.Parked).SubstateOf(State.Other)
            .OnEntry(() => { counters.Entries++; _root.Moves++; })
            .Permit(3, State.Text);

        // Stateless has no "any value" trigger and no runs: text is one internal transition per value, and the
        // parked state ignores every value but the one that resumes.
        for (var value = 0; value < 256; value++)
        {
            if (value is not (1 or 2 or 4 or PerformanceModule.Iac))
            {
                text.InternalTransition((byte)value, () => _text.Length++);
            }

            if (value != 3)
            {
                other.Ignore((byte)value);
            }
        }
    }

    public int Count => _text.Count;

    public int Length => _text.Length;

    public void Fire(byte value) => _machine.Fire(value);
}
```

Stateless has no "any value" trigger and no runs, so text is one configured internal transition per byte value —
which is the honest way to give it the same behaviour, and also why it reads a kilobyte four orders of magnitude
slower.

`benchmarks/StateAlchemist.Benchmarks/MachineBenchmarks.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

/// <summary>Spec §9, per call: a stay, a move across two levels and back, a 1 KB run, and an action that suspends.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class MachineBenchmarks
{
    private static readonly byte[] Kilobyte = Enumerable.Repeat((byte)'a', 1024).ToArray();

    private readonly Counters _counters = new();

    private InlineMachine _generated = null!;
    private HandWrittenMachine _handWritten = null!;
    private StatelessMachine _stateless = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generated = new InlineMachine(_counters);
        _generated.StartAsync().GetAwaiter().GetResult();
        _handWritten = new HandWrittenMachine(new Counters());
        _stateless = new StatelessMachine(new Counters());
    }

    // Every benchmark returns state the firing changed, so nothing measured here can be optimised away: a
    // hand-written switch whose result is unused compiles to nothing, and then there is no baseline to be within.
    [Benchmark(Baseline = true), BenchmarkCategory("Stay")]
    public int StayHandWritten()
    {
        _ = _handWritten.FireAsync(1);
        return _handWritten.Count;
    }

    [Benchmark, BenchmarkCategory("Stay")]
    public int StayGenerated()
    {
        _ = _generated.FireAsync(1);
        return _generated.Count;
    }

    [Benchmark, BenchmarkCategory("Stay")]
    public int StayStateless()
    {
        _stateless.Fire(1);
        return _stateless.Count;
    }

    [Benchmark, BenchmarkCategory("Move")]
    public int MoveAndBackHandWritten()
    {
        _ = _handWritten.FireAsync(2);
        _ = _handWritten.FireAsync(3);
        return _handWritten.Count;
    }

    [Benchmark, BenchmarkCategory("Move")]
    public int MoveAndBackGenerated()
    {
        _ = _generated.FireAsync(2);
        _ = _generated.FireAsync(3);
        return _generated.Count;
    }

    [Benchmark, BenchmarkCategory("Move")]
    public int MoveAndBackStateless()
    {
        _stateless.Fire(2);
        _stateless.Fire(3);
        return _stateless.Count;
    }

    [Benchmark, BenchmarkCategory("Run")]
    public int KilobyteRunHandWritten()
    {
        _ = _handWritten.FireAsync(Kilobyte);
        return _handWritten.Length;
    }

    [Benchmark, BenchmarkCategory("Run")]
    public int KilobyteRunGenerated()
    {
        _ = _generated.FireAsync(Kilobyte);
        return _generated.Length;
    }

    [Benchmark, BenchmarkCategory("Run")]
    public int KilobyteRunStateless()
    {
        foreach (var value in Kilobyte)
        {
            _stateless.Fire(value);
        }

        return _stateless.Length;
    }

    // What the action costs on its own is the floor for firing it: the difference is what the machine adds.
    [Benchmark(Baseline = true), BenchmarkCategory("Suspend")]
    public ValueTask SuspendingActionAlone() => PerformanceModule.Suspend.CompletedAsync(_counters);

    [Benchmark, BenchmarkCategory("Suspend")]
    public ValueTask SuspendingActionGenerated() => _generated.FireAsync(4);
}

/// <summary>What each concurrency mode costs on the same stay, and a machine whose async decision gives it an inbox.</summary>
[MemoryDiagnoser]
public class ConcurrencyBenchmarks
{
    private InlineMachine _checked = null!;
    private UncheckedMachine _unchecked = null!;
    private SerializedMachine _serialized = null!;
    private DecidingMachine _deciding = null!;

    [GlobalSetup]
    public void Setup()
    {
        _checked = new InlineMachine(new Counters());
        _unchecked = new UncheckedMachine(new Counters());
        _serialized = new SerializedMachine(new Counters());
        _deciding = new DecidingMachine(new Counters());
        foreach (var machine in new IMachine<byte>[] { _checked, _unchecked, _serialized, _deciding })
        {
            machine.StartAsync().GetAwaiter().GetResult();
        }
    }

    [Benchmark]
    public ValueTask Unchecked() => _unchecked.FireAsync(1);

    [Benchmark(Baseline = true)]
    public ValueTask Checked() => _checked.FireAsync(1);

    [Benchmark]
    public ValueTask Serialized() => _serialized.FireAsync(1);

    [Benchmark]
    public ValueTask CheckedWithAnInbox() => _deciding.FireAsync(1);
}
```

`benchmarks/StateAlchemist.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;

// dotnet run -c Release --project benchmarks/StateAlchemist.Benchmarks -- --filter '*'
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

Every benchmark returns state the firing changed. Without that, the hand-written baseline's stay compiles to
nothing at all and there is no baseline left to be within.

- [ ] **Step 4: Run them**

Run: `cd benchmarks/StateAlchemist.Benchmarks && dotnet run -c Release -- --filter '*' --job short --inProcess`

BenchmarkDotNet cannot build its own generated project against this SDK's `net11.0`, so `--inProcess` is not
optional here.

Expected: it runs, and the generated machine is already inside §9's absolute targets. What the rest of this plan
does is take the allocations out of the paths that have them, and shrink the inbox's per-call cost.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Benchmark the generated machine against a hand-written switch and Stateless"
```

---

### Task 2: Allocation tests — the targets that fail the build

**Files:**
- Create: `tests/StateAlchemist.Generated.Tests/Performance/PerformanceMachines.cs`,
  `tests/StateAlchemist.Generated.Tests/Performance/AllocationTests.cs`

**Interfaces:**
- Consumes: Task 1's module; Plan 5's generated test project.
- Produces: `InlineMachine`, `SerializedMachine`, `DecidingMachine`, `BoundedRecorderMachine` in the test project —
  `BoundedRecorderMachine` is Task 4's subject too.

- [ ] **Step 1: The machines**

`tests/StateAlchemist.Generated.Tests/Performance/PerformanceMachines.cs`:

```csharp
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Generated.Tests.Performance;

// The performance sample's modules, as three kinds of generated machine: inline, serialized, and one whose async
// decision gives it an inbox — the shape TNC's machine will have.

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule))]
public sealed partial class InlineMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters), Concurrency = Concurrency.Serialized)]
[Include(typeof(PerformanceModule))]
public sealed partial class SerializedMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule)), Include(typeof(PerformanceDecisionModule))]
public sealed partial class DecidingMachine;

[Machine(Root = typeof(StateAlchemist.Contracts.Machines.Recording.Root), Value = typeof(byte), Context = typeof(StateAlchemist.Contracts.Machines.RecordingContext),
    Concurrency = Concurrency.Serialized, InboxCapacity = 1)]
[Include(typeof(StateAlchemist.Contracts.Machines.Recording.RecorderModule))]
public sealed partial class BoundedRecorderMachine;
```

- [ ] **Step 2: The tests**

`tests/StateAlchemist.Generated.Tests/Performance/AllocationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Samples.Performance;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests.Performance;

/// <summary>
/// Spec §9: every synchronous path allocates nothing. Measured per call after warming up, on the test thread, in the
/// Debug build the suite runs — where an async method allocates even when it completes synchronously, so these also
/// prove the synchronous paths call none.
/// </summary>
[NotInParallel]
public class AllocationTests
{
    private static readonly byte[] Kilobyte = Enumerable.Repeat((byte)'a', 1024).ToArray();

    private static long PerCall(Func<ValueTask> fire)
    {
        for (var i = 0; i < 100; i++)
        {
            Complete(fire());
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            Complete(fire());
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
    }

    private static void Complete(ValueTask fired)
    {
        if (!fired.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The call did not complete synchronously.");
        }

        fired.GetAwaiter().GetResult();
    }

    private static async Task<T> Started<T>(T machine)
        where T : IMachine<byte>
    {
        await machine.StartAsync();
        return machine;
    }

    [Test]
    public async Task AStayAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync((byte)1))).IsEqualTo(0);
    }

    [Test]
    public async Task AMoveAcrossTwoLevelsWithSynchronousActionsAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() =>
        {
            Complete(machine.FireAsync((byte)2));
            return machine.FireAsync((byte)3);
        })).IsEqualTo(0);
    }

    [Test]
    public async Task AKilobyteRunAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync(Kilobyte))).IsEqualTo(0);
    }

    [Test]
    public async Task AnEventAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync(new Tick()))).IsEqualTo(0);
    }

    [Test]
    public async Task ASerializedMachineAllocatesNothingPerCall()
    {
        var machine = await Started(new SerializedMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync((byte)1))).IsEqualTo(0);
        await Assert.That(PerCall(() => machine.FireAsync(Kilobyte))).IsEqualTo(0);
    }

    /// <summary>
    /// A suspending action costs a fixed amount every time (spec §9): the call finishes in one async method, so
    /// firing the action adds a bounded amount to awaiting it directly, and never more as the calls go on. The
    /// Debug build the suite runs compiles async state machines as classes, which defeats the pooled builder; what
    /// a Release build costs is in the benchmarks.
    /// </summary>
    [Test]
    public async Task ASuspendingActionCostsTheSameEveryTime()
    {
        var counters = new Counters();
        var machine = await Started(new InlineMachine(counters));

        // On one thread: every continuation comes back to this pump, so the per-thread counter sees all of it.
        var pump = new OneThread();
        var alone = pump.Measure(() => PerformanceModule.Suspend.CompletedAsync(counters));
        var first = pump.Measure(() => machine.FireAsync((byte)4));
        var again = pump.Measure(() => machine.FireAsync((byte)4));
        await Assert.That(again).IsEqualTo(first).Because("a suspending call costs the same every time");
        await Assert.That(first).IsLessThanOrEqualTo(8 * alone).Because($"firing a suspending action adds a bounded amount to it: {alone} B alone, {first} B through the machine");
    }

    /// <summary>Runs suspending work on the calling thread: <c>await</c> posts here, and this pump runs it.</summary>
    private sealed class OneThread : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_work)
            {
                _work.Enqueue((callback, state));
            }
        }

        /// <summary>Bytes one call of <paramref name="fire"/> allocates, awaited to completion, after warming up.</summary>
        public long Measure(Func<ValueTask> fire)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                Run(fire, 100);
                var before = GC.GetAllocatedBytesForCurrentThread();
                Run(fire, 1000);
                return (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        private void Run(Func<ValueTask> fire, int times)
        {
            for (var i = 0; i < times; i++)
            {
                var fired = fire();
                while (!fired.IsCompleted)
                {
                    (SendOrPostCallback Callback, object? State) next;
                    lock (_work)
                    {
                        if (_work.Count == 0)
                        {
                            continue;
                        }

                        next = _work.Dequeue();
                    }

                    next.Callback(next.State);
                }

                fired.GetAwaiter().GetResult();
            }
        }
    }

    [Test]
    public async Task AMachineWithAnInboxAllocatesNothingForOrdinaryInput()
    {
        var machine = await Started(new DecidingMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync((byte)1))).IsEqualTo(0);
        await Assert.That(PerCall(() => machine.FireAsync(Kilobyte))).IsEqualTo(0);
    }
}
```

`GC.GetAllocatedBytesForCurrentThread` counts one thread, so the suspending test installs a synchronization context
and pumps the continuations itself: otherwise the resumption lands on a thread-pool thread and the count is
meaningless. The suite runs a Debug build, where the C# compiler makes each `async` method's state machine a class
and the pooled builder cannot pool it — so that test measures the shape of the cost, not its size, and the size is
in the benchmarks.

- [ ] **Step 3: Run them to see which fail**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: the synchronous tests pass — Plan 4's inline path already allocates nothing — and the inbox machines'
tests fail: `Serialized` and the deciding machine allocate an input and a `ValueTask` source per call.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Allocation tests for every path spec §9 names"
```

---

### Task 3: The direct path, flattened

**Files:**
- Modify: `src/StateAlchemist.Generators/MachineEmitter.Firing.cs`, `MachineEmitter.cs`, `MachineEmitter.Runs.cs`,
  `MachineEmitter.Lifecycle.cs`

**Interfaces:**
- Consumes: Plan 5's emitter.
- Produces: the same generated API. `FastEntry` replaces the `Entry`/`ProcessValue`/`FastDispatch` trio for single
  triggers; `FinishAfterDispatch` replaces `Finish(AfterDispatch(...))`.

- [ ] **Step 1: One method for a call that does not suspend**

`src/StateAlchemist.Generators/MachineEmitter.Firing.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private const string Exception = "global::System.Exception";

    /// <summary>
    /// The public entry points. A machine without an inbox runs each call inline, guarded by the busy flag and draining
    /// its event queue; a machine with one submits each call as an input to the pump (<see cref="WriteInbox"/>).
    /// </summary>
    private void WriteFiring()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync({V} value)"))
        {
            if (HasInbox)
            {
                _w.Line("var input = Rent();");
                _w.Line("input.Single[0] = value;");
                _w.Line("input.Values = input.Single;");
                _w.Line("return Submit(input);");
            }
            else
            {
                FastEntry("DispatchValue(value)", "ProcessValueQueued(value)");
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync(global::System.ReadOnlyMemory<{V}> values)"))
        {
            if (HasInbox)
            {
                _w.Line("var input = Rent();");
                _w.Line("input.Values = values;");
                _w.Line("return Submit(input);");
            }
            else
            {
                Entry("ProcessValues(values)");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            _w.Line($"/// <summary>Fires a <see cref=\"{Name(_events[i])}\"/>.</summary>");
            using (_w.Block($"public {ValueTaskType} FireAsync(in {Name(_events[i])} e)"))
            {
                if (HasInbox)
                {
                    _w.Line("var input = Rent();");
                    _w.Line($"input.Tag = {i};");
                    _w.Line($"input.E{i} = e;");
                    _w.Line("return Submit(input);");
                }
                else
                {
                    FastEntry($"DispatchEvent{i}(e)", $"ProcessEventQueued{i}(e)");
                }
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync<TEvent>(TEvent e) where TEvent : struct, {Rt}IEvent"))
        {
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"if (typeof(TEvent) == typeof({Name(_events[i])})) return FireAsync(in global::System.Runtime.CompilerServices.Unsafe.As<TEvent, {Name(_events[i])}>(ref e));");
            }

            if (HasInbox)
            {
                _w.Line("var input = Rent();");
                _w.Line("input.Tag = -1;");
                _w.Line("input.Unknown = typeof(TEvent);");
                _w.Line("return Submit(input);");
            }
            else
            {
                Entry("ProcessUnknown(typeof(TEvent))");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            _w.Line($"/// <summary>Queues a <see cref=\"{Name(_events[i])}\"/> to run after the current transition. Only from code running inside the machine.</summary>");
            using (_w.Block($"public void Enqueue(in {Name(_events[i])} e)"))
            {
                if (HasInbox)
                {
                    _w.Line("var queued = Rent();");
                    _w.Line($"queued.Tag = {i};");
                    _w.Line($"queued.E{i} = e;");
                    _w.Line("EnqueueInput(queued);");
                }
                else
                {
                    _w.Line("RefuseOutside();");
                    _w.Line($"var queued = new QueuedEvent {{ Tag = {i} }};");
                    _w.Line($"queued.E{i} = e;");
                    _w.Line("Queue().Enqueue(queued);");
                }
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public void Enqueue<TEvent>(TEvent e) where TEvent : struct, {Rt}IEvent"))
        {
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"if (typeof(TEvent) == typeof({Name(_events[i])})) {{ Enqueue(in global::System.Runtime.CompilerServices.Unsafe.As<TEvent, {Name(_events[i])}>(ref e)); return; }}");
            }

            if (HasInbox)
            {
                _w.Line("var queued = Rent();");
                _w.Line("queued.Tag = -1;");
                _w.Line("queued.Unknown = typeof(TEvent);");
                _w.Line("EnqueueInput(queued);");
            }
            else
            {
                _w.Line("RefuseOutside();");
                _w.Line("Queue().Enqueue(new QueuedEvent { Tag = -1, Unknown = typeof(TEvent) });");
            }
        }

        _w.Line();
        using (_w.Block("private void RefuseOutside()"))
        {
            _w.Line("if (!_inside) throw new global::System.InvalidOperationException(\"Enqueue is for code running inside the machine, such as an action, a hook or a decision. From outside, use FireAsync.\");");
        }

        _w.Line();
        _w.Line($"private static {ValueTaskType} Faulted({Exception} exception) {{ return new {ValueTaskType}(global::System.Threading.Tasks.Task.FromException(exception)); }}");

        // The values of a batch: a run where the leaf gives the value to a run transition, otherwise one value.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchAt(global::System.ReadOnlyMemory<{V}> values, int start, int count)"))
        {
            if (HasRuns)
            {
                _w.Line("if (RunOf(values.Span[start]) >= 0) return DispatchRun(values.Slice(start, count));");
            }

            _w.Line("return DispatchValue(values.Span[start]);");
        }

        if (!HasInbox)
        {
            WriteDirectFiring();
        }
    }

    /// <summary>The inline path: refuse, run, release. A Checked machine holds its busy flag for a caller's whole FireAsync.</summary>
    private void WriteDirectFiring()
    {
        _w.Line();
        _w.Line("private global::System.Collections.Generic.Queue<QueuedEvent> Queue() { return _queue ?? (_queue = new global::System.Collections.Generic.Queue<QueuedEvent>()); }");
        _w.Line();
        using (_w.Block($"private {Exception} Refuse()"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) return new {Rt}MachineNotRunningException(_status);");
            if (IsChecked)
            {
                _w.Line($"if (global::System.Threading.Interlocked.Exchange(ref _busy, 1) != 0) return new {Rt}ConcurrentUseException();");
            }

            _w.Line("return null;");
        }

        _w.Line();
        using (_w.Block("private void Release()"))
        {
            if (IsChecked)
            {
                _w.Line("global::System.Threading.Volatile.Write(ref _busy, 0);");
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} Finish({ValueTaskType} pending)"))
        {
            _w.Line("if (pending.IsCompletedSuccessfully) { Release(); return default(" + ValueTaskType + "); }");
            _w.Line("return FinishAsync(pending);");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} FinishAsync({ValueTaskType} pending)"))
        {
            _w.Line("try { await pending; } finally { Release(); }");
        }

        // One trigger: events kept from a transition that threw run first, then the trigger, then step 9. The fast path
        // calls no async method: an async method allocates even when it completes synchronously in a Debug build, and
        // costs a state machine in any. Only a step that really suspends continues in one.
        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} ProcessValueQueued({V} value)"))
        {
            _w.Line("await DrainQueue();");
            _w.Line("_inside = true;");
            _w.Line("try { await DispatchValue(value); } finally { _inside = false; }");
            _w.Line("await DrainQueue();");
        }

        // A trigger that suspended: one async method finishes the whole call — the transition, the events it queued,
        // and the release — so a suspended call costs one state machine, not one per step of the way back.
        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} FinishAfterDispatch({ValueTaskType} dispatched)"))
        {
            using (_w.Block("try"))
            {
                _w.Line("try { await dispatched; } finally { _inside = false; }");
                _w.Line("await DrainQueue();");
            }

            _w.Line("finally { Release(); }");
        }

        // A batch: one call per run or value, inline while each completes synchronously.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} ProcessValues(global::System.ReadOnlyMemory<{V}> values)"))
        {
            using (_w.Block("for (var i = 0; i < values.Length;)"))
            {
                _w.Line("if (_queue != null && _queue.Count != 0) return ProcessValuesFrom(values, i);");
                _w.Line(HasRuns ? "var count = RunLength(values.Slice(i));" : "var count = 1;");
                _w.Line("_inside = true;");
                _w.Line($"{ValueTaskType} dispatched;");
                _w.Line("try { dispatched = DispatchAt(values, i, count); }");
                _w.Line("catch { _inside = false; throw; }");
                _w.Line("i += count;");
                _w.Line("if (!dispatched.IsCompletedSuccessfully) return ContinueValues(dispatched, values, i);");
                _w.Line("_inside = false;");
            }

            _w.Line("return DrainQueue();");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} ContinueValues({ValueTaskType} dispatched, global::System.ReadOnlyMemory<{V}> values, int next)"))
        {
            _w.Line("try { await dispatched; } finally { _inside = false; }");
            _w.Line("await ProcessValuesFrom(values, next);");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} ProcessValuesFrom(global::System.ReadOnlyMemory<{V}> values, int start)"))
        {
            using (_w.Block("for (var i = start; i < values.Length;)"))
            {
                _w.Line("await DrainQueue();");
                _w.Line(HasRuns ? "var count = RunLength(values.Slice(i));" : "var count = 1;");
                _w.Line("_inside = true;");
                _w.Line("try { await DispatchAt(values, i, count); } finally { _inside = false; }");
                _w.Line("i += count;");
            }

            _w.Line("await DrainQueue();");
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} ProcessEventQueued{i}({Name(_events[i])} e)"))
            {
                _w.Line("await DrainQueue();");
                _w.Line("_inside = true;");
                _w.Line($"try {{ await DispatchEvent{i}(e); }} finally {{ _inside = false; }}");
                _w.Line("await DrainQueue();");
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} ProcessUnknown({TypeType} type)"))
        {
            _w.Line("if (_queue != null && _queue.Count != 0) return ProcessUnknownQueued(type);");
            _w.Line("UnhandledUnknown(type);");
            _w.Line($"return default({ValueTaskType});");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} ProcessUnknownQueued({TypeType} type)"))
        {
            _w.Line("await DrainQueue();");
            _w.Line("UnhandledUnknown(type);");
        }

        // Step 9: the events a transition queued. Nothing queued is the common case, and costs one check.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} DrainQueue()"))
        {
            _w.Line($"if (_queue == null || _queue.Count == 0) return default({ValueTaskType});");
            _w.Line(_events.Count > 0 ? "return DrainQueueAsync();" : "while (_queue.Count != 0) UnhandledUnknown(_queue.Dequeue().Unknown);");
            if (_events.Count == 0)
            {
                _w.Line($"return default({ValueTaskType});");
            }
        }

        if (_events.Count > 0)
        {
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} DrainQueueAsync()"))
            {
                using (_w.Block("while (_queue.Count != 0)"))
                {
                    _w.Line("var queued = _queue.Dequeue();");
                    _w.Line("_inside = true;");
                    using (_w.Block("try"))
                    {
                        using (_w.Block("switch (queued.Tag)"))
                        {
                            for (var i = 0; i < _events.Count; i++)
                            {
                                _w.Line($"case {i}: await DispatchEvent{i}(queued.E{i}); break;");
                            }

                            _w.Line("default: UnhandledUnknown(queued.Unknown); break;");
                        }
                    }

                    _w.Line("finally { _inside = false; }");
                }
            }
        }
    }

    /// <summary>
    /// Marks the next async method to use the pooling builder where the runtime has one (.NET 6 and later), so an
    /// action that suspends allocates nothing in steady state.
    /// </summary>
    private void AsyncMethod()
    {
        _w.Line("#if NET6_0_OR_GREATER");
        _w.Line("[global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]");
        _w.Line("#endif");
    }

    /// <summary>
    /// A single trigger's entry point, flattened: refuse, dispatch, and — when nothing was queued and the transition
    /// completed synchronously, the common case — release and return, with no further call.
    /// </summary>
    private void FastEntry(string dispatch, string queued)
    {
        _w.Line("var refused = Refuse();");
        _w.Line("if (refused != null) return Faulted(refused);");
        using (_w.Block("if (_queue == null || _queue.Count == 0)"))
        {
            _w.Line("_inside = true;");
            _w.Line($"{ValueTaskType} dispatched;");
            _w.Line($"try {{ dispatched = {dispatch}; }}");
            _w.Line($"catch ({Exception} exception) {{ _inside = false; Release(); return Faulted(exception); }}");
            _w.Line("if (!dispatched.IsCompletedSuccessfully) return FinishAfterDispatch(dispatched);");
            _w.Line("_inside = false;");
            _w.Line($"if (_queue == null || _queue.Count == 0) {{ Release(); return default({ValueTaskType}); }}");
            _w.Line("return Finish(DrainQueue());");
        }

        _w.Line($"{ValueTaskType} pending;");
        _w.Line($"try {{ pending = {queued}; }}");
        _w.Line($"catch ({Exception} exception) {{ Release(); return Faulted(exception); }}");
        _w.Line("return Finish(pending);");
    }

    /// <summary>The body of a public entry point: refuse, start, and release when done.</summary>
    private void Entry(string process)
    {
        _w.Line("var refused = Refuse();");
        _w.Line("if (refused != null) return Faulted(refused);");
        _w.Line($"{ValueTaskType} pending;");
        _w.Line($"try {{ pending = {process}; }}");
        _w.Line($"catch ({Exception} exception) {{ Release(); return Faulted(exception); }}");
        _w.Line("return Finish(pending);");
    }

    /// <summary>The dispatch <c>switch</c>es: on the active leaf, then on the value (spec §6.1, resolved at compile time).</summary>
    private void WriteDispatch()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchValue({V} value)"))
        {
            _w.Line("var v = (int)value;");
            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.Leaves)
                {
                    using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                    {
                        WriteValueCases(leaf, "value", Candidates);
                    }
                }

                _w.Line("default: return UnhandledValue(value);");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            var eventName = SymbolModelBuilder.MetadataName(_events[i]);
            _w.Line();
            using (_w.Block($"private {ValueTaskType} DispatchEvent{i}({Name(_events[i])} e)"))
            {
                using (_w.Block("switch (_leaf)"))
                {
                    foreach (var leaf in _hierarchy.Leaves)
                    {
                        var candidates = _resolver.ForEvent(leaf, eventName);
                        if (candidates.Count == 0)
                        {
                            continue;
                        }

                        using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                        {
                            Candidates(candidates, leaf, "e", $"UnhandledEvent{i}(e)");
                        }
                    }

                    _w.Line($"default: return UnhandledEvent{i}(e);");
                }
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} UnhandledValue({V} value)"))
        {
            _w.Line("OnUnhandled(_leaf, value);");
            Unhandled("value.ToString()");
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            using (_w.Block($"private {ValueTaskType} UnhandledEvent{i}({Name(_events[i])} e)"))
            {
                _w.Line("OnUnhandled(_leaf, in e);");
                Unhandled(Literal("event " + _events[i].Name));
            }
        }

        _w.Line();
        using (_w.Block($"private void UnhandledUnknown({TypeType} type)"))
        {
            if (_model.Options.Unhandled == UnhandledMode.Throw)
            {
                _w.Line($"throw new {Rt}UnhandledTriggerException(StateType, \"event \" + type.Name);");
            }
        }
    }

    private void Unhandled(string trigger)
    {
        if (_model.Options.Unhandled == UnhandledMode.Throw)
        {
            _w.Line($"throw new {Rt}UnhandledTriggerException(StateType, {trigger});");
        }
        else
        {
            _w.Line($"return default({ValueTaskType});");
        }
    }

    /// <summary>
    /// A leaf's value cases. Values with the same candidates form runs; the commonest candidate list is the
    /// <c>default</c>; short runs become <c>case</c> labels and long ones range checks, so a 16-bit value type does not
    /// write 65,536 labels.
    /// </summary>
    private void WriteValueCases(int leaf, string argument, System.Action<IReadOnlyList<TransitionModel>, int, string, string> write)
    {
        var runs = new List<(long Low, long High, IReadOnlyList<TransitionModel> Candidates, string Key)>();
        foreach (var value in _model.Options.Domain.Values)
        {
            var candidates = _resolver.ForValue(leaf, value);
            var key = string.Join(",", candidates.Select(c => c.Index));
            if (runs.Count > 0 && runs[runs.Count - 1].Key == key && runs[runs.Count - 1].High == value - 1)
            {
                var last = runs[runs.Count - 1];
                runs[runs.Count - 1] = (last.Low, value, last.Candidates, key);
            }
            else
            {
                runs.Add((value, value, candidates, key));
            }
        }

        var fallback = runs.GroupBy(r => r.Key).OrderByDescending(g => g.Sum(r => r.High - r.Low + 1)).First();
        var unhandled = $"UnhandledValue({argument})";
        foreach (var run in runs.Where(r => r.Key != fallback.Key && r.High - r.Low >= 8))
        {
            using (_w.Block($"if (v >= {run.Low} && v <= {run.High})"))
            {
                write(run.Candidates, leaf, argument, unhandled);
            }
        }

        var labelled = runs.Where(r => r.Key != fallback.Key && r.High - r.Low < 8).GroupBy(r => r.Key).ToList();
        if (labelled.Count == 0)
        {
            write(fallback.First().Candidates, leaf, argument, unhandled);
            return;
        }

        using (_w.Block("switch (v)"))
        {
            foreach (var group in labelled)
            {
                foreach (var run in group)
                {
                    for (var value = run.Low; value <= run.High; value++)
                    {
                        _w.Line($"case {value}:");
                    }
                }

                using (_w.Block(string.Empty))
                {
                    write(group.First().Candidates, leaf, argument, unhandled);
                }
            }

            _w.Line("default:");
            using (_w.Block(string.Empty))
            {
                write(fallback.First().Candidates, leaf, argument, unhandled);
            }
        }
    }

    /// <summary>
    /// The candidates for one trigger in one leaf, in the order they are tried: each guarded one if its guard passes,
    /// then the unguarded one — or, with none, the unhandled path. A decision's candidate decides; a run's is handed
    /// the value as a run of one.
    /// </summary>
    private void Candidates(IReadOnlyList<TransitionModel> candidates, int leaf, string argument, string unhandled)
    {
        foreach (var candidate in candidates)
        {
            string call;
            if (candidate.IsDecision)
            {
                _decisions.Add((candidate.Index, leaf));
                call = $"{DecisionName(candidate.Index, leaf)}({argument})";
            }
            else
            {
                _transitions.Add((candidate.Index, leaf));
                call = candidate.IsRun ? $"{TransitionName(candidate.Index, leaf)}(One({argument}))" : $"{TransitionName(candidate.Index, leaf)}({argument})";
            }

            if (candidate.IsGuarded)
            {
                _guards.Add((candidate.Index, leaf));
                _w.Line($"if ({GuardName(candidate.Index, leaf)}({argument})) return {call};");
            }
            else
            {
                _w.Line($"return {call};");
                return;
            }
        }

        _w.Line($"return {unhandled};");
    }

    private string TransitionName(int transition, int leaf) => $"T{transition}_{_stateIds[leaf]}";

    private string GuardName(int transition, int leaf) => $"G{transition}_{_stateIds[leaf]}";

    private string DecisionName(int transition, int leaf) => $"D{transition}_{_stateIds[leaf]}";
}
```

`FastEntry` writes the whole of a single trigger's entry point: refuse, claim, dispatch, and — when nothing was
queued and the transition completed synchronously, which is the common case — release and return `default`. No
helper is called and no `async` method is entered, so the stay costs the dispatch `switch` and the transform.

A call that *does* suspend now finishes in `FinishAfterDispatch`: one `async` method that awaits the transition,
runs the events it queued and releases the machine. It was two, and each one is a state machine the caller pays for.

- [ ] **Step 2: The run-of-one buffer, made when it is first needed**

`src/StateAlchemist.Generators/MachineEmitter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

/// <summary>
/// Writes a machine: a <c>partial</c> of the <c>[Machine]</c> class holding one field per state, a <c>switch</c> per
/// trigger kind, and one method per (transition, active leaf) with the steps of spec §6.2 written out in order.
/// Nothing it writes uses a dictionary, a hash or reflection. The output keeps to C# 7.3, so a <c>netstandard2.0</c>
/// application on its default language version compiles it.
/// </summary>
internal sealed partial class MachineEmitter
{
    private const string Rt = "global::StateAlchemist.";
    private const string ValueTaskType = "global::System.Threading.Tasks.ValueTask";
    private const string TypeType = "global::System.Type";

    private static readonly string[] ExceptionPhases = ["Guard", "Transform", "Exited", "Entered", "Completed"];

    private readonly SymbolMachine _machine;
    private readonly MachineModel _model;
    private readonly Hierarchy _hierarchy;
    private readonly Resolver _resolver;
    private readonly CodeWriter _w = new();
    private readonly string[] _stateIds;
    private readonly List<INamedTypeSymbol> _events;
    private readonly SortedSet<(int Transition, int Leaf)> _transitions = [];
    private readonly SortedSet<(int Transition, int Leaf)> _guards = [];

    private MachineEmitter(SymbolMachine machine)
    {
        _machine = machine;
        _model = machine.Model;
        _hierarchy = new Hierarchy(_model.States);
        _resolver = new Resolver(_model, _hierarchy);
        _stateIds = StateIds(_model.States);
        _events = machine.Events.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Value).ToList();
    }

    /// <summary>The machine's source.</summary>
    public static string Emit(SymbolMachine machine) => new MachineEmitter(machine).Write();

    private string V => Name(_machine.Value!);

    private bool IsChecked => _model.Options.Concurrency == ConcurrencyMode.Checked;

    /// <summary>
    /// Whether the machine needs the inbox and the pump (spec §6.6, §6.10): it is <c>Serialized</c>, or has an async
    /// decision, while which it must accept events from other callers. Every other machine runs each call inline.
    /// </summary>
    private bool HasInbox => _model.Options.Concurrency == ConcurrencyMode.Serialized || _model.Transitions.Any(t => t.Decision?.DecideAsync is not null);

    private bool HasRuns => _model.Transitions.Any(t => t.IsRun);

    private string Write()
    {
        _w.Line("// <auto-generated/>");
        _w.Line($"// Generated by StateAlchemist from [Machine] {_machine.Machine.Name}. Do not edit: change the declarations instead.");
        var ns = _machine.Machine.ContainingNamespace;
        var scopes = new List<IDisposable>();
        if (ns is { IsGlobalNamespace: false })
        {
            scopes.Add(_w.Block("namespace " + ns.ToDisplayString()));
        }

        foreach (var outer in Containing(_machine.Machine))
        {
            scopes.Add(_w.Block($"partial {Keyword(outer)} {outer.Name}"));
        }

        using (_w.Block($"partial class {_machine.Machine.Name} : {Rt}IMachine<{V}>"))
        {
            WriteStorage();
            WriteQueries();
            WriteDefinition();
            WriteLifecycle();
            WriteFiring();
            WriteDispatch();
            WritePlans();
            WriteRuns();
            WriteDecisions();
            WriteTransitions();
            if (HasInbox)
            {
                WriteInbox();
            }

            WriteHooks();
        }

        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            scopes[i].Dispose();
        }

        return _w.ToString();
    }

    private void WriteStorage()
    {
        _w.Line("/// <summary>The machine's states, one member each.</summary>");
        using (_w.Block("public enum StateId"))
        {
            for (var i = 0; i < _stateIds.Length; i++)
            {
                _w.Line($"/// <summary><see cref=\"{S(i)}\"/>.</summary>");
                _w.Line($"{_stateIds[i]} = {i},");
            }
        }

        _w.Line();
        for (var i = 0; i < _model.States.Count; i++)
        {
            _w.Line($"private {S(i)} {Field(i)};");
        }

        _w.Line("private StateId _leaf;");
        _w.Line($"private {Rt}MachineStatus _status;");
        _w.Line("private bool _inside;");
        _w.Line("private global::System.Threading.CancellationTokenSource _lifetime;");
        if (HasInbox)
        {
            WriteInboxStorage();
        }
        else
        {
            _w.Line("private global::System.Collections.Generic.Queue<QueuedEvent> _queue;");
            if (IsChecked)
            {
                _w.Line("private int _busy;");
            }
        }

        if (HasRuns)
        {
            _w.Line($"private {V}[] _one;");
        }

        if (_machine.Context is { } context)
        {
            _w.Line($"private readonly {Name(context)} _context;");
        }

        if (_machine.Config is { } config)
        {
            _w.Line($"private readonly {Name(config)} _config;");
        }

        _w.Line();
        var parameters = new List<string>();
        if (_machine.Context is { } c)
        {
            parameters.Add($"{Name(c)} context");
        }

        if (_machine.Config is { } g)
        {
            parameters.Add($"in {Name(g)} config");
        }

        _w.Line("/// <summary>Creates the machine in its initial state. Runs no actions: call <see cref=\"StartAsync\"/>.</summary>");
        using (_w.Block($"public {_machine.Machine.Name}({string.Join(", ", parameters)})"))
        {
            if (_machine.Context is not null)
            {
                _w.Line("_context = context;");
            }

            if (_machine.Config is not null)
            {
                _w.Line("_config = config;");
            }

            _w.Line($"_leaf = StateId.{_stateIds[_hierarchy.InitialLeaf(_hierarchy.Root)]};");
        }

        if (HasInbox)
        {
            return;
        }

        _w.Line();
        _w.Line("/// <summary>An event queued inside the machine: a tag, and the payload in the field for its type. Nothing is boxed.</summary>");
        using (_w.Block("private struct QueuedEvent"))
        {
            _w.Line("public int Tag;");
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"public {Name(_events[i])} E{i};");
            }

            _w.Line($"public {TypeType} Unknown;");
        }
    }

    private void WriteQueries()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        _w.Line($"public {Rt}MachineStatus Status {{ get {{ return _status; }} }}");
        _w.Line();
        _w.Line("/// <summary>The active leaf.</summary>");
        _w.Line("public StateId State { get { return _leaf; } }");
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {TypeType} StateType"))
        {
            using (_w.Block("get"))
            {
                _w.Line("return TypeOf(_leaf);");
            }
        }

        _w.Line();
        using (_w.Block($"private static {TypeType} TypeOf(StateId state)"))
        {
            using (_w.Block("switch (state)"))
            {
                for (var i = 0; i < _model.States.Count; i++)
                {
                    _w.Line($"case StateId.{_stateIds[i]}: return typeof({S(i)});");
                }

                _w.Line("default: throw new global::System.ArgumentOutOfRangeException(\"state\");");
            }
        }

        _w.Line();
        _w.Line("/// <summary>Whether <paramref name=\"state\"/> is the active leaf or one of its ancestors.</summary>");
        using (_w.Block("public bool IsIn(StateId state)"))
        {
            using (_w.Block("switch (state)"))
            {
                for (var i = 0; i < _model.States.Count; i++)
                {
                    var test = i == _hierarchy.Root ? "true" : string.Join(" || ", _hierarchy.LeavesUnder(i).Select(l => $"_leaf == StateId.{_stateIds[l]}"));
                    _w.Line($"case StateId.{_stateIds[i]}: return {test};");
                }

                _w.Line("default: return false;");
            }
        }

        for (var i = 0; i < _model.States.Count; i++)
        {
            _w.Line();
            _w.Line($"/// <summary>A copy of <see cref=\"{S(i)}\"/>'s data, if it is active.</summary>");
            using (_w.Block($"public bool TryGet{_stateIds[i]}(out {S(i)} value)"))
            {
                _w.Line($"if (IsIn(StateId.{_stateIds[i]})) {{ value = {Field(i)}; return true; }}");
                _w.Line($"value = default({S(i)});");
                _w.Line("return false;");
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block("public bool IsIn<TState>() where TState : struct"))
        {
            for (var i = 0; i < _model.States.Count; i++)
            {
                _w.Line($"if (typeof(TState) == typeof({S(i)})) return IsIn(StateId.{_stateIds[i]});");
            }

            _w.Line("return false;");
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block("public bool TryGetState<TState>(out TState value) where TState : struct"))
        {
            for (var i = 0; i < _model.States.Count; i++)
            {
                using (_w.Block($"if (typeof(TState) == typeof({S(i)}))"))
                {
                    _w.Line($"if (IsIn(StateId.{_stateIds[i]})) {{ value = global::System.Runtime.CompilerServices.Unsafe.As<{S(i)}, TState>(ref {Field(i)}); return true; }}");
                    _w.Line("value = default(TState);");
                    _w.Line("return false;");
                }
            }

            _w.Line("value = default(TState);");
            _w.Line("return false;");
        }
    }

    private void WriteDefinition()
    {
        _w.Line();
        _w.Line($"private static readonly {Rt}MachineDefinition s_definition = new {Rt}MachineDefinition(");
        _w.Line($"    typeof({V}),");
        _w.Line($"    new {Rt}StateDefinition[]");
        _w.Line("    {");
        foreach (var state in _model.States)
        {
            _w.Line($"        new {Rt}StateDefinition({state.Index}, typeof({S(state.Index)}), {state.Parent}, {Bool(state.IsInitial)}),");
        }

        _w.Line("    },");
        _w.Line($"    new {Rt}TransitionDefinition[]");
        _w.Line("    {");
        foreach (var t in _model.Transitions)
        {
            var usesContext = new[] { t.Guard, t.Transform }.Any(m => m is not null && m.Parameters.Any(p => p.Kind == ParameterKind.Context));
            _w.Line($"        new {Rt}TransitionDefinition({t.Index}, {Literal(t.Name)}, {t.Source}, {t.Target}, {Rt}TransitionKind.{t.Kind}, {TriggerDefinition(t.Trigger)}, " +
                    $"{t.Order}, {Bool(t.IsGuarded)}, {Bool(t.IsRun)}, {Bool(usesContext)}, {Bool(t.IsDecision)}),");
        }

        _w.Line("    });");
        _w.Line();
        _w.Line("/// <summary>The machine as data: its states, their parents, and its transitions.</summary>");
        _w.Line($"public static {Rt}MachineDefinition Definition {{ get {{ return s_definition; }} }}");
        _w.Line();
        _w.Line($"{Rt}MachineDefinition {Rt}IMachine<{V}>.Definition {{ get {{ return s_definition; }} }}");
    }

    private string TriggerDefinition(TriggerModel trigger) => trigger.Kind switch
    {
        MatchKind.Value => $"{Rt}TriggerDefinition.ForValue({trigger.Low})",
        MatchKind.Range => $"{Rt}TriggerDefinition.ForRange({trigger.Low}, {trigger.High})",
        MatchKind.Any => $"{Rt}TriggerDefinition.ForAny()",
        _ => $"{Rt}TriggerDefinition.ForEvent(typeof({Event(trigger.EventType!)}))",
    };

    private void WriteHooks()
    {
        _w.Line();
        _w.Line("/// <summary>After every transition. Implement it to trace; left unimplemented, the compiler removes every call.</summary>");
        _w.Line($"partial void OnTransitioned(in {Rt}TransitionInfo<{V}> transition);");
        _w.Line();
        _w.Line("/// <summary>A value nothing handles.</summary>");
        _w.Line($"partial void OnUnhandled(StateId state, {V} value);");
        foreach (var e in _events)
        {
            _w.Line();
            _w.Line($"/// <summary>A <see cref=\"{Name(e)}\"/> nothing handles.</summary>");
            _w.Line($"partial void OnUnhandled(StateId state, in {Name(e)} e);");
        }

        foreach (var phase in ExceptionPhases)
        {
            _w.Line();
            _w.Line($"/// <summary>A <c>{phase}</c> threw. Implemented, it chooses what the machine does; see <see cref=\"{Rt}ExceptionResolution\"/>.</summary>");
            _w.Line($"partial void On{phase}Exception(global::System.Exception exception, in {Rt}TransitionInfo<{V}> transition, ref {Rt}ExceptionResolution resolution);");
        }
    }

    /// <summary>Whether the application implements a hook: only then is its <c>try</c>/<c>catch</c> written (spec §6.9).</summary>
    private bool Implements(string hook) => _machine.Machine.GetMembers(hook).OfType<IMethodSymbol>().Any();

    private string S(int state) => Name(_machine.StateTypes[state]);

    private static string Field(int state) => "_s" + state.ToString(CultureInfo.InvariantCulture);

    private static string Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private string Event(string metadataName) => Name(_machine.Events[metadataName]);

    private int EventTag(string metadataName) => _events.FindIndex(e => SymbolModelBuilder.MetadataName(e) == metadataName);

    private string Owner(MethodModel method) => Name(_machine.Methods[method].ContainingType) + "." + method.Name;

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static IEnumerable<INamedTypeSymbol> Containing(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            chain.Insert(0, outer);
        }

        return chain;
    }

    private static string Keyword(INamedTypeSymbol type) => (type.IsRecord, type.TypeKind) switch
    {
        (true, TypeKind.Struct) => "record struct",
        (true, _) => "record",
        (_, TypeKind.Struct) => "struct",
        (_, TypeKind.Interface) => "interface",
        _ => "class",
    };

    /// <summary>Each state's <c>StateId</c> member: its short name, or its full name when two states share a short name.</summary>
    private static string[] StateIds(IReadOnlyList<StateModel> states)
    {
        var shared = new HashSet<string>(states.GroupBy(s => s.Name).Where(g => g.Count() > 1).Select(g => g.Key));
        return states.Select(s => shared.Contains(s.Name) ? s.TypeName.Replace('.', '_').Replace('+', '_') : s.Name).ToArray();
    }
}
```

`src/StateAlchemist.Generators/MachineEmitter.Runs.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary>
    /// Runs (spec §6.7). A value belongs to a leaf's run when resolution tries the run transition first for it — the
    /// stop set's complement — so <c>RunOf</c> is the dispatch <c>switch</c> answering "which run, if any", and a
    /// batch hands everything up to the first value with a different answer to one call.
    /// </summary>
    private void WriteRuns()
    {
        if (!HasRuns)
        {
            return;
        }

        var runs = new List<(int Leaf, int Transition)>();
        _w.Line();
        _w.Line("/// <summary>The run transition the active leaf gives <paramref name=\"value\"/> to first, or −1.</summary>");
        using (_w.Block($"private int RunOf({V} value)"))
        {
            _w.Line("var v = (int)value;");
            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.Leaves)
                {
                    var first = _model.Options.Domain.Values.Select(value => _resolver.ForValue(leaf, value)).Where(c => c.Count > 0 && c[0].IsRun).Select(c => c[0].Index).Distinct().ToList();
                    if (first.Count == 0)
                    {
                        continue;
                    }

                    runs.AddRange(first.Select(t => (leaf, t)));
                    using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                    {
                        WriteValueCases(leaf, "value", (candidates, _, _, _) => _w.Line($"return {(candidates.Count > 0 && candidates[0].IsRun ? candidates[0].Index : -1)};"));
                    }
                }

                _w.Line("default: return -1;");
            }
        }

        // On .NET 8 and later, a byte or char run ends at the first value of its stop set, found by a vectorised
        // search; elsewhere, by asking RunOf value by value. Both give the same answer: the stop set is exactly the
        // values RunOf does not give to this run in this leaf.
        var element = _model.Options.ValueType switch
        {
            "System.Byte" => "byte",
            "System.Char" => "char",
            _ => null,
        };
        if (element is not null)
        {
            _w.Line();
            _w.Line("#if NET8_0_OR_GREATER");
            foreach (var (leaf, transition) in runs)
            {
                var stops = _model.Options.Domain.Values.Where(value => _resolver.ForValue(leaf, value) is var c && (c.Count == 0 || c[0].Index != transition)).ToList();
                var literal = string.Join(", ", stops.Select(v => element == "char" ? $"(char){v}" : v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                _w.Line($"private static readonly global::System.Buffers.SearchValues<{element}> s_stop{transition}_{_stateIds[leaf]} = global::System.Buffers.SearchValues.Create(new {element}[] {{ {literal} }});");
            }

            _w.Line("#endif");
        }

        _w.Line();
        _w.Line("/// <summary>How many values from the start of <paramref name=\"values\"/> go to one call: a whole run, or one value.</summary>");
        using (_w.Block($"private int RunLength(global::System.ReadOnlyMemory<{V}> values)"))
        {
            _w.Line("var span = values.Span;");
            _w.Line("var run = RunOf(span[0]);");
            _w.Line("if (run < 0) return 1;");
            if (element is not null)
            {
                _w.Line("#if NET8_0_OR_GREATER");
                _w.Line("int stop;");
                using (_w.Block("switch (_leaf)"))
                {
                    foreach (var (leaf, transition) in runs)
                    {
                        _w.Line($"case StateId.{_stateIds[leaf]} when run == {transition}: stop = global::System.MemoryExtensions.IndexOfAny(span.Slice(1), s_stop{transition}_{_stateIds[leaf]}); return stop < 0 ? span.Length : stop + 1;");
                    }
                }

                _w.Line("#endif");
            }

            _w.Line("var count = 1;");
            _w.Line("while (count < span.Length && RunOf(span[count]) == run) count++;");
            _w.Line("return count;");
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchRun(global::System.ReadOnlyMemory<{V}> run)"))
        {
            using (_w.Block("switch (RunOf(run.Span[0]))"))
            {
                foreach (var group in runs.GroupBy(r => r.Transition))
                {
                    using (_w.Block($"case {group.Key}:"))
                    {
                        using (_w.Block("switch (_leaf)"))
                        {
                            foreach (var (leaf, transition) in group)
                            {
                                _transitions.Add((transition, leaf));
                                _w.Line($"case StateId.{_stateIds[leaf]}: return {TransitionName(transition, leaf)}(run);");
                            }
                        }

                        _w.Line("break;");
                    }
                }
            }

            _w.Line("return DispatchValue(run.Span[0]);");
        }

        _w.Line();
        _w.Line("/// <summary>A single value as a run of one. The buffer is made the first time and reused: one trigger runs at a time.</summary>");
        _w.Line($"private global::System.ReadOnlyMemory<{V}> One({V} value) {{ var one = _one ?? (_one = new {V}[1]); one[0] = value; return one; }}");
    }
}
```

A machine with runs allocated a one-element array in its constructor, which spec §9 forbids: construction allocates
the instance and nothing else. It is made on the first single value that reaches a run transition instead, and
reused after — one trigger runs at a time.

- [ ] **Step 3: The pooled builder on every async method a machine writes**

A transition whose action suspends, and a start or stop that awaits one, are `async` methods like any other the
machine writes: on `net6.0` and later their state machines are pooled too.

`src/StateAlchemist.Generators/MachineEmitter.Transitions.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private enum Use
    {
        Guard,
        Transform,
        Decide,
        Exited,
        Entered,
        Completed,
    }

    /// <summary>Every (transition, leaf) pair the dispatch reaches: its guard, if any, and its steps.</summary>
    private void WriteTransitions()
    {
        foreach (var (index, leaf) in _guards)
        {
            WriteGuard(_model.Transitions[index], leaf);
        }

        foreach (var (index, leaf) in _transitions)
        {
            var transition = _model.Transitions[index];
            WriteSteps(TransitionName(index, leaf), TriggerParameter(transition), transition, PathPlanner.Plan(_hierarchy, transition, leaf),
                transition.Transform, transition.Completed, transition.Kind.ToString(), outcome: null);
        }
    }

    private void WriteGuard(TransitionModel transition, int leaf)
    {
        var path = PathPlanner.Plan(_hierarchy, transition, leaf);
        var call = $"{Owner(transition.Guard!)}({Arguments(transition.Guard!, Use.Guard, transition, path, startedOver: [])})";
        _w.Line();
        using (_w.Block($"private bool {GuardName(transition.Index, leaf)}({TriggerParameter(transition, forGuard: true)})"))
        {
            if (!Implements("OnGuardException"))
            {
                _w.Line($"return {call};");
                return;
            }

            _w.Line($"try {{ return {call}; }}");
            using (_w.Block($"catch ({Exception} exception)"))
            {
                _w.Line($"var resolution = {Rt}ExceptionResolution.Rethrow;");
                _w.Line($"OnGuardException(exception, {Info(transition, path, "Guard")}, ref resolution);");
                _w.Line($"if (resolution == {Rt}ExceptionResolution.Rethrow) throw;");
                _w.Line("return false;");
            }
        }
    }

    /// <summary>
    /// Steps 2–8 of spec §6.2 for one transition from one leaf: snapshot what is started over, reset what is entered,
    /// transform, commit, run the actions, clear what was left. With <paramref name="outcome"/>, a decision outcome:
    /// its <c>Complete</c> is the transform and it receives the outcome.
    /// </summary>
    private void WriteSteps(string name, string parameters, TransitionModel transition, TransitionPath path, MethodModel? transform,
        IReadOnlyList<MethodModel> completed, string kind, string? outcome)
    {
        var startedOver = new HashSet<int>(path.Exiting.Intersect(path.Entering));
        var actions = Actions(completed, path);
        var isAsync = actions.Any(a => a.Method.IsAsync);
        var skip = actions.Any(a => Implements($"On{a.Phase}Exception"));
        var done = isAsync ? "return;" : $"return default({ValueTaskType});";
        var transformPhase = outcome is null ? "Transform" : "Complete";

        _w.Line();
        _w.Line($"// {transition.Name}: {_model.States[path.Leaf].Name} --[{transition.Trigger}]--> {_model.States[path.TargetLeaf].Name}");
        if (isAsync)
        {
            AsyncMethod();
        }

        using (_w.Block($"private {(isAsync ? "async " : "")}{ValueTaskType} {name}({parameters})"))
        {
            if (transition.IsRun)
            {
                _w.Line("var value = run.Span[0];");
            }

            foreach (var state in startedOver)
            {
                _w.Line($"var old{state} = {Field(state)};");
            }

            // 2. reset the entering states
            foreach (var state in path.Entering)
            {
                Clear(state);
            }

            // 3. transform: nothing has committed, so a failure puts back what step 2 started over
            if (transform is not null)
            {
                var call = transition.IsRun
                    ? $"{name}_Run(run);"
                    : $"{Owner(transform)}({Arguments(transform, Use.Transform, transition, path, startedOver)});";
                var hook = Implements("OnTransformException");
                if (startedOver.Count == 0 && !hook)
                {
                    _w.Line(call);
                }
                else
                {
                    _w.Line($"try {{ {call} }}");
                    using (_w.Block(hook ? $"catch ({Exception} exception)" : "catch"))
                    {
                        foreach (var state in startedOver)
                        {
                            _w.Line($"{Field(state)} = old{state};");
                        }

                        if (hook)
                        {
                            _w.Line($"var resolution = {Rt}ExceptionResolution.Rethrow;");
                            _w.Line($"OnTransformException(exception, {Info(transition, path, transformPhase, kind: kind)}, ref resolution);");
                            _w.Line($"if (resolution != {Rt}ExceptionResolution.Rethrow) {done}");
                        }

                        _w.Line("throw;");
                    }
                }
            }

            // 4. commit. A move leaves any pending decision's state, which sits below the leaf: the decision ends.
            _w.Line($"_leaf = StateId.{_stateIds[path.TargetLeaf]};");
            if (Deciding && path.Exiting.Count > 0)
            {
                _w.Line("lock (_sync) { if (_pending != null) EndPending(_pending); }");
            }

            var cleared = path.Exiting.Where(s => !startedOver.Contains(s)).ToList();
            if (actions.Count > 0 || cleared.Count > 0)
            {
                // 5–7. exited, entered, completed; 8. clear, whatever happens
                using (_w.Block("try"))
                {
                    foreach (var action in actions)
                    {
                        WriteAction(transition, path, startedOver, action, kind);
                    }
                }

                using (_w.Block("finally"))
                {
                    foreach (var state in cleared)
                    {
                        Clear(state);
                    }
                }

                if (skip)
                {
                    _w.Line("skipped:");
                }
            }

            _w.Line($"OnTransitioned({Info(transition, path, "Completed", kind: kind)});");
            _w.Line(done);
        }

        if (transition.IsRun && transform is not null)
        {
            // A span cannot live in an async method, so the run's transform is called from a synchronous one.
            _w.Line();
            using (_w.Block($"private void {name}_Run(global::System.ReadOnlyMemory<{V}> run)"))
            {
                _w.Line($"{Owner(transform)}({Arguments(transform, Use.Transform, transition, path, startedOver)});");
            }
        }
    }

    /// <summary>One action of the transition, and — if the application implements its phase's hook — its recovery.</summary>
    private void WriteAction(TransitionModel transition, TransitionPath path, HashSet<int> startedOver, TransitionAction action, string kind)
    {
        var call = $"{Owner(action.Method)}({Arguments(action.Method, action.Use, transition, path, startedOver, action.State, kind)})";
        var statement = action.Method.IsAsync ? $"await {call};" : $"{call};";
        if (!Implements($"On{action.Phase}Exception"))
        {
            _w.Line(statement);
            return;
        }

        _w.Line($"try {{ {statement} }}");
        using (_w.Block($"catch ({Exception} exception)"))
        {
            _w.Line($"var resolution = {Rt}ExceptionResolution.Rethrow;");
            _w.Line($"On{action.Phase}Exception(exception, {Info(transition, path, action.Phase, action.State, kind)}, ref resolution);");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Skip) goto skipped;");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Rethrow) throw;");
        }
    }

    /// <summary>The actions in order: each exited state's, leaf first; each entered state's, outermost first; then <paramref name="completed"/>.</summary>
    private List<TransitionAction> Actions(IReadOnlyList<MethodModel> completed, TransitionPath path)
    {
        var actions = new List<TransitionAction>();
        foreach (var state in path.Exiting)
        {
            actions.AddRange(StateActions(ActionPhase.Exited, state).Select(m => new TransitionAction(m, Use.Exited, "Exited", state)));
        }

        foreach (var state in path.Entering)
        {
            actions.AddRange(StateActions(ActionPhase.Entered, state).Select(m => new TransitionAction(m, Use.Entered, "Entered", state)));
        }

        actions.AddRange(completed.Select(m => new TransitionAction(m, Use.Completed, "Completed", -1)));
        return actions;
    }

    /// <summary>A state's actions for one phase: by <c>Order</c>, then by include order of their modules, then as declared.</summary>
    private IEnumerable<MethodModel> StateActions(ActionPhase phase, int state) => _model.StateActions
        .Where(a => a.Phase == phase && a.State == state)
        .OrderBy(a => a.Order)
        .ThenBy(a => ModuleOrder(a.Module))
        .ThenBy(a => a.DeclarationIndex)
        .Select(a => a.Method);

    private int ModuleOrder(string module)
    {
        var index = -1;
        for (var i = 0; i < _machine.Modules.Count; i++)
        {
            if (_machine.Modules[i] == module)
            {
                index = i;
                break;
            }
        }

        return index < 0 ? int.MaxValue : index;
    }

    private void Clear(int state) => _w.Line(_model.States[state].HasReset ? $"{Field(state)}.Reset();" : $"{Field(state)} = default({S(state)});");

    /// <summary>
    /// The arguments for one method. A state being started over is read from its snapshot — by a transform's
    /// <c>in</c> parameter and by its own <c>[Exited]</c> actions; everything else reads the live field.
    /// </summary>
    private string Arguments(MethodModel method, Use use, TransitionModel transition, TransitionPath path, HashSet<int> startedOver, int actionState = -1, string? kind = null)
    {
        var arguments = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            var modifier = parameter.Passing switch
            {
                Passing.Ref => "ref ",
                Passing.In => "in ",
                _ => string.Empty,
            };
            arguments.Add(parameter.Kind switch
            {
                ParameterKind.State => modifier + StateArgument(parameter, use, startedOver),
                ParameterKind.Value => "value",
                ParameterKind.Event => modifier + "e",
                ParameterKind.Run => "run.Span",
                ParameterKind.RunMemory => "run",
                ParameterKind.Outcome => "outcome",
                ParameterKind.Context => "_context",
                ParameterKind.Config => modifier + "_config",
                ParameterKind.CancellationToken => use == Use.Decide ? "pending.Cancellation.Token" : "LifetimeToken",
                ParameterKind.TransitionInfo => Info(transition, path, Phase(use), actionState, kind),
                _ => "default",
            });
        }

        return string.Join(", ", arguments);
    }

    private static string StateArgument(ParameterModel parameter, Use use, HashSet<int> startedOver)
    {
        var old = (use == Use.Transform && parameter.Passing == Passing.In) || use == Use.Exited;
        return old && startedOver.Contains(parameter.State) ? "old" + parameter.State : Field(parameter.State);
    }

    private static string Phase(Use use) => use switch
    {
        Use.Guard => "Guard",
        Use.Transform => "Transform",
        Use.Decide => "Decide",
        Use.Exited => "Exited",
        Use.Entered => "Entered",
        _ => "Completed",
    };

    /// <summary>The <c>TransitionInfo</c> for one phase of a transition fired from <paramref name="path"/>'s leaf.</summary>
    private string Info(TransitionModel transition, TransitionPath path, string phase, int state = -1, string? kind = null)
    {
        var isEvent = transition.Trigger.Kind == MatchKind.Event;
        return $"new {Rt}TransitionInfo<{V}>({Literal(transition.Name)}, typeof({S(transition.Source)}), typeof({S(path.Leaf)}), typeof({S(path.TargetLeaf)}), " +
               $"{Rt}TransitionKind.{kind ?? transition.Kind.ToString()}, {Rt}Phase.{phase}, {(isEvent ? $"default({V})" : "value")}, {Bool(!isEvent)}, " +
               $"{(isEvent ? $"typeof({Event(transition.Trigger.EventType!)})" : "null")}, {(state < 0 ? "null" : $"typeof({S(state)})")})";
    }

    /// <summary>What fires a transition, as its generated methods take it: the value, the event, or a run.</summary>
    private string TriggerParameter(TransitionModel transition, bool forGuard = false) =>
        transition.Trigger.Kind == MatchKind.Event ? $"{Event(transition.Trigger.EventType!)} e"
        : transition.IsRun && !forGuard ? $"global::System.ReadOnlyMemory<{V}> run"
        : $"{V} value";

    private sealed record TransitionAction(MethodModel Method, Use Use, string Phase, int State);
}
```

`src/StateAlchemist.Generators/MachineEmitter.Lifecycle.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary><c>StartAsync</c>, <c>StopAsync</c> and <c>DisposeAsync</c> (spec D22), and the lifetime token actions may take.</summary>
    private void WriteLifecycle()
    {
        var initialPath = _hierarchy.PathFromRoot(_hierarchy.InitialLeaf(_hierarchy.Root));
        var starting = initialPath.SelectMany(s => StateActions(ActionPhase.Entered, s).Select(m => new TransitionAction(m, Use.Entered, "Entered", s))).ToList();

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} StartAsync()"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.NotStarted) return Faulted(new global::System.InvalidOperationException(\"The machine has already been started.\"));");
            _w.Line("return Start();");
        }

        WriteLifecycleActions("Start", starting, $"_status = {Rt}MachineStatus.Running;", null);

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} StopAsync()"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) {{ _status = {Rt}MachineStatus.Stopped; return default({ValueTaskType}); }}");
            _w.Line(HasInbox ? "Abandon();" : $"_status = {Rt}MachineStatus.Stopped;");
            _w.Line("if (_lifetime != null) _lifetime.Cancel();");
            _w.Line("return Stop();");
        }

        var stopping = _hierarchy.Leaves.ToDictionary(
            leaf => leaf,
            leaf => _hierarchy.PathFromRoot(leaf).Reverse()
                .SelectMany(s => StateActions(ActionPhase.Exited, s).Select(m => new TransitionAction(m, Use.Exited, "Exited", s))).ToList());
        WriteLifecycleActions("Stop", stopping.Values.SelectMany(a => a).ToList(), null, stopping);

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        _w.Line($"public {ValueTaskType} DisposeAsync() {{ return StopAsync(); }}");

        _w.Line();
        using (_w.Block("private global::System.Threading.CancellationToken LifetimeToken"))
        {
            using (_w.Block("get"))
            {
                _w.Line($"if (_status == {Rt}MachineStatus.Stopped) return new global::System.Threading.CancellationToken(true);");
                _w.Line("if (_lifetime == null) _lifetime = new global::System.Threading.CancellationTokenSource();");
                _w.Line("return _lifetime.Token;");
            }
        }
    }

    /// <summary>
    /// A lifecycle's actions, run inside the machine so they may <c>Enqueue</c>. Their exception hooks apply as in a
    /// transition; <c>Skip</c> skips the rest.
    /// </summary>
    private void WriteLifecycleActions(string name, List<TransitionAction> all, string? after, Dictionary<int, List<TransitionAction>>? byLeaf)
    {
        var isAsync = all.Any(a => a.Method.IsAsync);
        var skip = all.Any(a => Implements($"On{a.Phase}Exception"));
        _w.Line();
        if (isAsync)
        {
            AsyncMethod();
        }

        using (_w.Block($"private {(isAsync ? "async " : "")}{ValueTaskType} {name}()"))
        {
            if (all.Count > 0)
            {
                _w.Line("_inside = true;");
                using (_w.Block("try"))
                {
                    if (byLeaf is null)
                    {
                        foreach (var action in all)
                        {
                            WriteLifecycleAction(action);
                        }
                    }
                    else
                    {
                        using (_w.Block("switch (_leaf)"))
                        {
                            foreach (var pair in byLeaf.Where(p => p.Value.Count > 0))
                            {
                                _w.Line($"case StateId.{_stateIds[pair.Key]}:");
                                foreach (var action in pair.Value)
                                {
                                    WriteLifecycleAction(action);
                                }

                                _w.Line("break;");
                            }
                        }
                    }
                }

                _w.Line("finally { _inside = false; }");
                if (skip)
                {
                    _w.Line("skipped:");
                }
            }

            if (after is not null)
            {
                _w.Line(after);
            }

            _w.Line(isAsync ? "return;" : $"return default({ValueTaskType});");
        }
    }

    private void WriteLifecycleAction(TransitionAction action)
    {
        var arguments = string.Join(", ", action.Method.Parameters.Select(p => p.Kind switch
        {
            ParameterKind.State => (p.Passing == Passing.In ? "in " : string.Empty) + Field(p.State),
            ParameterKind.Context => "_context",
            ParameterKind.Config => (p.Passing == Passing.In ? "in " : string.Empty) + "_config",
            ParameterKind.CancellationToken => "LifetimeToken",
            ParameterKind.TransitionInfo => LifecycleInfo(action),
            _ => "default",
        }));
        var call = $"{Owner(action.Method)}({arguments})";
        var statement = action.Method.IsAsync ? $"await {call};" : $"{call};";
        if (!Implements($"On{action.Phase}Exception"))
        {
            _w.Line(statement);
            return;
        }

        _w.Line($"try {{ {statement} }}");
        using (_w.Block($"catch ({Exception} exception)"))
        {
            _w.Line($"var resolution = {Rt}ExceptionResolution.Rethrow;");
            _w.Line($"On{action.Phase}Exception(exception, {LifecycleInfo(action)}, ref resolution);");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Skip) goto skipped;");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Rethrow) throw;");
        }
    }

    private string LifecycleInfo(TransitionAction action) =>
        $"new {Rt}TransitionInfo<{V}>(\"(lifecycle)\", typeof({S(_hierarchy.Root)}), StateType, StateType, {Rt}TransitionKind.Stay, {Rt}Phase.{action.Phase}, default({V}), false, null, typeof({S(action.State)}))";
}
```

- [ ] **Step 4: Test the paths a modern application compiles**

The generated `#if NET6_0_OR_GREATER` and `#if NET8_0_OR_GREATER` paths — the pooled builder, the vectorised run
scan — are the ones an application actually builds, so the generator tests define those symbols and compile them.

`tests/StateAlchemist.Generators.Tests/TestCompilation.cs`:

```csharp
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

    /// <summary>What a modern application defines: generated code's .NET 8 paths — the pooling builder, vectorised runs — compile in.</summary>
    public static readonly string[] Net8Symbols = ["NET6_0_OR_GREATER", "NET8_0_OR_GREATER"];

    public static CSharpCompilation Create(LanguageVersion language, params string[] sources) => Create(language, [], sources);

    public static CSharpCompilation Create(LanguageVersion language, string[] symbols, params string[] sources) => CSharpCompilation.Create(
        "App",
        sources.Select((source, i) => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(language, preprocessorSymbols: symbols), path: $"App{i}.cs")),
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
```

`tests/StateAlchemist.Generators.Tests/Agreement/RandomMachineTests.cs`:

```csharp
extern alias generator;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StateAlchemist.Reference;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Agreement;

/// <summary>
/// The generated machine and the reference interpreter agree on random machines (spec §10): for any tree and any
/// batch of triggers, the same state, the same data, the same actions in the same order, and the same plans.
/// </summary>
public class RandomMachineTests
{
    private const int Machines = 60;
    private const int Triggers = 60;

    [Test]
    public async Task GeneratedAndInterpretedMachinesAgreeOnRandomTrees()
    {
        var checkedMachines = 0;
        var withRuns = 0;
        var seed = 0;
        while (checkedMachines < Machines)
        {
            seed++;
            var source = RandomMachineSource.Generate(seed);
            if (Compile(source) is not { } assembly)
            {
                continue; // an invalid random machine — a conflict, an unreachable initial child — is skipped, not tested
            }

            checkedMachines++;
            withRuns += source.Contains(", Run]") ? 1 : 0;
            var disagreement = await Compare(assembly, seed);
            await Assert.That(disagreement).IsEqualTo("").Because($"seed {seed}:\n{source}");
        }

        await Assert.That(seed).IsLessThan(Machines * 4).Because("most random machines should be valid");
        await Assert.That(withRuns).IsGreaterThanOrEqualTo(Machines / 6).Because("runs must be among what agrees");
    }

    /// <summary>Compiles <paramref name="source"/> with the generator, or returns <see langword="null"/> if the machine has errors.</summary>
    private static Assembly? Compile(string source)
    {
        var compilation = TestCompilation.Create(LanguageVersion.Latest, TestCompilation.Net8Symbols, source);
        CSharpGeneratorDriver.Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        using var image = new MemoryStream();
        var emitted = output.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("The generated code does not compile:\n" + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        image.Position = 0;
        return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(image);
    }

    private static async Task<string> Compare(Assembly assembly, int seed)
    {
        var machineType = assembly.GetType($"{RandomMachineSource.Namespace}.RandomMachine")!;
        var logType = assembly.GetType($"{RandomMachineSource.Namespace}.Log")!;
        var stateTypes = assembly.GetTypes().Where(t => t.IsValueType && !t.IsNested && !t.IsEnum && t.Namespace == RandomMachineSource.Namespace).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        var module = assembly.GetType($"{RandomMachineSource.Namespace}.RandomModule")!;

        var generatedLog = Activator.CreateInstance(logType)!;
        var interpretedLog = Activator.CreateInstance(logType)!;
        var generated = (IMachine<byte>)Activator.CreateInstance(machineType, generatedLog)!;
        var reflected = ReflectionModelBuilder.Build(new MachineSpec("RandomMachine", assembly.GetType($"{RandomMachineSource.Namespace}.S0"), typeof(byte), [module], logType));
        var interpreted = ReferenceMachine<byte>.Create(reflected, interpretedLog);

        await generated.StartAsync();
        await interpreted.StartAsync();
        var random = new Random(seed * 7919);
        for (var step = 0; step <= Triggers; step++)
        {
            if (Describe(generated, generatedLog, stateTypes) is var g && Describe(interpreted, interpretedLog, stateTypes) is var i && g != i)
            {
                return $"after {step} triggers\n generated:   {g}\n interpreted: {i}";
            }

            // A batch of one to four values, so runs form; the plan is compared for its first value.
            var batch = Enumerable.Range(0, random.Next(1, 5)).Select(_ => (byte)random.Next(8)).ToArray();
            if (generated.Plan(batch[0]).Transition != interpreted.Plan(batch[0]).Transition)
            {
                return $"step {step}: the plans for {batch[0]} differ: {generated.Plan(batch[0]).Transition} and {interpreted.Plan(batch[0]).Transition}";
            }

            await generated.FireAsync(batch);
            await interpreted.FireAsync(batch);
        }

        return "";
    }

    /// <summary>The active leaf, every active state's value, and the log — everything the two machines must agree on.</summary>
    private static string Describe(IMachine<byte> machine, object log, List<Type> stateTypes)
    {
        var values = stateTypes.Select(t =>
        {
            var arguments = new object?[] { null };
            var active = (bool)typeof(IMachine<byte>).GetMethod(nameof(IMachine<byte>.TryGetState))!.MakeGenericMethod(t).Invoke(machine, arguments)!;
            return active ? $"{t.Name}={t.GetField("Value")!.GetValue(arguments[0])}" : null;
        }).OfType<string>();
        var entries = (List<string>)log.GetType().GetField("Entries")!.GetValue(log)!;
        return $"{machine.StateType.Name} [{string.Join(" ", values)}] log: {string.Join(" | ", entries)}";
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: PASS — every contract, every agreement test, and the synchronous allocation tests. The inbox machines'
allocation tests still fail.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Flatten the entry points of a machine without an inbox"
```

---

### Task 4: The inbox, without the allocations

**Files:**
- Modify: `src/StateAlchemist.Generators/MachineEmitter.Inbox.cs`
- Create: `tests/StateAlchemist.Generated.Tests/Performance/BoundedInboxTests.cs`

**Interfaces:**
- Consumes: Plan 5's inbox — the rules are unchanged and are stated in `ReferenceMachine.Inbox.cs`.
- Produces: `Input` as the caller's `IValueTaskSource`; `RunInline`; `InboxCapacity` honoured (spec §6.10).

- [ ] **Step 1: The inbox**

`src/StateAlchemist.Generators/MachineEmitter.Inbox.cs`:

```csharp
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// The inbox and the pump, written into machines that are Serialized or have an async decision — the design the
// reference interpreter runs (Plan 3), in generated C# 7.3. Every FireAsync is an input; whoever finds the machine
// idle pumps, one trigger per step; a pending decision pauses the input that started it while the machine accepts
// the events it handles. See ReferenceMachine.Inbox.cs for the rules, stated once.
//
// For speed (Plan 6): inputs are pooled and are themselves the IValueTaskSource a caller awaits, so a call allocates
// nothing in steady state; the pump runs synchronously and continues asynchronously only when a step suspends; and
// the AsyncLocal that catches self-firing is set only while a decision is pending, which is rare. The
// pending-decision parts are written only into machines that have an async decision.
internal sealed partial class MachineEmitter
{
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Sources = "global::System.Threading.Tasks.Sources.";

    /// <summary>Whether the machine has an async decision: only then does its inbox carry a pending state.</summary>
    private bool Deciding => _model.Transitions.Any(t => t.Decision?.DecideAsync is not null);

    /// <summary>A Serialized machine's bounded inbox (spec §6.10): callers wait for room instead of growing the queue.</summary>
    private bool Bounded => _model.Options.Concurrency == ConcurrencyMode.Serialized && _model.Options.InboxCapacity > 0;

    private void WriteInboxStorage()
    {
        _w.Line("private readonly object _sync = new object();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _inbox = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _queued = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private readonly global::System.Collections.Generic.Stack<Input> _pool = new global::System.Collections.Generic.Stack<Input>();");
        _w.Line("private Input _spare;");
        _w.Line("private Input _current;");
        _w.Line("private bool _pumping;");
        if (Bounded)
        {
            _w.Line($"private readonly global::System.Threading.SemaphoreSlim _room = new global::System.Threading.SemaphoreSlim({_model.Options.InboxCapacity}, {_model.Options.InboxCapacity});");
        }

        if (IsChecked)
        {
            _w.Line("private bool _busy;");
        }

        if (Deciding)
        {
            _w.Line("private readonly global::System.Collections.Generic.List<Pending> _ended = new global::System.Collections.Generic.List<Pending>();");
            _w.Line("private readonly global::System.Threading.AsyncLocal<object> _flow = new global::System.Threading.AsyncLocal<object>();");
            _w.Line("private static readonly object s_step = new object();");
            _w.Line("private Input _owner;");
            _w.Line("private Pending _pending;");
            _w.Line("private Pending _started;");
        }
    }

    private void WriteInbox()
    {
        WriteInboxTypes();
        WritePool();
        WriteSubmit();
        WritePump();
        WritePumpSteps();
        WriteEndings();
    }

    private void WriteInboxTypes()
    {
        var machine = _machine.Machine.Name;
        _w.Line();
        _w.Line("/// <summary>One caller's input — a batch of values, or one event — or an event queued inside the machine. Pooled; the caller awaits it directly.</summary>");
        using (_w.Block($"private sealed class Input : {Sources}IValueTaskSource"))
        {
            _w.Line($"public readonly {machine} Machine;");
            _w.Line($"public readonly {V}[] Single = new {V}[1];");
            _w.Line($"public global::System.ReadOnlyMemory<{V}> Values;");
            _w.Line("public int Next;");
            _w.Line("public int Tag = -2;");
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"public {Name(_events[i])} E{i};");
            }

            _w.Line($"public {TypeType} Unknown;");
            _w.Line("public bool HasCaller;");
            _w.Line("public int Settled;");
            if (IsChecked)
            {
                _w.Line("public bool HoldsBusy;");
            }

            if (Deciding)
            {
                _w.Line("public bool ArrivedWhilePending;");
            }

            _w.Line($"public {Sources}ManualResetValueTaskSourceCore<bool> Core;");
            _w.Line($"public Input({machine} machine) {{ Machine = machine; Core.RunContinuationsAsynchronously = true; }}");
            _w.Line("public bool IsEvent { get { return Tag != -2; } }");
            _w.Line($"public {Sources}ValueTaskSourceStatus GetStatus(short token) {{ return Core.GetStatus(token); }}");
            _w.Line($"public void OnCompleted(global::System.Action<object> continuation, object state, short token, {Sources}ValueTaskSourceOnCompletedFlags flags) {{ Core.OnCompleted(continuation, state, token, flags); }}");
            _w.Line("public void GetResult(short token) { try { Core.GetResult(token); } finally { Machine.Return(this); } }");
        }

        _w.Line();
        _w.Line(Deciding ? "private enum Step { None, Continue, Event, Apply }" : "private enum Step { None, Continue, Event }");
        if (!Deciding)
        {
            return;
        }

        var decisions = _model.Transitions.Where(t => t.Decision?.DecideAsync is not null).ToList();
        _w.Line();
        _w.Line("/// <summary>The pending state's slot: the decision in flight, and what cancels it.</summary>");
        using (_w.Block("private sealed class Pending"))
        {
            _w.Line("public int Decision;");
            _w.Line("public string Name;");
            _w.Line("public Input Owner;");
            if (decisions.Any(t => t.Trigger.Kind != MatchKind.Event))
            {
                _w.Line($"public {V} Value;");
            }

            if (decisions.Any(t => t.Trigger.Kind == MatchKind.Event))
            {
                _w.Line("public object Event;");
            }

            _w.Line("public readonly global::System.Threading.CancellationTokenSource Cancellation = new global::System.Threading.CancellationTokenSource();");
            _w.Line("public bool HasResult;");
            _w.Line("public object Outcome;");
            _w.Line($"public {Exception} Failure;");
        }
    }

    private void WritePool()
    {
        _w.Line();
        using (_w.Block("private Input Rent()"))
        {
            _w.Line("var spare = global::System.Threading.Interlocked.Exchange(ref _spare, null);");
            _w.Line("if (spare != null) return spare;");
            _w.Line("lock (_sync) { if (_pool.Count > 0) return _pool.Pop(); }");
            _w.Line("return new Input(this);");
        }

        _w.Line();
        using (_w.Block("private void Return(Input input)"))
        {
            _w.Line("input.Values = default(global::System.ReadOnlyMemory<" + V + ">);");
            _w.Line("input.Next = 0;");
            _w.Line("input.Tag = -2;");
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"input.E{i} = default({Name(_events[i])});");
            }

            _w.Line("input.Unknown = null;");
            _w.Line("input.HasCaller = false;");
            _w.Line("input.Settled = 0;");
            if (IsChecked)
            {
                _w.Line("input.HoldsBusy = false;");
            }

            if (Deciding)
            {
                _w.Line("input.ArrivedWhilePending = false;");
            }

            _w.Line("input.Core.Reset();");
            _w.Line("if (global::System.Threading.Interlocked.CompareExchange(ref _spare, input, null) == null) return;");
            _w.Line("lock (_sync) { if (_pool.Count < 16) _pool.Push(input); }");
        }

        // Settling an input releases the busy flag it holds, then completes its caller's ValueTask — or, for an event
        // queued inside the machine, which has no caller, returns it to the pool. Exactly once: the pump and Abandon may race.
        _w.Line();
        using (_w.Block("private void Succeed(Input input)"))
        {
            _w.Line("if (global::System.Threading.Interlocked.Exchange(ref input.Settled, 1) != 0) return;");
            if (IsChecked)
            {
                _w.Line("if (input.HoldsBusy) { lock (_sync) { _busy = false; } }");
            }

            _w.Line("if (input.HasCaller) input.Core.SetResult(true); else Return(input);");
        }

        _w.Line();
        using (_w.Block($"private void Fault(Input input, {Exception} exception)"))
        {
            _w.Line("if (global::System.Threading.Interlocked.Exchange(ref input.Settled, 1) != 0) return;");
            if (IsChecked)
            {
                _w.Line("if (input.HoldsBusy) { lock (_sync) { _busy = false; } }");
            }

            _w.Line("if (input.HasCaller) input.Core.SetException(exception); else Return(input);");
        }
    }

    private void WriteSubmit()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} Submit(Input input)"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) {{ Return(input); return Faulted(new {Rt}MachineNotRunningException(_status)); }}");
            if (Deciding)
            {
                // Code inside the machine that fires it would wait for itself: caught in a decision, and in any transition while one is pending.
                _w.Line("var flow = _flow.Value;");
                _w.Line($"if (flow != null && (flow is Pending || _pending != null)) {{ Return(input); return Faulted(new {Rt}ConcurrentUseException()); }}");
            }

            _w.Line("input.HasCaller = true;");
            _w.Line("var token = input.Core.Version;");
            if (Bounded)
            {
                _w.Line("if (!_room.Wait(0)) return SubmitWhenRoom(input, token);");
            }
            if (IsChecked)
            {
                _w.Line("var refused = false;");
            }

            // A caller that finds the machine idle runs its input at once, inline; otherwise it joins the inbox.
            var idle = "!_pumping && _current == null && _inbox.Count == 0 && _queued.Count == 0" + (Deciding ? " && _pending == null" : "");
            if (!Bounded)
            {
                _w.Line("var inline = false;");
            }

            using (_w.Block("lock (_sync)"))
            {
                if (Deciding)
                {
                    _w.Line("input.ArrivedWhilePending = _pending != null;");
                }

                if (IsChecked)
                {
                    // One caller at a time — except events, which anyone may fire while a decision is pending.
                    var claim = "if (_busy) refused = true; else { _busy = true; input.HoldsBusy = true; }";
                    _w.Line(Deciding ? $"if (!(input.IsEvent && input.ArrivedWhilePending)) {{ {claim} }}" : claim);
                }

                // A bounded inbox releases room as inputs leave it, so its callers always go through it.
                var join = Bounded ? "_inbox.Add(input);" : $"if ({idle}) {{ _pumping = true; _current = input; inline = true; }} else _inbox.Add(input);";
                _w.Line(IsChecked ? $"if (!refused) {{ {join} }}" : join);
            }

            if (IsChecked)
            {
                _w.Line($"if (refused) {{ Return(input); return Faulted(new {Rt}ConcurrentUseException()); }}");
            }

            _w.Line(Bounded ? "PumpNow();" : "if (inline) RunInline(input); else PumpNow();");
            _w.Line($"var result = new {ValueTaskType}(input, token);");
            _w.Line("if (!result.IsCompletedSuccessfully) return result;");
            _w.Line("result.GetAwaiter().GetResult();");
            _w.Line($"return default({ValueTaskType});");
        }

        if (!Bounded)
        {
            WriteRunInline();
        }
        else
        {
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} SubmitWhenRoom(Input input, short token)"))
            {
                _w.Line("await _room.WaitAsync();");
                _w.Line($"if (_status != {Rt}MachineStatus.Running) {{ _room.Release(); Return(input); throw new {Rt}MachineNotRunningException(_status); }}");
                _w.Line("lock (_sync) { _inbox.Add(input); }");
                _w.Line("PumpNow();");
                _w.Line($"await new {ValueTaskType}(input, token);");
            }
        }

        _w.Line();
        using (_w.Block("private void EnqueueInput(Input queued)"))
        {
            if (Deciding)
            {
                _w.Line("var decision = _flow.Value as Pending;");
                using (_w.Block("if (decision != null)"))
                {
                    _w.Line("var stale = false;");
                    _w.Line("lock (_sync) { if (decision != _pending) stale = true; else _queued.Add(queued); }");
                    _w.Line("if (stale) { Return(queued); return; }");
                    _w.Line("// The machine is idle while a decision runs: start a pump, off the decision's stack.");
                    _w.Line("global::System.Threading.ThreadPool.UnsafeQueueUserWorkItem(s_pump, this);");
                    _w.Line("return;");
                }
            }

            _w.Line("if (!_inside) { Return(queued); RefuseOutside(); }");
            _w.Line("lock (_sync) { _queued.Add(queued); }");
        }

        if (Deciding)
        {
            _w.Line();
            _w.Line($"private static readonly global::System.Threading.WaitCallback s_pump = state => (({_machine.Machine.Name})state).PumpNow();");
        }
    }

    // The idle caller's input, trigger by trigger, holding the pump; the general pump takes over as soon as there
    // is anything else to consider — an event queued by an action, or a decision that started — or a step suspends.
    private void WriteRunInline()
    {
        _w.Line();
        using (_w.Block("private void RunInline(Input input)"))
        {
            using (_w.Block("while (true)"))
            {
                _w.Line($"if (_queued.Count != 0{(Deciding ? " || _pending != null" : "")}) {{ PumpSteps(); return; }}");
                _w.Line("var done = input.IsEvent ? input.Next != 0 : input.Next >= input.Values.Length;");
                using (_w.Block("if (done)"))
                {
                    _w.Line("var more = false;");
                    using (_w.Block("lock (_sync)"))
                    {
                        _w.Line("_current = null;");
                        if (IsChecked)
                        {
                            _w.Line("if (input.HoldsBusy) { input.HoldsBusy = false; _busy = false; }");
                        }

                        _w.Line("if (_inbox.Count == 0 && _queued.Count == 0) _pumping = false; else more = true;");
                    }

                    _w.Line("Succeed(input);");
                    _w.Line("if (more) PumpSteps();");
                    _w.Line("return;");
                }

                _w.Line($"{ValueTaskType} stepped;");
                _w.Line("try { stepped = ContinueStep(input); }");
                _w.Line("catch { lock (_sync) { _pumping = false; } throw; }");
                _w.Line("if (!stepped.IsCompletedSuccessfully) { _ = PumpAfter(stepped); return; }");
                _w.Line("if (_current != input) { PumpSteps(); return; }");
            }
        }
    }

    private void WritePump()
    {
        // Runs steps inline until nothing is runnable — or until one suspends, when the rest continues after it.
        _w.Line();
        using (_w.Block("private void PumpNow()"))
        {
            _w.Line("lock (_sync) { if (_pumping) return; _pumping = true; }");
            _w.Line("PumpSteps();");
        }

        _w.Line();
        using (_w.Block("private void PumpSteps()"))
        {
            using (_w.Block("while (true)"))
            {
                _w.Line(Deciding ? "Step step; Input item; Input owner; Pending pending;" : "Step step; Input item; Input owner;");
                _w.Line($"lock (_sync) {{ step = NextStep(out item, out owner{(Deciding ? ", out pending" : "")}); if (step == Step.None) {{ _pumping = false; return; }} }}");
                _w.Line($"{ValueTaskType} stepped;");
                using (_w.Block("try"))
                {
                    _w.Line("if (step == Step.Continue) stepped = ContinueStep(item);");
                    if (Deciding)
                    {
                        _w.Line("else if (step == Step.Event) stepped = EventStep(item, owner);");
                        _w.Line("else stepped = ApplyStep(pending);");
                    }
                    else
                    {
                        _w.Line("else stepped = EventStep(item, owner);");
                    }
                }

                _w.Line("catch { lock (_sync) { _pumping = false; } throw; }");
                _w.Line("if (!stepped.IsCompletedSuccessfully) { _ = PumpAfter(stepped); return; }");
            }
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} PumpAfter({ValueTaskType} stepped)"))
        {
            _w.Line("try { await stepped; } catch { lock (_sync) { _pumping = false; } throw; }");
            _w.Line("PumpSteps();");
        }

        _w.Line();
        _w.Line("/// <summary>The next runnable step: while a decision is pending, its result or an event it handles; otherwise queued events, then the current input, then the next.</summary>");
        using (_w.Block($"private Step NextStep(out Input item, out Input owner{(Deciding ? ", out Pending pending" : "")})"))
        {
            _w.Line(Deciding ? "item = null; owner = null; pending = null;" : "item = null; owner = null;");
            _w.Line($"if (_status != {Rt}MachineStatus.Running) return Step.None;");
            if (Deciding)
            {
                using (_w.Block("if (_pending != null)"))
                {
                    _w.Line("if (_pending.HasResult) { pending = _pending; return Step.Apply; }");
                    using (_w.Block("for (var i = 0; i < _queued.Count; i++)"))
                    {
                        _w.Line("if (_queued[i].IsEvent && Handles(_pending.Decision, _queued[i].Tag)) { item = _queued[i]; _queued.RemoveAt(i); owner = item.HasCaller ? item : _pending.Owner; return Step.Event; }");
                    }

                    using (_w.Block("for (var i = 0; i < _inbox.Count; i++)"))
                    {
                        _w.Line("if (_inbox[i].IsEvent && Handles(_pending.Decision, _inbox[i].Tag)) { item = _inbox[i]; _inbox.RemoveAt(i); owner = item; return Step.Event; }");
                    }

                    _w.Line("return Step.None;");
                }
            }

            _w.Line("if (_queued.Count > 0 && _queued[0].HasCaller) { item = _queued[0]; _queued.RemoveAt(0); owner = item; return Step.Event; }");
            _w.Line($"if (_current == null && _inbox.Count > 0) {{ _current = _inbox[0]; _inbox.RemoveAt(0);{(Bounded ? " _room.Release();" : "")} }}");
            _w.Line("if (_current == null) return Step.None;");
            _w.Line("if (_queued.Count > 0) { item = _queued[0]; _queued.RemoveAt(0); owner = item.HasCaller ? item : _current; return Step.Event; }");
            _w.Line("item = _current;");
            _w.Line("return Step.Continue;");
        }
    }

    private void WritePumpSteps()
    {
        // The current input's next trigger — inline, continuing asynchronously only if its transition suspends.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} ContinueStep(Input input)"))
        {
            if (Deciding)
            {
                _w.Line("_owner = input; _started = null;");
            }

            _w.Line("_inside = true;");
            _w.Line($"{ValueTaskType} dispatched;");
            using (_w.Block("try"))
            {
                _w.Line("if (input.IsEvent && input.Next == 0) { input.Next = 1; dispatched = DispatchInput(input); }");
                using (_w.Block("else if (!input.IsEvent && input.Next < input.Values.Length)"))
                {
                    _w.Line("var start = input.Next;");
                    _w.Line(HasRuns ? "var count = RunLength(input.Values.Slice(start));" : "var count = 1;");
                    _w.Line("input.Next += count;");
                    _w.Line("dispatched = DispatchAt(input.Values, start, count);");
                }

                using (_w.Block("else"))
                {
                    _w.Line("_inside = false;");
                    _w.Line("lock (_sync) { _current = null; }");
                    _w.Line("Succeed(input);");
                    _w.Line($"return default({ValueTaskType});");
                }
            }

            _w.Line($"catch ({Exception} exception) {{ _inside = false; Fail(input, exception); return default({ValueTaskType}); }}");
            _w.Line("if (!dispatched.IsCompletedSuccessfully) return FinishContinue(dispatched, input);");
            _w.Line("_inside = false;");
            _w.Line($"return default({ValueTaskType});");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} FinishContinue({ValueTaskType} dispatched, Input input)"))
        {
            _w.Line($"try {{ await dispatched; }} catch ({Exception} exception) {{ Fail(input, exception); }} finally {{ _inside = false; }}");
        }

        // An event that is not the current input's own: queued inside the machine, or handled while a decision is
        // pending. Only then — rarely — is the transition marked for self-fire detection, which needs an async method.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} EventStep(Input item, Input owner)"))
        {
            if (Deciding)
            {
                _w.Line("if (_pending != null) return EventStepWhilePending(item, owner);");
                _w.Line("_owner = owner; _started = null;");
            }

            _w.Line("_inside = true;");
            _w.Line($"{ValueTaskType} dispatched;");
            _w.Line($"try {{ dispatched = DispatchInput(item); }}");
            _w.Line($"catch ({Exception} exception) {{ _inside = false; Fail(owner, exception); {(Deciding ? "ReleaseEnded(); " : "")}return default({ValueTaskType}); }}");
            _w.Line("if (!dispatched.IsCompletedSuccessfully) return FinishEvent(dispatched, item, owner);");
            _w.Line("_inside = false;");
            _w.Line(Deciding ? "if (_started == null || !item.HasCaller) Succeed(item);" : "Succeed(item);");
            if (Deciding)
            {
                _w.Line("ReleaseEnded();");
            }

            _w.Line($"return default({ValueTaskType});");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} FinishEvent({ValueTaskType} dispatched, Input item, Input owner)"))
        {
            _w.Line($"try {{ await dispatched; {(Deciding ? "if (_started == null || !item.HasCaller) " : "")}Succeed(item); }}");
            _w.Line($"catch ({Exception} exception) {{ Fail(owner, exception); }}");
            _w.Line(Deciding ? "finally { _inside = false; ReleaseEnded(); }" : "finally { _inside = false; }");
        }

        if (Deciding)
        {
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} EventStepWhilePending(Input item, Input owner)"))
            {
                _w.Line("_flow.Value = s_step; _owner = owner; _started = null; _inside = true;");
                _w.Line("try { await DispatchInput(item); if (_started == null || !item.HasCaller) Succeed(item); }");
                _w.Line($"catch ({Exception} exception) {{ Fail(owner, exception); }}");
                _w.Line("finally { _inside = false; ReleaseEnded(); }");
            }

            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} ApplyStep(Pending pending)"))
            {
                _w.Line("_flow.Value = s_step; _owner = pending.Owner; _started = null; _inside = true;");
                _w.Line("lock (_sync) { EndPending(pending); }");
                _w.Line("try { await ApplyDecision(pending); }");
                _w.Line($"catch ({Exception} exception) {{ Fail(pending.Owner, exception); }}");
                _w.Line("finally { _inside = false; ReleaseEnded(); }");
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchInput(Input input)"))
        {
            using (_w.Block("switch (input.Tag)"))
            {
                for (var i = 0; i < _events.Count; i++)
                {
                    _w.Line($"case {i}: return DispatchEvent{i}(input.E{i});");
                }

                _w.Line($"default: UnhandledUnknown(input.Unknown); return default({ValueTaskType});");
            }
        }
    }

    private void WriteEndings()
    {
        _w.Line();
        _w.Line("/// <summary>An input failed: its caller's FireAsync throws, and the rest of it — and any decision it started — is abandoned.</summary>");
        using (_w.Block($"private void Fail(Input input, {Exception} exception)"))
        {
            if (Deciding)
            {
                _w.Line("Pending abandoned = null;");
                using (_w.Block("lock (_sync)"))
                {
                    _w.Line("if (input == _current) _current = null;");
                    _w.Line("if (_pending != null && _pending.Owner == input) { abandoned = _pending; EndPending(abandoned); }");
                }

                _w.Line("if (abandoned != null) abandoned.Cancellation.Cancel();");
                _w.Line("Fault(input, exception);");
                _w.Line("ReleaseEnded();");
            }
            else
            {
                _w.Line("lock (_sync) { if (input == _current) _current = null; }");
                _w.Line("Fault(input, exception);");
            }
        }

        _w.Line();
        _w.Line("/// <summary>Stopping: cancel any pending decision; every waiting caller's FireAsync throws.</summary>");
        using (_w.Block("private void Abandon()"))
        {
            _w.Line("var waiting = new global::System.Collections.Generic.List<Input>();");
            if (Deciding)
            {
                _w.Line("Pending abandoned;");
            }

            using (_w.Block("lock (_sync)"))
            {
                _w.Line($"_status = {Rt}MachineStatus.Stopped;");
                if (Deciding)
                {
                    _w.Line("abandoned = _pending;");
                    _w.Line("_pending = null;");
                    _w.Line("_ended.Clear();");
                }

                _w.Line("waiting.AddRange(_inbox);");
                if (Bounded)
                {
                    _w.Line("if (_inbox.Count > 0) _room.Release(_inbox.Count);");
                }

                _w.Line("waiting.AddRange(_queued);");
                _w.Line("if (_current != null) waiting.Add(_current);");
                _w.Line("_inbox.Clear();");
                _w.Line("_queued.Clear();");
                _w.Line("_current = null;");
            }

            if (Deciding)
            {
                _w.Line("if (abandoned != null) abandoned.Cancellation.Cancel();");
            }

            _w.Line($"foreach (var input in waiting) Fault(input, new {Rt}MachineNotRunningException({Rt}MachineStatus.Stopped));");
        }

        if (!Deciding)
        {
            return;
        }

        _w.Line();
        _w.Line("/// <summary>Leaves the pending state. Called under the lock; <see cref=\"ReleaseEnded\"/> finishes the job.</summary>");
        using (_w.Block("private void EndPending(Pending pending)"))
        {
            _w.Line("if (_pending == pending) { _pending = null; _ended.Add(pending); }");
        }

        _w.Line();
        _w.Line("/// <summary>After a decision ends: cancel it, let the events that waited for it run next, and complete an owner that was a waiting event.</summary>");
        using (_w.Block("private void ReleaseEnded()"))
        {
            _w.Line("Pending[] ended;");
            _w.Line("global::System.Collections.Generic.List<Input> finished = null;");
            using (_w.Block("lock (_sync)"))
            {
                _w.Line("if (_ended.Count == 0) return;");
                _w.Line("ended = _ended.ToArray();");
                _w.Line("_ended.Clear();");
                using (_w.Block("for (var i = 0; i < _inbox.Count;)"))
                {
                    _w.Line("var waited = _inbox[i];");
                    _w.Line("if (waited.IsEvent && waited.ArrivedWhilePending) { _inbox.RemoveAt(i); waited.ArrivedWhilePending = false; _queued.Add(waited); } else i++;");
                }

                using (_w.Block("foreach (var pending in ended)"))
                {
                    _w.Line("if (pending.Owner != null && pending.Owner != _current && (_pending == null || _pending.Owner != pending.Owner))");
                    _w.Line("{ if (finished == null) finished = new global::System.Collections.Generic.List<Input>(); finished.Add(pending.Owner); }");
                }
            }

            _w.Line("foreach (var pending in ended) pending.Cancellation.Cancel();");
            _w.Line("if (finished != null) foreach (var owner in finished) Succeed(owner);");
        }
    }
}
```

Four changes, each removing an allocation or a hand-off:

- **An input is the completion source.** `Input` implements `IValueTaskSource` over a
  `ManualResetValueTaskSourceCore`, and `FireAsync` returns `new ValueTask(input, token)`. Inputs are pooled: a
  single lock-free spare for the common ping-pong, a small stack behind it. `GetResult` returns the input to the
  pool, so a caller that awaits its own call recycles it.
- **A caller that finds the machine idle runs it inline.** Under the same lock that would have queued the input, an
  idle machine is claimed — `_pumping` and `_current` — and the caller runs its own triggers on its own stack.
  `RunInline` hands over to the general pump the moment anything else appears: an event queued by an action, a
  decision that started, or a step that suspends.
- **A call that completed synchronously returns `default`.** `Submit` checks `IsCompletedSuccessfully`, consumes
  the result, and returns a completed `ValueTask` — which returns the input to the pool before `FireAsync` does.
- **`InboxCapacity` is honoured**: a `SemaphoreSlim` counts the room, taken when an input joins the inbox and
  released when the pump takes it out or the machine stops. A bounded machine always goes through its inbox, so it
  does not take the inline path — that is the cost of the accounting, and why it is opt-in.

The pump is synchronous now — `PumpNow` claims it and `PumpSteps` runs until nothing is runnable — so a decision
that queues an event starts it the same way.

`src/StateAlchemist.Generators/MachineEmitter.Decisions.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private readonly SortedSet<(int Transition, int Leaf)> _decisions = [];

    /// <summary>
    /// Decisions (spec §5.6, §6.5). A synchronous <c>Decide</c> runs inline; a <c>DecideAsync</c> enters the pending
    /// state and runs apart, and its result is applied by the pump. Either way each outcome is a move from the leaf,
    /// written as its own method with the outcome's <c>Complete</c> as the transform.
    /// </summary>
    private void WriteDecisions()
    {
        foreach (var (index, leaf) in _decisions)
        {
            var decision = _model.Transitions[index];
            WriteDecide(decision, leaf);
            WriteOutcomes(decision, leaf);
        }

        foreach (var index in _decisions.Select(d => d.Transition).Distinct().Where(i => _model.Transitions[i].Decision!.DecideAsync is not null))
        {
            WriteRunDecision(_model.Transitions[index]);
        }

        if (_model.Transitions.Any(t => t.Decision?.DecideAsync is not null))
        {
            WriteApplyDecision();
        }
    }

    private void WriteDecide(TransitionModel decision, int leaf)
    {
        var parts = decision.Decision!;
        var argument = TriggerArgument(decision);
        _w.Line();
        _w.Line($"// {decision.Name}: decided in {_model.States[leaf].Name}");
        using (_w.Block($"private {ValueTaskType} {DecisionName(decision.Index, leaf)}({TriggerParameter(decision)})"))
        {
            if (parts.DecideAsync is not null)
            {
                _w.Line("Pending pending;");
                using (_w.Block("lock (_sync)"))
                {
                    _w.Line($"if (_pending != null) throw new global::System.InvalidOperationException(\"'{decision.Name}' cannot start while decision '\" + _pending.Name + \"' is pending.\");");
                    _w.Line($"pending = new Pending {{ Decision = {decision.Index}, Name = {Literal(decision.Name)}, Owner = _owner }};");
                    _w.Line(decision.Trigger.Kind == MatchKind.Event ? "pending.Event = e;" : "pending.Value = value;");
                    _w.Line("_pending = pending;");
                    _w.Line("_started = pending;");
                }

                _w.Line($"_ = RunDecision{decision.Index}(pending);");
                _w.Line($"return default({ValueTaskType});");
                return;
            }

            var path = PathPlanner.Stay(leaf);
            _w.Line("object outcome;");
            using (_w.Block("try"))
            {
                _w.Line($"outcome = {Owner(parts.Decide!)}({Arguments(parts.Decide!, Use.Decide, decision, path, [])}).Value;");
                _w.Line($"if (outcome == null) throw new global::System.InvalidOperationException({Literal($"'{decision.Name}' returned no outcome.")});");
            }

            using (_w.Block($"catch ({Exception} exception)"))
            {
                _w.Line($"return DispatchEvent{DecisionFailedTag}(new {Rt}DecisionFailed({Literal(decision.Name)}, exception));");
            }

            WriteOutcomeSwitch(decision, leaf, "outcome", argument);
        }
    }

    /// <summary>Picks the outcome's move by the type of the case the union holds.</summary>
    private void WriteOutcomeSwitch(TransitionModel decision, int leaf, string outcome, string argument)
    {
        var completions = decision.Decision!.Completions;
        for (var i = 0; i < completions.Count; i++)
        {
            var type = Name(OutcomeType(completions[i]));
            _w.Line($"if ({outcome} is {type}) return {OutcomeName(decision.Index, i, leaf)}({argument}, ({type}){outcome});");
        }

        _w.Line($"throw new global::System.InvalidOperationException({Literal($"'{decision.Name}' returned an outcome it does not complete.")});");
    }

    private void WriteOutcomes(TransitionModel decision, int leaf)
    {
        var completions = decision.Decision!.Completions;
        for (var i = 0; i < completions.Count; i++)
        {
            var completion = completions[i];
            var type = OutcomeType(completion);
            var completed = decision.Completed
                .Where(m => m.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Outcome) is not { } taken || taken.TypeName == completion.OutcomeType)
                .ToList();
            var kind = completion.Target == decision.Source ? "Reenter" : "Move";
            WriteSteps(OutcomeName(decision.Index, i, leaf), $"{TriggerParameter(decision)}, {Name(type)} outcome", decision,
                PathPlanner.Move(_hierarchy, leaf, completion.Target), completion.Complete, completed, kind, outcome: Name(type));
        }
    }

    /// <summary>
    /// Runs a <c>DecideAsync</c> apart from the pump, marked so it cannot fire its own machine (spec §6.6), and records
    /// its result only if the decision is still the pending one.
    /// </summary>
    private void WriteRunDecision(TransitionModel decision)
    {
        var decide = decision.Decision!.DecideAsync!;
        _w.Line();
        using (_w.Block($"private async global::System.Threading.Tasks.Task RunDecision{decision.Index}(Pending pending)"))
        {
            _w.Line("_flow.Value = pending;");
            _w.Line(decision.Trigger.Kind == MatchKind.Event ? $"var e = ({Event(decision.Trigger.EventType!)})pending.Event;" : "var value = pending.Value;");
            _w.Line("object outcome = null;");
            _w.Line($"{Exception} failure = null;");
            using (_w.Block("try"))
            {
                _w.Line($"outcome = (await {Owner(decide)}({Arguments(decide, Use.Decide, decision, PathPlanner.Stay(decision.Source), [])})).Value;");
                _w.Line($"if (outcome == null) throw new global::System.InvalidOperationException({Literal($"'{decision.Name}' returned no outcome.")});");
            }

            _w.Line($"catch ({Exception} exception) {{ failure = exception; }}");
            _w.Line("_flow.Value = null;");
            using (_w.Block("lock (_sync)"))
            {
                _w.Line($"if (_pending != pending || _status != {Rt}MachineStatus.Running) return;");
                _w.Line("pending.Outcome = outcome;");
                _w.Line("pending.Failure = failure;");
                _w.Line("pending.HasResult = true;");
            }

            _w.Line("PumpNow();");
        }
    }

    /// <summary>A pending decision's result: its failure fires <c>DecisionFailed</c>; its outcome runs that outcome's move.</summary>
    private void WriteApplyDecision()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} ApplyDecision(Pending pending)"))
        {
            using (_w.Block("switch (pending.Decision)"))
            {
                foreach (var group in _decisions.Where(d => _model.Transitions[d.Transition].Decision!.DecideAsync is not null).GroupBy(d => d.Transition))
                {
                    var decision = _model.Transitions[group.Key];
                    using (_w.Block($"case {decision.Index}:"))
                    {
                        _w.Line($"if (pending.Failure != null) return DispatchEvent{DecisionFailedTag}(new {Rt}DecisionFailed({Literal(decision.Name)}, pending.Failure));");
                        _w.Line(decision.Trigger.Kind == MatchKind.Event ? $"var e = ({Event(decision.Trigger.EventType!)})pending.Event;" : "var value = pending.Value;");
                        using (_w.Block("switch (_leaf)"))
                        {
                            foreach (var (_, leaf) in group)
                            {
                                using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                                {
                                    WriteOutcomeSwitch(decision, leaf, "pending.Outcome", TriggerArgument(decision));
                                }
                            }

                            _w.Line($"default: return default({ValueTaskType});");
                        }
                    }
                }

                _w.Line($"default: return default({ValueTaskType});");
            }
        }

        _w.Line();
        _w.Line("/// <summary>Whether the pending decision lists the event in <c>Handle</c>: it runs at once instead of waiting.</summary>");
        using (_w.Block("private static bool Handles(int decision, int tag)"))
        {
            using (_w.Block("switch (decision)"))
            {
                foreach (var decision in _model.Transitions.Where(t => t.Decision?.DecideAsync is not null))
                {
                    var tags = decision.Decision!.Handle.Select(EventTag).Where(t => t >= 0).ToList();
                    _w.Line($"case {decision.Index}: return {(tags.Count == 0 ? "false" : string.Join(" || ", tags.Select(t => $"tag == {t}")))};");
                }

                _w.Line("default: return false;");
            }
        }
    }

    private ITypeSymbol OutcomeType(OutcomeCompletion completion) =>
        _machine.Methods[completion.Complete].Parameters.First(p => SymbolModelBuilder.MetadataName(p.Type) == completion.OutcomeType).Type;

    private int DecisionFailedTag => EventTag("StateAlchemist.DecisionFailed");

    private static string TriggerArgument(TransitionModel transition) => transition.Trigger.Kind == MatchKind.Event ? "e" : "value";

    private string OutcomeName(int decision, int outcome, int leaf) => $"C{decision}_{outcome}_{_stateIds[leaf]}";
}
```

- [ ] **Step 2: The bounded inbox's test**

`tests/StateAlchemist.Generated.Tests/Performance/BoundedInboxTests.cs`:

```csharp
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests.Performance;

/// <summary>
/// docs/concepts/concurrency.md, "Bounded inbox": callers that outrun a Serialized machine wait for room instead of
/// growing its queue. What the bound saves is memory, which no contract can see, so this looks at the generated
/// inbox itself.
/// </summary>
public class BoundedInboxTests
{
    private static int Queued(object machine) =>
        ((ICollection)machine.GetType().GetField("_inbox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(machine)!).Count;

    [Test]
    public async Task ACallerFindingTheInboxFullWaitsForRoom()
    {
        var context = new RecordingContext();
        var machine = new BoundedRecorderMachine(context);
        context.Machine = machine;
        await machine.StartAsync();

        var held = machine.FireAsync((byte)11).AsTask();
        var first = machine.FireAsync((byte)3).AsTask();
        var second = machine.FireAsync((byte)3).AsTask();
        await Assert.That(Queued(machine)).IsEqualTo(1);

        context.Gate.SetResult();
        await Task.WhenAll(held, first, second);
        await Assert.That(machine.TryGetState(out A1 a1) ? a1.Value : -1).IsEqualTo(2);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: PASS, including the two inbox allocation tests and every concurrency and decision contract. Run the suite
a few times: the inline path is where a race would show.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Pool the inbox's inputs and let an idle caller run its own"
```

---

### Task 5: A TNC-sized machine — generation, incrementality, construction

**Files:**
- Create: `tests/StateAlchemist.Generators.Tests/Performance/TncSizedMachineSource.cs`,
  `TncSizedMachineTests.cs`

**Interfaces:**
- Consumes: Plan 4's `TestCompilation`; the generator.
- Produces: the two size targets of spec §9 as tests.

- [ ] **Step 1: A machine the size of TNC's**

`tests/StateAlchemist.Generators.Tests/Performance/TncSizedMachineSource.cs`:

```csharp
using System.Text;

namespace StateAlchemist.Generators.Tests.Performance;

/// <summary>
/// A machine the size of TNC's (spec §9): a root, <see cref="Groups"/> groups of <see cref="LeavesPerGroup"/> leaves —
/// 81 states — and 1,000 transitions: every leaf stays on some values and moves to a leaf in the next group on others,
/// every group moves on its own values, the first leaf captures text as a run, and every group has actions on the way
/// in and out. Nothing about it is realistic but its size.
/// </summary>
internal static class TncSizedMachineSource
{
    public const string Namespace = "TncSized";
    public const int Groups = 8;
    public const int LeavesPerGroup = 9;
    public const int StaysPerLeaf = 6;
    public const int MovesPerLeaf = 7;

    public const int States = 1 + Groups + Groups * LeavesPerGroup;

    /// <summary>Leaf transitions, group transitions, and the run: 993.</summary>
    public const int Transitions = Groups * LeavesPerGroup * (StaysPerLeaf + MovesPerLeaf) + Groups * (Groups - 1) + 1;

    public static string Generate()
    {
        var text = new StringBuilder();
        text.AppendLine("using System;");
        text.AppendLine("using StateAlchemist;");
        text.AppendLine($"namespace {Namespace};");
        text.AppendLine("public sealed class Log { public int Count; }");
        text.AppendLine("public struct Root : IRootState { public int Moves; }");
        for (var g = 0; g < Groups; g++)
        {
            text.AppendLine($"{(g == 0 ? "[Initial] " : "")}public struct G{g} : IState<Root> {{ public int Visits; }}");
            for (var l = 0; l < LeavesPerGroup; l++)
            {
                text.AppendLine($"{(l == 0 ? "[Initial] " : "")}public struct L{g}_{l} : IState<G{g}> {{ public int Count; public int Length; }}");
            }
        }

        text.AppendLine("[Module] public static class TncSizedModule {");
        for (var g = 0; g < Groups; g++)
        {
            var next = (g + 1) % Groups;
            for (var l = 0; l < LeavesPerGroup; l++)
            {
                var leaf = $"L{g}_{l}";
                for (var v = 0; v < StaysPerLeaf; v++)
                {
                    text.AppendLine($"  [Transition(From = typeof({leaf})), On({v})] public static void Stay{leaf}_{v}(ref {leaf} self) => self.Count += {v + 1};");
                }

                for (var v = 0; v < MovesPerLeaf; v++)
                {
                    var target = $"L{next}_{(l + v) % LeavesPerGroup}";
                    text.AppendLine($"  [Transition(From = typeof({leaf}), To = typeof({target})), On({StaysPerLeaf + v})] public static void Move{leaf}_{v}(in {leaf} from, ref {target} to, ref Root root) {{ to.Count = from.Count + {v}; root.Moves++; }}");
                }
            }

            // A group moves to the initial leaf of any other group on a value of its own, whichever leaf is active.
            for (var other = 0; other < Groups; other++)
            {
                if (other != g)
                {
                    text.AppendLine($"  [Transition(From = typeof(G{g}), To = typeof(G{other})), On({100 + other})] public static void Jump{g}_{other}(ref G{other} to) => to.Visits++;");
                }
            }

            text.AppendLine($"  [Entered(typeof(G{g}))] public static void Enter{g}(Log log) => log.Count++;");
            text.AppendLine($"  [Exited(typeof(G{g}))] public static void Exit{g}(Log log) => log.Count++;");
        }

        text.AppendLine("  [Transition(From = typeof(L0_0)), OnRange(32, 99), Run] public static void Capture(ref L0_0 self, ReadOnlySpan<byte> run) => self.Length += run.Length;");
        text.AppendLine("}");
        text.AppendLine("[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Log))]");
        text.AppendLine("[Include(typeof(TncSizedModule))]");
        text.AppendLine("public sealed partial class TncSizedMachine { public static object Create(object log) => new TncSizedMachine((Log)log); }");
        return text.ToString();
    }
}
```

- [ ] **Step 2: The tests**

`tests/StateAlchemist.Generators.Tests/Performance/TncSizedMachineTests.cs`:

```csharp
extern alias generator;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Performance;

/// <summary>
/// The two size targets of spec §9, on a machine as big as TNC's: generating it takes under a second, an unrelated
/// edit regenerates nothing, and constructing the machine it writes costs one allocation and well under 10 µs.
/// </summary>
public class TncSizedMachineTests
{
    private static readonly string Source = TncSizedMachineSource.Generate();


    [Test]
    public async Task GeneratingItTakesLessThanASecond()
    {
        // Warm up the generator on a small machine first: the target is generation, not the JIT.
        Run(TestCompilation.Create("[global::StateAlchemist.Machine(Root = typeof(global::StateAlchemist.Samples.Performance.Root), Value = typeof(byte))] public sealed partial class Small;"), out _);

        var compilation = TestCompilation.Create(Source);
        var watch = Stopwatch.StartNew();
        var driver = Run(compilation, out var generated);
        watch.Stop();

        await Assert.That(generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        // Spec §9 asks for under a second, which is what a developer machine does (0.4 s when this was written).
        // The budget here is five, because CI runs three frameworks at once on shared hardware: what a test on that
        // hardware can honestly catch is a regression of an order of magnitude, not a factor of two.
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5)).Because($"a TNC-sized machine generates quickly, not in {watch.ElapsedMilliseconds} ms");

        // An edit somewhere else in the program: the transform's result is unchanged, so nothing is written again.
        var edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Elsewhere { public sealed class Unrelated { public int Value; } }", (CSharpParseOptions)compilation.SyntaxTrees.First().Options));
        var incremental = Stopwatch.StartNew();
        var results = driver.RunGenerators(edited).GetRunResult().Results.Single();
        incremental.Stop();

        var outputs = results.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(step => step.Outputs).ToList();
        await Assert.That(outputs).IsNotEmpty();
        await Assert.That(outputs.All(output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue()
            .Because("an unrelated edit must not regenerate the machine");
        await Assert.That(incremental.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ConstructingItCostsOneObjectAndLittleTime()
    {
        var assembly = Compile();
        var type = assembly.GetType($"{TncSizedMachineSource.Namespace}.TncSizedMachine")!;
        var definition = (global::StateAlchemist.MachineDefinition)type.GetProperty("Definition", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        await Assert.That(definition.States).Count().IsEqualTo(TncSizedMachineSource.States);
        await Assert.That(definition.Transitions).Count().IsEqualTo(TncSizedMachineSource.Transitions);
        var log = Activator.CreateInstance(assembly.GetType($"{TncSizedMachineSource.Namespace}.Log")!)!;
        var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.CreateDelegate<Func<object, object>>();

        for (var i = 0; i < 100; i++)
        {
            _ = create(log);
            _ = RuntimeHelpers.GetUninitializedObject(type);
        }

        var empty = Allocated(() => RuntimeHelpers.GetUninitializedObject(type));
        var constructed = Allocated(() => create(log));
        await Assert.That(constructed).IsEqualTo(empty).Because("construction allocates the machine and nothing else");

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            _ = create(log);
        }

        watch.Stop();
        var each = watch.Elapsed.TotalMicroseconds / 1000;
        await Assert.That(each).IsLessThan(10).Because($"spec §9: construction takes under 10 µs, not {each:F3} µs");
    }

    private static long Allocated(Func<object> action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var kept = action();
        var after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(kept);
        return after - before;
    }

    private static GeneratorDriver Run(CSharpCompilation compilation, out Compilation generated) =>
        CSharpGeneratorDriver.Create(
                [new MachineGenerator().AsSourceGenerator()],
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                optionsProvider: null,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true))
            .RunGeneratorsAndUpdateCompilation(compilation, out generated, out _);

    private static Assembly Compile()
    {
        var compilation = TestCompilation.Create(LanguageVersion.Latest, TestCompilation.Net8Symbols, Source);
        Run(compilation, out var generated);
        using var image = new MemoryStream();
        var emitted = generated.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("The generated code does not compile:\n" + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        image.Position = 0;
        return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(image);
    }
}
```

Incrementality is read from the driver's tracked output steps: after an edit elsewhere in the program, every output
must be `Cached` or `Unchanged`. Construction is compared against `RuntimeHelpers.GetUninitializedObject`, which
allocates the instance and runs no constructor — so "the same" means the constructor allocated nothing of its own.

- [ ] **Step 3: Run them**

Run: `dotnet test tests/StateAlchemist.Generators.Tests -- --treenode-filter "/*/*/TncSizedMachineTests/*"`
Expected: PASS. On this machine: 81 states and 993 transitions generate in 0.4 s, an unrelated edit regenerates
nothing, and one construction costs 0.07 µs and one object. The test's own budget for generation is five seconds,
not §9's one: CI builds three frameworks at once on shared hardware, where 1.3 s is ordinary, and what a test there
can honestly catch is a regression of an order of magnitude.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Test generation, incrementality and construction on a TNC-sized machine"
```

---

### Task 6: A machine in a native binary

**Files:**
- Create: `samples/StateAlchemist.AotSample/StateAlchemist.AotSample.csproj`, `Program.cs`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the Telnet sample's module set.
- Produces: a `PublishAot` executable, and the CI job that publishes it with `-warnaserror` and runs it.

- [ ] **Step 1: The sample**

`samples/StateAlchemist.AotSample/StateAlchemist.AotSample.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>StateAlchemist.AotSample</RootNamespace>
    <!-- The point of this sample: publishing it native must warn about nothing. CI publishes it and fails on a warning. -->
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IsAotCompatible>true</IsAotCompatible>
    <TrimmerSingleWarn>false</TrimmerSingleWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\StateAlchemist\StateAlchemist.csproj" />
    <ProjectReference Include="..\StateAlchemist.Samples\StateAlchemist.Samples.csproj" />
    <ProjectReference Include="..\..\src\StateAlchemist.Generators\StateAlchemist.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

`samples/StateAlchemist.AotSample/Program.cs`:

```csharp
using System;
using System.Threading.Tasks;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.AotSample;

// A machine in a native binary: no reflection, no dictionaries, nothing the trimmer has to guess at. Publishing this
// with PublishAot must produce no warnings — that is what CI checks — and running it must negotiate the same way the
// sample does on the runtime.
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class AotTelnet;

public static class Program
{
    public static async Task<int> Main()
    {
        var context = new TelnetContext();
        var machine = new AotTelnet(context);
        await machine.StartAsync();

        // IAC SB NAWS 0 80 0 24 IAC SE: a window of 80×24, read byte by byte.
        await machine.FireAsync(new byte[] { 255, 250, 31, 0, 80, 0, 24, 255, 240 });
        await machine.StopAsync();

        machine.TryGetState<Connected>(out var root);
        Console.WriteLine($"window {root.Width}x{root.Height}");
        foreach (var line in context.Log)
        {
            Console.WriteLine(line);
        }

        return root.Width == 80 && root.Height == 24 ? 0 : 1;
    }
}
```

- [ ] **Step 2: The job**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        framework: [ net8.0, net10.0, net11.0 ]
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v5
        with:
          # global.json pins the SDK that builds every target; 8.0.x and 10.0.x bring the test runtimes.
          global-json-file: global.json
          dotnet-version: |
            8.0.x
            10.0.x
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test --solution StateAlchemist.slnx --no-build --framework ${{ matrix.framework }}

  aot:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
      # Spec §9: a machine in a native binary. The publish must warn about nothing — no reflection, no dictionaries,
      # nothing the trimmer has to guess at — and the binary must negotiate the same way the sample does.
      - run: dotnet publish samples/StateAlchemist.AotSample -c Release -r linux-x64 -warnaserror
      - run: samples/StateAlchemist.AotSample/bin/Release/net11.0/linux-x64/publish/StateAlchemist.AotSample

  docs:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
      - run: dotnet tool restore
      - run: dotnet mdsnippets
      - name: The docs match the compiled samples
        run: git diff --exit-code -- '*.md'
```

- [ ] **Step 3: Publish it**

Run: `dotnet publish samples/StateAlchemist.AotSample -c Release -r linux-x64 -warnaserror`
Expected: it publishes with no warnings — a machine has no reflection and no dictionaries for the trimmer to guess
at — and the binary prints `window 80x24` and exits 0. On this machine it is 1.4 MB.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Publish a machine as a native binary, and check it in CI"
```

---

### Task 7: What it costs, written down

**Files:**
- Modify: `docs/concepts/concurrency.md`, `docs/concepts/actions.md`, `docs/concepts/lifecycle.md`
- Modify: `docs/superpowers/specs/2026-09-11-statealchemist-design.md` (§9's table, D22)

**Interfaces:**
- Consumes: the numbers the benchmarks print.
- Produces: documentation that matches the implementation, with the design's guesses replaced by measurements.

- [ ] **Step 1: Concurrency**

`docs/concepts/concurrency.md`:

```markdown
# Concurrency

A machine processes one transition at a time. What differs is who may call it, and what happens if two callers
try at once. The choice is made when the application compiles — `[Machine(Concurrency = ...)]` — and the
generator emits only that mode's code.

| Mode | Who may fire | If two callers overlap | Cost per call |
|---|---|---|---|
| **`Checked`** (default) | one caller at a time | the second throws `ConcurrentUseException`; state is never corrupted | +4.9 ns |
| `Unchecked` | one caller at a time, promised by the host | undefined: state can be corrupted | none |
| `Serialized` | any thread | nothing goes wrong: calls queue and run one at a time | +65 ns |

Costs are what a stay costs above `Unchecked`'s 5.7 ns, measured on .NET 11 RC1, x64, uncontended, by the
benchmarks in `benchmarks/StateAlchemist.Benchmarks`. No mode allocates.

## Checked

The default follows the lead of .NET's `Dictionary`, which detects concurrent modification and throws rather than
corrupting itself: wrong use fails loudly, and correct use pays one interlocked operation per call. Use it unless
you have measured a reason not to.

## Unchecked

For a host that guarantees a single caller — a connection's read loop, say — and wants the last few nanoseconds.
This is the same trade as `Channel`'s `SingleReader`/`SingleWriter`: a promise the host makes in exchange for a
faster path. An `Unchecked` machine with async actions draws warning
[`SALCH0209`](../reference/diagnostics.md#salch0209): continuations run on other threads, so even a single caller
must await every `FireAsync` before the next.

## Serialized

Any thread may call `FireAsync`. Each call becomes an *input* in the machine's inbox, and whichever caller finds
the machine idle runs it straight away — inline, on the calling thread, with no background consumer task and no
thread hop when nothing is contended. Each transition, **including its awaited actions**, completes before the
next begins. A caller whose trigger queued behind another waits on its input, which is itself the pooled
`IValueTaskSource` the caller's `ValueTask` is built on: it completes when that caller's own triggers are done,
and carries the exception if one throws. A call that finishes without suspending allocates nothing at all —
the input goes back to the pool before `FireAsync` returns.

This is the turn-based model of Orleans grains. It is also what a lock around a machine cannot give you: a lock
around `FireAsync` stops protecting anything once actions are async.

The inbox is a list and a lock, not a `Channel`: a channel's own completion sources and its bounded mode cost
more than the whole rest of the call, and the machine needs neither — it has one reader by construction, and the
inputs it hands out are the completion sources.

### Bounded inbox

`InboxCapacity = n` caps how many inputs may wait: producers that outrun the machine wait for room instead of
growing the queue. Room is a `SemaphoreSlim`, taken when an input joins the inbox and released when the pump takes
it out, so a producer that has to wait does so asynchronously. A bounded machine always goes through its inbox —
it cannot take the inline path, which would skip the accounting — so it costs more per call even when nothing is
contended. That is why it is opt-in.

## While a decision is pending

While a [decision](decisions.md) is pending, the caller whose input started it is awaiting its `FireAsync`, and
the decision completes on another thread. So the machine accepts **events** from other callers in every mode —
`Checked` and `Unchecked` included — through the same inbox, which is why a machine with an async decision has one
whatever its concurrency mode. That is what lets a disconnect or a timeout reach a pending decision.

Events that waited run as soon as the decision resolves, in the order they arrived, before the rest of the paused
input.

Values are different: they come from one stream, so a second caller firing values while a decision is pending is
misuse in every mode — `Checked` throws, `Serialized` queues them behind the waiting batch.

## What the inbox costs

A machine with an inbox — `Serialized`, or any mode with an async decision — costs about 65 ns per call more than
one without, for the same stay. The inbox is what buys the guarantee: every call is an object with an identity
that outlives the calling stack, so it can be queued, completed later, and completed exactly once. Most of the
cost is the two lock regions a call passes through, claiming the pump and releasing it.

Every guard is paid per call, not per value: the batch `FireAsync(ReadOnlyMemory<TValue>)` pays it once for a
whole read, which is why a machine reading a socket should hand the buffer over whole.
```

The design guessed at a `Channel` inbox and priced it. The implementation is a list and a lock, because a channel's
completion sources and bounded mode cost more than the rest of the call and the machine needs neither — it has one
reader by construction, and the inputs it hands out are its completion sources.

- [ ] **Step 2: Actions and lifecycle**

`docs/concepts/actions.md`:

````markdown
# Actions

Transforms change the machine's data; **actions run your code**: network writes, callbacks, logging. Actions run
*after* the state has changed, so their names are past tense, and they may be async.

## Three places to put an action

| Declared as | Runs | Use it for |
|---|---|---|
| a transition's `Completed` / `CompletedAsync` | after this transition | "this happened": reply to an offer, raise a callback |
| `[Exited(typeof(S))]` on a module method | whenever `S` is left, by any transition | cleanup, tracing |
| `[Entered(typeof(S))]` on a module method | whenever `S` is entered, by any transition | "ready" notifications |

<!-- snippet: sample-class-form -->
<a id='snippet-sample-class-form'></a>
```cs
[Transition(From = typeof(Willing), To = typeof(Idle)), On(GmcpOption)]
public static class Accept
{
    public static void Transform(ref Connected root) => root.GmcpEnabled = true;

    public static ValueTask CompletedAsync(TelnetContext context) => context.SendAsync(Iac, Do, GmcpOption);
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/GmcpModule.cs#L11-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-class-form' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## The order of one transition

Every transition runs the same steps. The phase names say where you are: imperative before the state changes,
past tense after.

| Step | Phase | You declare it as | |
|---|---|---|---|
| 1 | **Guard** | the transition's `Guard` | May it fire? Read-only. |
| 2 | *reset* | — | The states being entered are reset. Nothing can see them yet. |
| 3 | **Transform** | the transition's `Transform` | Synchronous. The source is intact, the target fresh. |
| 4 | *commit* | — | The active leaf becomes the target. **The state has now changed.** |
| 5 | **Exited** | `[Exited(typeof(S))]` | For each state left, innermost first. |
| 6 | **Entered** | `[Entered(typeof(S))]` | For each state entered, outermost first. |
| 7 | **Completed** | the transition's `Completed` | Last. |
| 8 | *clear* | — | The states left are cleared. |
| 9 | *drain* | — | Events queued during the transition are processed. |

A stay has no exits or entries: steps 2, 5, 6 and 8 do nothing, and `Completed` follows the transform.

States left are cleared at step 8, not step 4, so the actions in steps 5–7 can still read what was left — a NAWS
`Completed` can report the window size from the state it just exited. Nothing else can observe the difference:
from step 4 those states are no longer active.

### Order among actions for the same state

Several modules may attach actions to the same state and phase. Within one module, declaration order decides.
Across modules, give each an `Order`; two with the same `Order` from different modules are
[`SALCH0103`](../reference/diagnostics.md#salch0103), because nothing else would decide between them.

```csharp
[Exited(typeof(Naws), Order = 1)]
public static void Trace(TelnetContext context, Naws naws) => context.Log.Add($"left NAWS at {naws.Index}");
```

## What an action can take

| Parameter | Meaning |
|---|---|
| the context | your object |
| a state, by value (or `in` for a synchronous action) | a copy of its data: states active after the change, and states just left |
| the value, the event, or a run as `ReadOnlyMemory<TValue>` | what fired the transition (`Completed` only) |
| a decision's outcome | for a decision's `Completed` overloads |
| `CancellationToken` | cancelled when the machine stops |
| `in TransitionInfo<TValue>` | which transition, which phase, which state |

An async action cannot take `ref` or `in` parameters — C# forbids it — which is why actions see copies.

## Async without the cost

An action returning `ValueTask` that completes synchronously costs nothing extra: the generated code checks
`IsCompletedSuccessfully` and carries straight on — no state machine is entered, and nothing is allocated. Only an
action that actually suspends moves the rest of the call into a continuation, and then the whole of the rest of the
call — the transition, the events it queued, the release — finishes in a single async method, whose state machine
is pooled on `net6.0` and later. A suspending action costs what the action itself costs, plus that one continuation.

While a transition's actions are awaited, the transition is still running: it [runs to
completion](#run-to-completion) before the next trigger.

## Run to completion

A transition finishes — through every awaited action — before the next trigger is processed. A trigger fired
*during* a transition, such as an action calling `Enqueue(new Error())`, is queued and processed at step 9.
`Enqueue` exists for code running inside the machine — an action, a hook, a decision. Called from anywhere else, it
throws `InvalidOperationException`: from outside, use `FireAsync`. The
queue is a small buffer inside the machine that allocates only if more than four events queue at once. Values are
never queued this way; see [decisions](decisions.md#deferral-and-backpressure).
````

`docs/concepts/lifecycle.md`:

````markdown
# Lifecycle

A machine is constructed, started, fired, and stopped.

```csharp
await using var telnet = new SampleTelnet(context);   // not started: nothing has run
await telnet.StartAsync();                            // [Entered] actions on the initial path, root first
await telnet.FireAsync(bytes);                        // running
await telnet.StopAsync();                             // [Exited] actions from the leaf to the root
```

| Status | How it gets there | Firing |
|---|---|---|
| `NotStarted` | construction | throws `MachineNotRunningException` |
| `Running` | `StartAsync` completed | allowed |
| `Stopped` | `StopAsync`, or disposal after starting | throws `MachineNotRunningException` |

## Why starting is separate

**Construction runs no actions.** It resets every state's storage and sets the active leaf to the root's initial
path — nothing more, so it cannot fail and cannot await.

**`StartAsync` runs the initial path's `[Entered]` actions once** — a second call throws
`InvalidOperationException`. Those actions may `Enqueue` events; they run before the first trigger. If one throws,
the [exception hooks](exceptions.md) apply as they do in a transition, and `Skip` skips the remaining lifecycle
actions. Keeping it separate means the host finishes
wiring — attaching the machine to a connection, a pipe, a writer — before any action runs. A telnet server that
speaks first, sending its offers as soon as a client connects, sends them from an `[Entered]` action: under
`StartAsync`, not in a constructor, and not waiting for input that may never come.

Forgetting to start is caught twice:

- **At compile time**, where it can be seen: [`SALCH0801`](../reference/diagnostics.md#salch0801) warns when a
  method creates a machine and fires it without starting it on every path. A machine that crosses methods, fields
  or dependency injection is left to the runtime check.
- **At runtime**, for one comparison: the first thing any `FireAsync` does is check the machine's status, and a
  machine that is not running throws a clear exception instead of dispatching. That check is a field read and a
  branch the processor predicts — it does not show up in the [measured cost](concurrency.md) of a call.

## Stopping and disposal

`StopAsync` cancels a pending [decision](decisions.md), then runs `[Exited]` actions from the active leaf up to the
root. `DisposeAsync` stops the machine if it was started, and does nothing if it was not. Stopping twice is
harmless.
````

"Not started" and "stopped" were said to cost nothing. They cost one comparison — below what the benchmarks can
measure, which is a different claim and the true one.

- [ ] **Step 3: The spec's table**

Fill in §9's measured column and correct the baseline row: a hand-written stay or move inlines to a field
increment, which neither the JIT nor BenchmarkDotNet can separate from an empty method, so "within 2×" is a number
only where the baseline does measurable work — the 1 KB run, where the generated machine is 1.7×.

- [ ] **Step 4: The docs match the samples**

Run: `dotnet mdsnippets && git diff --exit-code -- '*.md'`
Expected: no diff.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Say what a machine costs, from the measurements"
```

---

## What Plan 6 leaves

- **A stay whose only step is an async action** enters two state machines on the way back, not one: the generated
  transition could return the action's `ValueTask` directly when nothing follows the await. Worth roughly the 216 B
  a suspending call costs over awaiting the action alone.
- **The inbox costs about 65 ns a call** — two lock regions, claiming the pump and releasing it. A lock-free claim
  for the idle case would cut most of it, at the price of mixing two synchronisation schemes in one file.
- **`InboxCapacity` is honoured by generated machines only.** The reference interpreter still ignores it; the
  contract that covers it therefore runs against generated machines alone.

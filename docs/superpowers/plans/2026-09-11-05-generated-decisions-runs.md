# StateAlchemist Plan 5 — Generated Decisions, Deferral, Runs and `Serialized` Machines — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate what Plan 4 refused — async and synchronous decisions with their pending state, deferral and
backpressure, run transitions, and `Serialized` machines — so that every contract, Plan 3's included, passes against
generated machines.

**Architecture:** The generator writes the reference interpreter's inbox-and-pump design (Plan 3) as C# 7.3, and
writes it only into machines that need it: a `Serialized` machine, or one with an async decision. Every other
machine keeps Plan 4's inline path. A decision is a generated `D` method per leaf — inline for `Decide`, entering the
pending slot for `DecideAsync` — and each outcome a generated `C` method: a move whose transform is that outcome's
`Complete`. Runs are a generated `RunOf` `switch` — the stop set's complement, resolved at compile time — a scan, and
a transform handed a `ReadOnlySpan` of the caller's own batch.

**Tech Stack:** as Plan 4.

**Spec:** [`docs/superpowers/specs/2026-09-11-statealchemist-design.md`](../specs/2026-09-11-statealchemist-design.md)
— §5.6, §6.5–§6.7, §6.10. The rules this plan generates are stated once, executably, in Plan 3's
`ReferenceMachine.Inbox.cs` and `ReferenceMachine.Decisions.cs`. The roadmap: [`2026-09-11-00-roadmap.md`](2026-09-11-00-roadmap.md).

> **Validated (2026-09-11):** built on Plans 1–4 and run on net8.0, net10.0 and net11.0 — 909 tests, all passing:
> all 76 contracts against generated machines, stable over 15 repeated runs, and generated and interpreted machines
> agreeing on random machines with runs fed random batches. The code below is that code.

## Global Constraints

Plan 4's constraints hold — plain C# 7.3, no reflection, no warning — and one more: **the inbox is written only where
it is needed.** A `Checked` or `Unchecked` machine without an async decision runs each call inline, as in Plan 4; the
pending-decision parts of an inbox are written only into machines with an async decision. Unused generated fields
would be compiler warnings, and so errors.

Performance is Plan 6's: this plan allocates an input per call and a pending slot per decision, and uses a locked
list for the inbox, exactly as the interpreter does. The bounded inbox (`InboxCapacity`) arrives with Plan 6's
`Channel`.

## File structure

```
src/StateAlchemist.Generators/
    MachineEmitter.Decisions.cs   new: D methods (decide), C methods (outcomes), RunDecision, ApplyDecision, Handles
    MachineEmitter.Runs.cs        new: RunOf, RunLength, DispatchRun, One
    MachineEmitter.Inbox.cs       new: Input, Pending, Submit, the pump and its steps, Fail, Abandon, EndPending, ReleaseEnded
    MachineEmitter.cs · .Firing.cs · .Transitions.cs · .Lifecycle.cs   changed: the two paths, steps for outcomes and runs
    KnownTypes.cs · SymbolModelBuilder.cs                              changed: DecisionFailed is always known to a deciding machine
tests/StateAlchemist.Generated.Tests/   Plan 3's shapes declared; four contracts inherited
tests/StateAlchemist.Generators.Tests/  the C# 7.3 test and the random agreement extended
```

---

### Task 1: Declare Plan 3's machines

**Files:**
- Modify: `tests/StateAlchemist.Generated.Tests/Machines.cs`, `GeneratedHarness.cs`, `GeneratedContracts.cs`

**Interfaces:**
- Consumes: Plan 3's contract machines and contracts; Plan 4's generated test project.
- Produces: `RecorderSerializedMachine`, `DecidingMachine`, `DecidingSerializedMachine`, `RunsMachine`; the decision,
  backpressure, run and serialized contracts inherited against them.

- [ ] **Step 1: Declare them and inherit the contracts**

`tests/StateAlchemist.Generated.Tests/Machines.cs`:

```csharp
using System;
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Contracts.Machines.Runs;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Generated.Tests;

// One [Machine] per contract shape, with the same modules the shape names. Each forwards its hooks to the test's
// ContractHooks; the "Handled" variant also implements the exception hooks, so the generator writes their try/catch.

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class RecorderMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnUnhandled(StateId state, in Ping e) => Hooks?.UnhandledEvent(StateType, e);
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Unchecked)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class RecorderUncheckedMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);
}

[Machine(Root = typeof(GuardRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(GuardModule))]
public sealed partial class GuardsMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);
}

[Machine(Root = typeof(GuardRoot), Value = typeof(byte), Context = typeof(RecordingContext), Unhandled = Unhandled.Throw)]
[Include(typeof(GuardModule))]
public sealed partial class GuardsThatThrowMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(FailRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(FailureModule))]
public sealed partial class FailuresMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);
}

[Machine(Root = typeof(FailRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(FailureModule))]
public sealed partial class FailuresHandledMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnGuardException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnTransformException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnExitedException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnEnteredException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnCompletedException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);
}

[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class TelnetMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class RecorderSerializedMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(DecidingModule))]
public sealed partial class DecidingMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnUnhandled(StateId state, in DecisionFailed e) => Hooks?.UnhandledEvent(StateType, e);
}

[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized)]
[Include(typeof(DecidingModule))]
public sealed partial class DecidingSerializedMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(RunRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(RunModule))]
public sealed partial class RunsMachine
{
    public ContractHooks? Hooks { get; set; }
}
```

`tests/StateAlchemist.Generated.Tests/GeneratedHarness.cs`:

```csharp
using System;
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Generated.Tests;

/// <summary>Turns a contract shape into its generated machine.</summary>
internal static class GeneratedHarness
{
    public static IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks)
    {
        var handled = hooks is { HandleExceptions: true };
        return (shape.Name, handled) switch
        {
            ("Recorder", false) => new RecorderMachine((RecordingContext)context) { Hooks = hooks },
            ("RecorderUnchecked", false) => new RecorderUncheckedMachine((RecordingContext)context) { Hooks = hooks },
            ("Guards", false) => new GuardsMachine((RecordingContext)context) { Hooks = hooks },
            ("GuardsThatThrow", false) => new GuardsThatThrowMachine((RecordingContext)context) { Hooks = hooks },
            ("Failures", false) => new FailuresMachine((RecordingContext)context) { Hooks = hooks },
            ("Failures", true) => new FailuresHandledMachine((RecordingContext)context) { Hooks = hooks },
            ("SampleTelnet", false) => new TelnetMachine((TelnetContext)context) { Hooks = hooks },
            ("RecorderSerialized", false) => new RecorderSerializedMachine((RecordingContext)context) { Hooks = hooks },
            ("Deciding", false) => new DecidingMachine((RecordingContext)context) { Hooks = hooks },
            ("DecidingSerialized", false) => new DecidingSerializedMachine((RecordingContext)context) { Hooks = hooks },
            ("Runs", false) => new RunsMachine((RecordingContext)context) { Hooks = hooks },
            _ => throw new NotSupportedException($"No generated machine for shape '{shape.Name}'{(handled ? " with exception hooks" : "")}."),
        };
    }
}
```

`tests/StateAlchemist.Generated.Tests/GeneratedContracts.cs`:

```csharp
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Suite;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

// Every contract, run against generated machines.

[InheritsTests]
public sealed class GeneratedLifecycle : LifecycleContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedTransitions : TransitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedResolution : ResolutionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedEvents : EventContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedUnhandled : UnhandledContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedExceptions : ExceptionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedPlans : PlanContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedDefinitions : DefinitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedConcurrency : ConcurrencyContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedTelnet : TelnetContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedDecisions : DecisionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedBackpressure : BackpressureContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedRuns : RunContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedSerialized : SerializedContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}
```

`DecidingMachine` implements the `DecisionFailed` overload of `OnUnhandled`: that is how
`AFailureNothingHandlesIsAnUnhandledTrigger` sees a failure nothing recovers from.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: FAIL — 25 contracts per framework, each with Plan 4's
`NotSupportedException: Decisions, runs and Serialized machines are generated from Plan 5 of the StateAlchemist
implementation plans.`

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Generated machines for decisions, runs and the serialized mode, failing"
```

---

### Task 2: Generate decisions, the inbox and runs

**Files:**
- Modify: `src/StateAlchemist.Generators/KnownTypes.cs`, `SymbolModelBuilder.cs`
- Modify: `src/StateAlchemist.Generators/MachineEmitter.cs`, `MachineEmitter.Firing.cs`, `MachineEmitter.Transitions.cs`,
  `MachineEmitter.Lifecycle.cs`
- Create: `src/StateAlchemist.Generators/MachineEmitter.Decisions.cs`, `MachineEmitter.Runs.cs`, `MachineEmitter.Inbox.cs`

**Interfaces:**
- Consumes: Task 1; Plan 4's emitter.
- Produces: generated machines that pass all 76 contracts. The generated members Plan 4 wrote are unchanged; the
  inbox, pending slot, decision and run methods are private.

- [ ] **Step 1: Let a deciding machine know `DecisionFailed`**

A failed decision fires `DecisionFailed` whether or not a transition handles it, so the machine needs the type — for
its `OnUnhandled` overload, its queue, and its dispatch — even when no module names it.

`src/StateAlchemist.Generators/KnownTypes.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace StateAlchemist.Generators;

/// <summary>The runtime and BCL types the front-end recognises, resolved once per compilation.</summary>
internal sealed class KnownTypes(Compilation compilation)
{
    public INamedTypeSymbol? Machine { get; } = compilation.GetTypeByMetadataName("StateAlchemist.MachineAttribute");

    public INamedTypeSymbol? Include { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IncludeAttribute");

    public INamedTypeSymbol? Module { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ModuleAttribute");

    public INamedTypeSymbol? Transition { get; } = compilation.GetTypeByMetadataName("StateAlchemist.TransitionAttribute");

    public INamedTypeSymbol? Decision { get; } = compilation.GetTypeByMetadataName("StateAlchemist.DecisionAttribute");

    public INamedTypeSymbol? On { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnAttribute");

    public INamedTypeSymbol? OnRange { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnRangeAttribute");

    public INamedTypeSymbol? OnAny { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnAnyAttribute");

    public INamedTypeSymbol? OnEvent { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnEventAttribute");

    public INamedTypeSymbol? Run { get; } = compilation.GetTypeByMetadataName("StateAlchemist.RunAttribute");

    public INamedTypeSymbol? To { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ToAttribute");

    public INamedTypeSymbol? Exited { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ExitedAttribute");

    public INamedTypeSymbol? Entered { get; } = compilation.GetTypeByMetadataName("StateAlchemist.EnteredAttribute");

    public INamedTypeSymbol? Initial { get; } = compilation.GetTypeByMetadataName("StateAlchemist.InitialAttribute");

    public INamedTypeSymbol? RootState { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IRootState");

    public INamedTypeSymbol? State { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IState`1");

    public INamedTypeSymbol? Event { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IEvent");

    public INamedTypeSymbol? DecisionFailed { get; } = compilation.GetTypeByMetadataName("StateAlchemist.DecisionFailed");

    public INamedTypeSymbol? TransitionInfo { get; } = compilation.GetTypeByMetadataName("StateAlchemist.TransitionInfo`1");

    public INamedTypeSymbol? ValueTask { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");

    public INamedTypeSymbol? ValueTaskOfT { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

    public INamedTypeSymbol? Task { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");

    public INamedTypeSymbol? TaskOfT { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");

    public INamedTypeSymbol? ReadOnlySpan { get; } = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");

    public INamedTypeSymbol? ReadOnlyMemory { get; } = compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1");

    public INamedTypeSymbol? CancellationToken { get; } = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
}

/// <summary>Compares by reference: the model's records compare their lists by reference anyway, and two equal methods are still two methods.</summary>
internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly ReferenceComparer<T> Instance = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
```

`src/StateAlchemist.Generators/SymbolModelBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

/// <summary>
/// Builds a <see cref="MachineModel"/> from a <c>[Machine]</c> class's symbols — rule for rule the model the reference
/// front-end builds by reflection, so the generator and the interpreter are checked by the same analysis. The
/// agreement is a test (<c>FrontEndAgreementTests</c>), not a hope.
/// </summary>
internal static class SymbolModelBuilder
{
    private static readonly string[] TransitionPhases = ["Guard", "Transform", "Completed", "CompletedAsync"];
    private static readonly string[] DecisionPhases = ["Guard", "Decide", "DecideAsync", "Complete", "Completed", "CompletedAsync"];

    /// <summary>Builds the model of <paramref name="machine"/>. Problems become diagnostics; nothing throws for a bad declaration.</summary>
    public static SymbolMachine Build(INamedTypeSymbol machine, Compilation compilation) => new Builder(machine, new KnownTypes(compilation)).Run();

    private sealed class Builder(INamedTypeSymbol machine, KnownTypes known)
    {
        private readonly List<ModelDiagnostic> _diagnostics = [];
        private readonly Dictionary<MethodModel, IMethodSymbol> _methods = new(ReferenceComparer<MethodModel>.Instance);
        private readonly Dictionary<SourceSpan, Location> _locations = [];
        private readonly Dictionary<string, INamedTypeSymbol> _events = [];
        private readonly List<INamedTypeSymbol> _stateTypes = [];
        private MachineOptions _options = new("System.Byte", ValueDomain.Byte);
        private INamedTypeSymbol? _root;
        private ITypeSymbol? _value;
        private ITypeSymbol? _context;
        private ITypeSymbol? _config;

        public SymbolMachine Run()
        {
            var attribute = machine.GetAttributes().First(a => Is(a.AttributeClass, known.Machine));
            var concurrency = ConcurrencyMode.Checked;
            var purity = PurityMode.Permissive;
            var unhandled = UnhandledMode.Ignore;
            var inbox = 0;
            foreach (var argument in attribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "Root":
                        _root = argument.Value.Value as INamedTypeSymbol;
                        break;
                    case "Value":
                        _value = argument.Value.Value as ITypeSymbol;
                        break;
                    case "Context":
                        _context = argument.Value.Value as ITypeSymbol;
                        break;
                    case "Config":
                        _config = argument.Value.Value as ITypeSymbol;
                        break;
                    case "Concurrency":
                        concurrency = (ConcurrencyMode)(int)argument.Value.Value!;
                        break;
                    case "InboxCapacity":
                        inbox = (int)argument.Value.Value!;
                        break;
                    case "Purity":
                        purity = (PurityMode)(int)argument.Value.Value!;
                        break;
                    case "Unhandled":
                        unhandled = (UnhandledMode)(int)argument.Value.Value!;
                        break;
                }
            }

            var machineLocation = Span(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
            if (_root is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, machineLocation, machine.Name, "does not name a Root state"));
            }

            if (_value is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, machineLocation, machine.Name, "does not name a Value type"));
            }

            _options = Options(concurrency, inbox, purity, unhandled);
            var modules = machine.GetAttributes()
                .Where(a => Is(a.AttributeClass, known.Include))
                .Select(a => a.ConstructorArguments[0].Value)
                .OfType<INamedTypeSymbol>()
                .Where(module => IsModule(module, machineLocation))
                .ToList();
            var declarations = modules.SelectMany(Declarations).ToList();
            CollectStates(declarations);
            var stateIndex = new Dictionary<ITypeSymbol, int>(SymbolEqualityComparer.Default);
            for (var i = 0; i < _stateTypes.Count; i++)
            {
                stateIndex[_stateTypes[i]] = i;
            }

            var states = _stateTypes.Select((type, index) => StateModelOf(type, index, stateIndex)).ToList();
            var transitions = new List<TransitionModel>();
            var actions = new List<StateActionModel>();
            foreach (var declaration in declarations)
            {
                switch (declaration)
                {
                    case TransitionDeclaration transition:
                        foreach (var transitionModel in TransitionModels(transition, stateIndex))
                        {
                            transitions.Add(transitionModel with { Index = transitions.Count });
                        }

                        break;
                    case ActionDeclaration action:
                        actions.Add(new StateActionModel(action.Phase, stateIndex[action.State], action.Order, MetadataName(action.Module), action.DeclarationIndex,
                            MethodModelOf(action.Method, DisplayName(action.Module), stateIndex, [])));
                        break;
                }
            }

            // A failed decision fires DecisionFailed whether or not a transition handles it: the machine must know the type.
            if (transitions.Any(t => t.IsDecision) && known.DecisionFailed is { } failed)
            {
                Event(failed);
            }

            var model = new MachineModel(machine.Name, _options, states, transitions, actions, _diagnostics);
            return new SymbolMachine(machine, model, _stateTypes, _methods, _events, _value, _context, _config, _locations, machineLocation,
                modules.Select(m => MetadataName(m)).ToList());
        }

        private bool IsModule(INamedTypeSymbol module, SourceSpan at)
        {
            if (module.GetAttributes().Any(a => Is(a.AttributeClass, known.Module)))
            {
                return true;
            }

            _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, at, machine.Name, $"includes '{module.Name}', which is not a [Module]"));
            return false;
        }

        /// <summary>
        /// A module's declarations in the order reflection sees them — by metadata token, so nested types (transition and
        /// decision classes) before methods, each in declaration order.
        /// </summary>
        private IEnumerable<object> Declarations(INamedTypeSymbol module)
        {
            foreach (var nested in module.GetTypeMembers())
            {
                if (nested.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.Transition)) is { } transition)
                {
                    yield return new TransitionDeclaration(module, null, nested, TypeArgument(transition, "From"), TypeArgument(transition, "To"),
                        IntArgument(transition, "Order"), false, []);
                }
                else if (nested.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.Decision)) is { } decision)
                {
                    var handle = decision.NamedArguments.FirstOrDefault(a => a.Key == "Handle").Value;
                    var events = handle.Kind == TypedConstantKind.Array ? handle.Values.Select(v => v.Value).OfType<INamedTypeSymbol>().ToArray() : [];
                    yield return new TransitionDeclaration(module, null, nested, TypeArgument(decision, "From"), null, IntArgument(decision, "Order"), true, events);
                }
            }

            var index = 0;
            foreach (var method in module.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
            {
                if (method.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.Transition)) is { } transition)
                {
                    yield return new TransitionDeclaration(module, method, null, TypeArgument(transition, "From"), TypeArgument(transition, "To"),
                        IntArgument(transition, "Order"), false, []);
                    continue;
                }

                foreach (var exited in method.GetAttributes().Where(a => Is(a.AttributeClass, known.Exited)))
                {
                    if (exited.ConstructorArguments[0].Value is INamedTypeSymbol state)
                    {
                        yield return new ActionDeclaration(module, method, ActionPhase.Exited, state, IntArgument(exited, "Order"), index++);
                    }
                }

                foreach (var entered in method.GetAttributes().Where(a => Is(a.AttributeClass, known.Entered)))
                {
                    if (entered.ConstructorArguments[0].Value is INamedTypeSymbol state)
                    {
                        yield return new ActionDeclaration(module, method, ActionPhase.Entered, state, IntArgument(entered, "Order"), index++);
                    }
                }
            }
        }

        private void CollectStates(IReadOnlyList<object> declarations)
        {
            var pending = new List<INamedTypeSymbol>();
            if (_root is not null)
            {
                pending.Add(_root);
            }

            foreach (var declaration in declarations)
            {
                switch (declaration)
                {
                    case TransitionDeclaration t:
                        if (t.From is not null)
                        {
                            pending.Add(t.From);
                        }

                        if (t.To is not null)
                        {
                            pending.Add(t.To);
                        }

                        if (t.Class is not null)
                        {
                            pending.AddRange(t.Class.GetMembers().OfType<IMethodSymbol>()
                                .Select(m => m.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.To))?.ConstructorArguments[0].Value)
                                .OfType<INamedTypeSymbol>());
                        }

                        break;
                    case ActionDeclaration a:
                        pending.Add(a.State);
                        break;
                }
            }

            var found = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            while (pending.Count > 0)
            {
                var type = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);
                if (!found.Add(type))
                {
                    continue;
                }

                if (ParentOf(type) is { } parent && !Is(type, _root))
                {
                    pending.Add(parent);
                }
            }

            var others = found.Where(t => !Is(t, _root)).OrderBy(MetadataName, StringComparer.Ordinal);
            _stateTypes.AddRange(_root is null ? others : [_root, .. others]);
        }

        private INamedTypeSymbol? ParentOf(INamedTypeSymbol state) =>
            state.AllInterfaces.FirstOrDefault(i => Is(i.OriginalDefinition, known.State))?.TypeArguments[0] as INamedTypeSymbol;

        private StateModel StateModelOf(INamedTypeSymbol type, int index, IReadOnlyDictionary<ITypeSymbol, int> stateIndex)
        {
            var markers = type.AllInterfaces.Count(i => Is(i, known.RootState) || Is(i.OriginalDefinition, known.State));
            var isRoot = Is(type, _root);
            var parent = isRoot || ParentOf(type) is not { } parentType ? -1 : stateIndex[parentType];
            var location = Span(type.Locations.FirstOrDefault());
            if (isRoot && ParentOf(type) is not null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, location, type.Name, "is the machine's root but declares a parent"));
            }

            var reset = type.GetMembers("Reset").OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 0 && m.ReturnsVoid);
            return new StateModel(
                index,
                MetadataName(type),
                parent,
                IsInitial: type.GetAttributes().Any(a => Is(a.AttributeClass, known.Initial)),
                HasData: type.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic),
                HasReset: reset,
                IsPublicStruct: type.TypeKind == TypeKind.Struct && IsVisible(type),
                ParentMarkers: markers,
                location);
        }

        private IEnumerable<TransitionModel> TransitionModels(TransitionDeclaration declaration, IReadOnlyDictionary<ITypeSymbol, int> stateIndex)
        {
            var member = (ISymbol?)declaration.Method ?? declaration.Class!;
            var name = DisplayName(declaration.Module) + "." + member.Name;
            var location = Span(member.Locations.FirstOrDefault());
            if (declaration.From is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "does not name its From state"));
                yield break;
            }

            var triggers = Triggers(member, name, location);
            if (triggers.Count == 0)
            {
                yield break;
            }

            var declaringType = declaration.Class is null ? DisplayName(declaration.Module) : DisplayName(declaration.Class);
            var outcomes = declaration.IsDecision ? OutcomesOf(declaration.Class!) : [];
            var classMethods = declaration.Class?.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).ToList() ?? [];
            MethodModel? Phase(string phase) => classMethods.FirstOrDefault(m => m.Name == phase) is { } method
                ? MethodModelOf(method, declaringType, stateIndex, outcomes)
                : null;
            IReadOnlyList<MethodModel> Phases(params string[] phases) =>
                classMethods.Where(m => phases.Contains(m.Name)).Select(m => MethodModelOf(m, declaringType, stateIndex, outcomes)).ToList();

            var transform = declaration.Method is not null ? MethodModelOf(declaration.Method, declaringType, stateIndex, outcomes) : Phase("Transform");
            var knownPhases = declaration.IsDecision ? DecisionPhases : TransitionPhases;
            var unknown = classMethods
                .Where(m => m.DeclaredAccessibility == Accessibility.Public && !knownPhases.Contains(m.Name))
                .Select(m => m.Name).Distinct().ToList();

            DecisionModel? decision = null;
            if (declaration.IsDecision)
            {
                var completions = classMethods.Where(m => m.Name == "Complete").Select(m =>
                {
                    var target = m.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.To))?.ConstructorArguments[0].Value as INamedTypeSymbol;
                    var complete = MethodModelOf(m, declaringType, stateIndex, outcomes);
                    if (target is null)
                    {
                        _diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, complete.Location, complete.FullName, "declare its target with [To]"));
                    }

                    var outcome = m.Parameters.Select(p => MetadataName(p.Type)).FirstOrDefault(outcomes.Contains) ?? string.Empty;
                    return new OutcomeCompletion(outcome, target is null ? -1 : stateIndex[target], complete);
                }).Where(c => c.Target >= 0).ToList();
                decision = new DecisionModel(Phase("Decide"), Phase("DecideAsync"), outcomes, completions, declaration.Handle.Select(h => Event(h)).ToList());
            }

            var source = stateIndex[declaration.From];
            var target = declaration.To is null ? -1 : stateIndex[declaration.To];
            var isRun = member.GetAttributes().Any(a => Is(a.AttributeClass, known.Run));
            foreach (var trigger in triggers)
            {
                yield return new TransitionModel(0, name, source, target, trigger, declaration.Order, isRun, Phase("Guard"), transform,
                    Phases("Completed", "CompletedAsync"), decision, unknown, MetadataName(declaration.Module), location);
            }
        }

        private List<TriggerModel> Triggers(ISymbol member, string name, SourceSpan location)
        {
            var triggers = new List<TriggerModel>();
            var attributes = member.GetAttributes();
            foreach (var on in attributes.Where(a => Is(a.AttributeClass, known.On)))
            {
                var value = on.ConstructorArguments[0];
                if (ToInt64(value) is { } number)
                {
                    triggers.Add(TriggerModel.Value(number));
                }
                else
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, $"fires on '{Text(value)}', which is not an integral constant"));
                }
            }

            foreach (var range in attributes.Where(a => Is(a.AttributeClass, known.OnRange)))
            {
                if (ToInt64(range.ConstructorArguments[0]) is not { } low || ToInt64(range.ConstructorArguments[1]) is not { } high)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "has a range whose ends are not integral constants"));
                }
                else if (high < low)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, $"has an empty range {low}..{high}"));
                }
                else
                {
                    triggers.Add(TriggerModel.Range(low, high));
                }
            }

            if (attributes.Any(a => Is(a.AttributeClass, known.OnAny)))
            {
                triggers.Add(TriggerModel.Any);
            }

            var onEvent = attributes.FirstOrDefault(a => Is(a.AttributeClass, known.OnEvent));
            if (onEvent is not null && triggers.Count > 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "mixes value and event triggers"));
                return [];
            }

            if (onEvent?.ConstructorArguments[0].Value is INamedTypeSymbol eventType)
            {
                triggers.Add(TriggerModel.Event(Event(eventType)));
            }

            if (triggers.Count == 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "has no trigger"));
            }

            return triggers;
        }

        private string Event(INamedTypeSymbol type)
        {
            var name = MetadataName(type);
            _events[name] = type;
            return name;
        }

        private MethodModel MethodModelOf(IMethodSymbol method, string declaringType, IReadOnlyDictionary<ITypeSymbol, int> stateIndex, IReadOnlyList<string> outcomes)
        {
            var parameters = method.Parameters.Select(p =>
            {
                var passing = p.RefKind switch
                {
                    RefKind.None => Passing.Value,
                    RefKind.Ref => Passing.Ref,
                    RefKind.Out => Passing.Out,
                    _ => Passing.In,
                };
                var kind = ParameterClassifier.Classify(Facts(p.Type), _options, n => IndexOf(n, stateIndex), outcomes, out var state);
                if (kind == ParameterKind.Event && p.Type is INamedTypeSymbol eventType)
                {
                    Event(eventType);
                }

                return new ParameterModel(p.Name.Length == 0 ? "_" : p.Name, FriendlyName(p.Type), kind, passing, state);
            }).ToList();

            var model = new MethodModel(declaringType, method.Name, ReturnShapeOf(method.ReturnType), method.IsStatic,
                method.DeclaredAccessibility == Accessibility.Public && IsVisible(method.ContainingType), parameters, Span(method.Locations.FirstOrDefault()));
            _methods[model] = method;
            return model;
        }

        private static int IndexOf(string fullName, IReadOnlyDictionary<ITypeSymbol, int> stateIndex)
        {
            foreach (var pair in stateIndex)
            {
                if (MetadataName(pair.Key) == fullName)
                {
                    return pair.Value;
                }
            }

            return -1;
        }

        private TypeFacts Facts(ITypeSymbol type)
        {
            string? ArgumentOf(INamedTypeSymbol? definition) =>
                type is INamedTypeSymbol { IsGenericType: true } generic && Is(generic.OriginalDefinition, definition) ? MetadataName(generic.TypeArguments[0]) : null;

            return new TypeFacts(
                MetadataName(type),
                IsEvent: type.AllInterfaces.Any(i => Is(i, known.Event)),
                SpanOf: ArgumentOf(known.ReadOnlySpan),
                MemoryOf: ArgumentOf(known.ReadOnlyMemory),
                IsCancellationToken: Is(type, known.CancellationToken),
                TransitionInfoOf: ArgumentOf(known.TransitionInfo));
        }

        private IReadOnlyList<string> OutcomesOf(INamedTypeSymbol decision)
        {
            var decide = decision.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name is "Decide" or "DecideAsync");
            var union = decide?.ReturnType as INamedTypeSymbol;
            if (union is { IsGenericType: true } && (Is(union.OriginalDefinition, known.ValueTaskOfT) || Is(union.OriginalDefinition, known.TaskOfT)))
            {
                union = union.TypeArguments[0] as INamedTypeSymbol;
            }

            if (union is null || !union.GetAttributes().Any(a => a.AttributeClass is { } c && MetadataName(c) == "System.Runtime.CompilerServices.UnionAttribute"))
            {
                return [];
            }

            return union.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 1)
                .Select(c => MetadataName(c.Parameters[0].Type))
                .ToList();
        }

        private MachineOptions Options(ConcurrencyMode concurrency, int inbox, PurityMode purity, UnhandledMode unhandled)
        {
            var value = _value;
            var underlying = value is INamedTypeSymbol { EnumUnderlyingType: { } enumType } ? enumType : value;
            ValueDomain? domain = underlying?.SpecialType switch
            {
                SpecialType.System_Byte => new ValueDomain(byte.MinValue, byte.MaxValue),
                SpecialType.System_SByte => new ValueDomain(sbyte.MinValue, sbyte.MaxValue),
                SpecialType.System_Int16 => new ValueDomain(short.MinValue, short.MaxValue),
                SpecialType.System_UInt16 => new ValueDomain(ushort.MinValue, ushort.MaxValue),
                SpecialType.System_Char => new ValueDomain(char.MinValue, char.MaxValue),
                _ => null,
            };

            if (domain is not null && value is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumSymbol)
            {
                domain = domain with
                {
                    Members = enumSymbol.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
                        .Select(f => Convert.ToInt64(f.ConstantValue)).Distinct().OrderBy(v => v).ToList(),
                };
            }

            return new MachineOptions(
                value is null ? "System.Byte" : MetadataName(value),
                domain ?? new ValueDomain(0, 0),
                ValueTypeSupported: domain is not null,
                _context is null ? null : MetadataName(_context),
                _config is null ? null : MetadataName(_config),
                concurrency,
                inbox,
                purity,
                unhandled);
        }

        private SourceSpan Span(Location? location)
        {
            if (location is not { IsInSource: true })
            {
                return SourceSpan.None;
            }

            var line = location.GetLineSpan();
            var span = new SourceSpan(line.Path, line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1);
            _locations[span] = location;
            return span;
        }

        private bool Is(ISymbol? symbol, ISymbol? other) => other is not null && SymbolEqualityComparer.Default.Equals(symbol, other);

        private static INamedTypeSymbol? TypeArgument(AttributeData attribute, string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as INamedTypeSymbol;

        private static int IntArgument(AttributeData attribute, string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is int value ? value : 0;

        private static long? ToInt64(TypedConstant constant) => constant.Kind == TypedConstantKind.Array ? null : constant.Value switch
        {
            byte or sbyte or short or ushort or int or uint or long or char => Convert.ToInt64(constant.Value),
            _ => null,
        };

        private static string Text(TypedConstant constant) => constant.Kind == TypedConstantKind.Array ? "array" : constant.Value?.ToString() ?? "null";

        private ReturnShape ReturnShapeOf(ITypeSymbol type) => type switch
        {
            _ when type.SpecialType == SpecialType.System_Void => ReturnShape.Void,
            _ when type.SpecialType == SpecialType.System_Boolean => ReturnShape.Bool,
            _ when Is(type, known.ValueTask) => ReturnShape.ValueTask,
            _ when Is(type, known.Task) => ReturnShape.Task,
            INamedTypeSymbol { IsGenericType: true } g when Is(g.OriginalDefinition, known.ValueTaskOfT) => ReturnShape.ValueTaskOfResult,
            INamedTypeSymbol { IsGenericType: true } g when Is(g.OriginalDefinition, known.TaskOfT) => ReturnShape.TaskOfResult,
            _ => ReturnShape.Other,
        };
    }

    private static bool IsVisible(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public && (type.ContainingType is null || IsVisible(type.ContainingType));

    /// <summary>As written in C#, without the namespace: <c>TelnetCore</c>, <c>TelnetCore.Refuse</c>.</summary>
    internal static string DisplayName(INamedTypeSymbol type) => type.ContainingType is null ? type.Name : DisplayName(type.ContainingType) + "." + type.Name;

    /// <summary>What reflection's <c>Type.FullName</c> gives for the types a machine uses: <c>Ns.Outer+Inner</c>, <c>System.Byte[]</c>.</summary>
    internal static string MetadataName(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return MetadataName(array.ElementType) + "[]";
            case INamedTypeSymbol { ContainingType: { } outer } nested:
                return MetadataName(outer) + "+" + nested.MetadataName;
            default:
                var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() + "." : string.Empty;
                return ns + type.MetadataName;
        }
    }

    /// <summary>A parameter type's name as the reference front-end records it: generic types written out with their arguments.</summary>
    internal static string FriendlyName(ITypeSymbol type) => type is INamedTypeSymbol { IsGenericType: true } generic
        ? $"{MetadataName(generic.OriginalDefinition).Split('`')[0]}<{string.Join(", ", generic.TypeArguments.Select(FriendlyName))}>"
        : MetadataName(type);

    private sealed record TransitionDeclaration(INamedTypeSymbol Module, IMethodSymbol? Method, INamedTypeSymbol? Class, INamedTypeSymbol? From, INamedTypeSymbol? To, int Order, bool IsDecision, INamedTypeSymbol[] Handle);

    private sealed record ActionDeclaration(INamedTypeSymbol Module, IMethodSymbol Method, ActionPhase Phase, INamedTypeSymbol State, int Order, int DeclarationIndex);
}
```

- [ ] **Step 2: Two paths through the shell and the entry points**

`HasInbox` decides the path: `Serialized`, or any `DecideAsync`. Plan 4's constructor refusal is gone.

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
            _w.Line($"private readonly {V}[] _one = new {V}[1];");
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
                _w.Line($"return Submit(new Input {{ Values = new {V}[] {{ value }} }});");
            }
            else
            {
                Entry("ProcessValue(value)");
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync(global::System.ReadOnlyMemory<{V}> values)"))
        {
            if (HasInbox)
            {
                _w.Line("return Submit(new Input { Values = values });");
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
                    _w.Line($"var input = new Input {{ Tag = {i} }};");
                    _w.Line($"input.E{i} = e;");
                    _w.Line("return Submit(input);");
                }
                else
                {
                    Entry($"ProcessEvent{i}(e)");
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
                _w.Line("return Submit(new Input { Tag = -1, Unknown = typeof(TEvent) });");
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
                    _w.Line($"var queued = new Input {{ Tag = {i} }};");
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
                _w.Line("EnqueueInput(new Input { Tag = -1, Unknown = typeof(TEvent) });");
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
        using (_w.Block($"private async {ValueTaskType} FinishAsync({ValueTaskType} pending)"))
        {
            _w.Line("try { await pending; } finally { Release(); }");
        }

        // One trigger: events kept from a transition that threw run first, then the trigger, then step 9.
        _w.Line();
        using (_w.Block($"private async {ValueTaskType} ProcessValue({V} value)"))
        {
            _w.Line("await DrainQueue();");
            _w.Line("_inside = true;");
            _w.Line("try { await DispatchValue(value); } finally { _inside = false; }");
            _w.Line("await DrainQueue();");
        }

        _w.Line();
        using (_w.Block($"private async {ValueTaskType} ProcessValues(global::System.ReadOnlyMemory<{V}> values)"))
        {
            using (_w.Block("for (var i = 0; i < values.Length;)"))
            {
                _w.Line("await DrainQueue();");
                _w.Line(HasRuns ? "var count = RunLength(values.Slice(i));" : "var count = 1;");
                _w.Line("_inside = true;");
                _w.Line("try { await DispatchAt(values, i, count); } finally { _inside = false; }");
                _w.Line("i += count;");
                _w.Line("await DrainQueue();");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            using (_w.Block($"private async {ValueTaskType} ProcessEvent{i}({Name(_events[i])} e)"))
            {
                _w.Line("await DrainQueue();");
                _w.Line("_inside = true;");
                _w.Line($"try {{ await DispatchEvent{i}(e); }} finally {{ _inside = false; }}");
                _w.Line("await DrainQueue();");
            }
        }

        _w.Line();
        using (_w.Block($"private async {ValueTaskType} ProcessUnknown({TypeType} type)"))
        {
            _w.Line("await DrainQueue();");
            _w.Line("UnhandledUnknown(type);");
        }

        _w.Line();
        using (_w.Block($"private {(_events.Count > 0 ? "async " : "")}{ValueTaskType} DrainQueue()"))
        {
            using (_w.Block("while (_queue != null && _queue.Count != 0)"))
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

            if (_events.Count == 0)
            {
                _w.Line($"return default({ValueTaskType});");
            }
        }
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

On the inline path, `ProcessValues` now asks `RunLength` how many values the next call takes; `DispatchAt` sends a
run to `DispatchRun` and anything else to the value `switch`. A candidate list can also reach a run transition for a
single value — a guarded stop value whose guard fails falls through to the run — and `One(value)` hands it a run of
one from a reused one-element buffer, which is safe because one trigger runs at a time.

- [ ] **Step 3: Steps for outcomes and runs**

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

`WriteTransition` became `WriteSteps`, parameterised by the transform, the `Completed` methods and the kind, so a
decision outcome is written by the same code as any transition: its `Complete` is the transform, reported to hooks as
`Phase.Complete`. A move ends a pending decision at commit (step 4). A run's transform is called from a synchronous
helper, `…_Run`, because a span cannot live in an `async` method under C# 7.3.

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

- [ ] **Step 4: Decisions**

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

            _w.Line("await Pump();");
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

The outcome is read from the union's `Value` and matched by type — for the contracts' `Ask`, `if (outcome is …Accept)
return C0_0_Asking(value, (…Accept)outcome);` — which works on any compiler, where matching the union itself would
need C# 15.

- [ ] **Step 5: Runs**

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

        _w.Line();
        _w.Line("/// <summary>How many values from the start of <paramref name=\"values\"/> go to one call: a whole run, or one value.</summary>");
        using (_w.Block($"private int RunLength(global::System.ReadOnlyMemory<{V}> values)"))
        {
            _w.Line("var span = values.Span;");
            _w.Line("var run = RunOf(span[0]);");
            _w.Line("if (run < 0) return 1;");
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
        _w.Line("/// <summary>A single value as a run of one. The buffer is reused: one trigger runs at a time.</summary>");
        _w.Line($"private global::System.ReadOnlyMemory<{V}> One({V} value) {{ _one[0] = value; return _one; }}");
    }
}
```

`RunOf` is written by the same `WriteValueCases` as dispatch: for each value of the domain, the run transition that
resolution would try first, or −1. That is the stop set of spec §6.7, computed by the model's own resolver.

- [ ] **Step 6: The inbox and the pump**

`src/StateAlchemist.Generators/MachineEmitter.Inbox.cs`:

```csharp
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// The inbox and the pump, written into machines that are Serialized or have an async decision — the design the
// reference interpreter runs (Plan 3), in generated C# 7.3. Every FireAsync is an input; whoever finds the machine
// idle pumps, one trigger per step; a pending decision pauses the input that started it while the machine accepts
// the events it handles. See ReferenceMachine.Inbox.cs for the rules, stated once. The pending-decision parts are
// written only into machines that have an async decision.
internal sealed partial class MachineEmitter
{
    private const string Task = "global::System.Threading.Tasks.Task";

    /// <summary>Whether the machine has an async decision: only then does its inbox carry a pending state.</summary>
    private bool Deciding => _model.Transitions.Any(t => t.Decision?.DecideAsync is not null);

    private void WriteInboxStorage()
    {
        _w.Line("private readonly object _sync = new object();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _inbox = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _queued = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private Input _current;");
        _w.Line("private bool _pumping;");
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
        WriteSubmit();
        WritePump();
        WritePumpSteps();
        WriteEndings();
    }

    private void WriteInboxTypes()
    {
        _w.Line();
        _w.Line("/// <summary>One caller's input — a batch of values, or one event — or an event queued inside the machine.</summary>");
        using (_w.Block("private sealed class Input"))
        {
            _w.Line($"public global::System.ReadOnlyMemory<{V}> Values;");
            _w.Line("public int Next;");
            _w.Line("public int Tag = -2;");
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"public {Name(_events[i])} E{i};");
            }

            _w.Line($"public {TypeType} Unknown;");
            _w.Line($"public {Task}CompletionSource<bool> Done;");
            if (Deciding)
            {
                _w.Line("public bool ArrivedWhilePending;");
            }

            _w.Line("public bool IsEvent { get { return Tag != -2; } }");
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

    private void WriteSubmit()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} Submit(Input input)"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) return Faulted(new {Rt}MachineNotRunningException(_status));");
            if (Deciding)
            {
                // Code inside the machine that fires it would wait for itself: caught in a decision, and in any transition while one is pending.
                _w.Line("var flow = _flow.Value;");
                _w.Line($"if (flow != null && (flow is Pending || _pending != null)) return Faulted(new {Rt}ConcurrentUseException());");
            }

            if (IsChecked)
            {
                _w.Line("var holdsBusy = false;");
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
                    var refuse = $"if (_busy) return Faulted(new {Rt}ConcurrentUseException()); _busy = holdsBusy = true;";
                    _w.Line(Deciding ? $"if (!(input.IsEvent && input.ArrivedWhilePending)) {{ {refuse} }}" : refuse);
                }

                _w.Line($"input.Done = new {Task}CompletionSource<bool>(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);");
                _w.Line("_inbox.Add(input);");
            }

            _w.Line(IsChecked ? "return Await(input, holdsBusy);" : "return Await(input);");
        }

        _w.Line();
        using (_w.Block($"private async {ValueTaskType} Await(Input input{(IsChecked ? ", bool holdsBusy" : "")})"))
        {
            if (IsChecked)
            {
                _w.Line("try { await Pump(); await input.Done.Task; }");
                _w.Line("finally { if (holdsBusy) { lock (_sync) { _busy = false; } } }");
            }
            else
            {
                _w.Line("await Pump();");
                _w.Line("await input.Done.Task;");
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
                    _w.Line("lock (_sync) { if (decision != _pending) return; _queued.Add(queued); }");
                    _w.Line("// The machine is idle while a decision runs: start a pump, off the decision's stack.");
                    _w.Line($"using (global::System.Threading.ExecutionContext.SuppressFlow()) {{ {Task}.Run(new global::System.Func<{Task}>(Pump)); }}");
                    _w.Line("return;");
                }
            }

            _w.Line("RefuseOutside();");
            _w.Line("lock (_sync) { _queued.Add(queued); }");
        }
    }

    private void WritePump()
    {
        _w.Line();
        using (_w.Block($"private async {Task} Pump()"))
        {
            _w.Line("lock (_sync) { if (_pumping) return; _pumping = true; }");
            using (_w.Block("try"))
            {
                using (_w.Block("while (true)"))
                {
                    _w.Line(Deciding ? "Step step; Input item; Input owner; Pending pending;" : "Step step; Input item; Input owner;");
                    _w.Line($"lock (_sync) {{ step = NextStep(out item, out owner{(Deciding ? ", out pending" : "")}); if (step == Step.None) {{ _pumping = false; return; }} }}");
                    _w.Line("if (step == Step.Continue) await ContinueStep(item);");
                    if (Deciding)
                    {
                        _w.Line("else if (step == Step.Event) await EventStep(item, owner);");
                        _w.Line("else await ApplyStep(pending);");
                    }
                    else
                    {
                        _w.Line("else await EventStep(item, owner);");
                    }
                }
            }

            _w.Line("catch { lock (_sync) { _pumping = false; } throw; }");
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
                        _w.Line("if (_queued[i].IsEvent && Handles(_pending.Decision, _queued[i].Tag)) { item = _queued[i]; _queued.RemoveAt(i); owner = item.Done == null ? _pending.Owner : item; return Step.Event; }");
                    }

                    using (_w.Block("for (var i = 0; i < _inbox.Count; i++)"))
                    {
                        _w.Line("if (_inbox[i].IsEvent && Handles(_pending.Decision, _inbox[i].Tag)) { item = _inbox[i]; _inbox.RemoveAt(i); owner = item; return Step.Event; }");
                    }

                    _w.Line("return Step.None;");
                }
            }

            _w.Line("if (_queued.Count > 0 && _queued[0].Done != null) { item = _queued[0]; _queued.RemoveAt(0); owner = item; return Step.Event; }");
            _w.Line("if (_current == null && _inbox.Count > 0) { _current = _inbox[0]; _inbox.RemoveAt(0); }");
            _w.Line("if (_current == null) return Step.None;");
            _w.Line("if (_queued.Count > 0) { item = _queued[0]; _queued.RemoveAt(0); owner = item.Done == null ? _current : item; return Step.Event; }");
            _w.Line("item = _current;");
            _w.Line("return Step.Continue;");
        }
    }

    private void WritePumpSteps()
    {
        var enter = Deciding ? "_flow.Value = s_step; _started = null; " : string.Empty;
        _w.Line();
        using (_w.Block($"private async {Task} ContinueStep(Input input)"))
        {
            _w.Line($"{enter}{(Deciding ? "_owner = input; " : "")}_inside = true;");
            using (_w.Block("try"))
            {
                using (_w.Block("if (input.IsEvent)"))
                {
                    _w.Line("if (input.Next == 0) { input.Next = 1; await DispatchInput(input); return; }");
                }

                using (_w.Block("else if (input.Next < input.Values.Length)"))
                {
                    _w.Line("var start = input.Next;");
                    _w.Line(HasRuns ? "var count = RunLength(input.Values.Slice(start));" : "var count = 1;");
                    _w.Line("input.Next += count;");
                    _w.Line("await DispatchAt(input.Values, start, count);");
                    _w.Line("return;");
                }

                _w.Line("lock (_sync) { _current = null; }");
                _w.Line("input.Done.TrySetResult(true);");
            }

            _w.Line($"catch ({Exception} exception) {{ Fail(input, exception); }}");
            _w.Line("finally { _inside = false; }");
        }

        _w.Line();
        using (_w.Block($"private async {Task} EventStep(Input item, Input owner)"))
        {
            _w.Line($"{enter}{(Deciding ? "_owner = owner; " : "")}_inside = true;");
            _w.Line(Deciding
                ? "try { await DispatchInput(item); if (_started == null && item.Done != null) item.Done.TrySetResult(true); }"
                : "try { await DispatchInput(item); if (item.Done != null) item.Done.TrySetResult(true); }");
            _w.Line($"catch ({Exception} exception) {{ Fail(owner, exception); }}");
            _w.Line(Deciding ? "finally { _inside = false; ReleaseEnded(); }" : "finally { _inside = false; }");
        }

        if (Deciding)
        {
            _w.Line();
            using (_w.Block($"private async {Task} ApplyStep(Pending pending)"))
            {
                _w.Line($"{enter}_owner = pending.Owner; _inside = true;");
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
                _w.Line("if (input.Done != null) input.Done.TrySetException(exception);");
                _w.Line("ReleaseEnded();");
            }
            else
            {
                _w.Line("lock (_sync) { if (input == _current) _current = null; }");
                _w.Line("if (input.Done != null) input.Done.TrySetException(exception);");
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

            _w.Line($"foreach (var input in waiting) if (input.Done != null) input.Done.TrySetException(new {Rt}MachineNotRunningException({Rt}MachineStatus.Stopped));");
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
            _w.Line("var finished = new global::System.Collections.Generic.List<Input>();");
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

                _w.Line("foreach (var pending in ended) if (pending.Owner != _current && (_pending == null || _pending.Owner != pending.Owner)) finished.Add(pending.Owner);");
            }

            _w.Line("foreach (var pending in ended) pending.Cancellation.Cancel();");
            _w.Line("foreach (var owner in finished) if (owner != null && owner.Done != null) owner.Done.TrySetResult(true);");
        }
    }
}
```

Read it beside `ReferenceMachine.Inbox.cs`: `NextStep`, `ContinueStep`, `EventStep`, `ApplyStep`, `Fail`,
`EndPending`, `ReleaseEnded` and `Abandon` are the interpreter's, rule for rule, with its `AsyncLocal` as `_flow` and
its step owner as `_owner`. `Deciding` leaves the pending parts out of a `Serialized` machine that has no async
decision.

- [ ] **Step 7: Run the tests to see them pass — repeatedly**

```bash
dotnet test --solution StateAlchemist.slnx
for i in $(seq 1 10); do dotnet test --project tests/StateAlchemist.Generated.Tests/StateAlchemist.Generated.Tests.csproj --no-build | grep -E "failed: [1-9]"; done
```

Expected: PASS — 76 contracts per framework against generated machines, and the loop prints nothing.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Generator: decisions, deferral, runs and serialized machines"
```

---

### Task 3: Hold the new code to C# 7.3, and to the interpreter on random machines

**Files:**
- Modify: `tests/StateAlchemist.Generators.Tests/Generation/GeneratedCodeTests.cs` (four machines added)
- Modify: `tests/StateAlchemist.Generators.Tests/Agreement/RandomMachineSource.cs`, `RandomMachineTests.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces: the no-warning C# 7.3 test over every generator path; random agreement over runs and batches.

- [ ] **Step 1: Compile the new machines as C# 7.3**

`tests/StateAlchemist.Generators.Tests/Generation/GeneratedCodeTests.cs`:

```csharp
extern alias generator;

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Contracts.Machines.Runs;
using StateAlchemist.Reference.Tests.FrontEnd;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Generation;

/// <summary>What the generator writes: code that compiles as C# 7.3, without a single warning.</summary>
public class GeneratedCodeTests
{
    private static GeneratorDriver Driver(LanguageVersion language = LanguageVersion.Latest, bool track = false) => CSharpGeneratorDriver.Create(
        [new MachineGenerator().AsSourceGenerator()],
        parseOptions: new CSharpParseOptions(language),
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, track));

    private static string Problems(Compilation compilation) => string.Join("\n", compilation.GetDiagnostics()
        .Where(d => d.Severity >= DiagnosticSeverity.Warning && !d.Id.StartsWith("SALCH", StringComparison.Ordinal))
        .Select(d => d.ToString()));

    [Test]
    [Arguments("Telnet")]
    [Arguments("Recorder")]
    [Arguments("Guards")]
    [Arguments("FailuresWithHooks")]
    [Arguments("Deciding")]
    [Arguments("DecidingSerialized")]
    [Arguments("RecorderSerialized")]
    [Arguments("Runs")]
    public async Task TheGeneratedCodeIsCSharp73AndCompilesWithoutAWarning(string machine)
    {
        var source = machine switch
        {
            "Telnet" => TestCompilation.Machine("M", typeof(Connected), typeof(byte), typeof(TelnetContext), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule)], body: " { }"),
            "Recorder" => TestCompilation.Machine("M", typeof(Root), typeof(byte), typeof(RecordingContext), [typeof(RecorderModule), typeof(RecorderExtras)], body: " { }"),
            "Guards" => TestCompilation.Machine("M", typeof(GuardRoot), typeof(byte), typeof(RecordingContext), [typeof(GuardModule)], body: " { }"),
            "Deciding" => TestCompilation.Machine("M", typeof(DecideRoot), typeof(byte), typeof(RecordingContext), [typeof(DecidingModule)], body: " { }"),
            "DecidingSerialized" => TestCompilation.Machine("M", typeof(DecideRoot), typeof(byte), typeof(RecordingContext), [typeof(DecidingModule)],
                ", Concurrency = global::StateAlchemist.Concurrency.Serialized", " { }"),
            "RecorderSerialized" => TestCompilation.Machine("M", typeof(Root), typeof(byte), typeof(RecordingContext), [typeof(RecorderModule), typeof(RecorderExtras)],
                ", Concurrency = global::StateAlchemist.Concurrency.Serialized", " { }"),
            "Runs" => TestCompilation.Machine("M", typeof(RunRoot), typeof(byte), typeof(RecordingContext), [typeof(RunModule)], body: " { }"),
            _ => TestCompilation.Machine("M", typeof(FailRoot), typeof(byte), typeof(RecordingContext), [typeof(FailureModule)], body: """
                 {
                     partial void OnGuardException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnTransformException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnExitedException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnEnteredException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnCompletedException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                 }
                 """),
        };
        var compilation = TestCompilation.Create(LanguageVersion.CSharp7_3, source);

        Driver(LanguageVersion.CSharp7_3).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        await Assert.That(output.SyntaxTrees.Count()).IsEqualTo(2);
        await Assert.That(Problems(output)).IsEqualTo("");
    }
}
```

This test is what caught two defects while this plan was validated: a `Serialized` machine without decisions still
carrying the pending slot (CS0649), and an un-awaited decision runner (CS4014) — each an error in an application
that treats warnings as errors.

- [ ] **Step 2: Random machines with runs, fed batches**

`tests/StateAlchemist.Generators.Tests/Agreement/RandomMachineSource.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StateAlchemist.Generators.Tests.Agreement;

/// <summary>
/// Writes a random, usually valid machine as C#: a random tree of states that each carry a value, transitions of
/// every kind between random states — declared on leaves and on ancestors, exact, ranged and or-else, some guarded,
/// some with a <c>Completed</c>, some runs — and <c>[Exited]</c>/<c>[Entered]</c> actions that log. Every transform names only
/// what it may: the target by <c>ref</c>, the root by <c>ref</c>, and a leaf source by <c>in</c>.
/// </summary>
internal static class RandomMachineSource
{
    public const string Namespace = "RandomMachines";

    public static string Generate(int seed)
    {
        var random = new Random(seed);
        var count = random.Next(3, 9);
        var parents = new int[count];
        parents[0] = -1;
        for (var i = 1; i < count; i++)
        {
            parents[i] = random.Next(0, i);
        }

        bool IsLeaf(int state) => !parents.Contains(state);
        var initial = Enumerable.Range(0, count).Where(s => !IsLeaf(s))
            .ToDictionary(s => s, s => Enumerable.Range(0, count).Where(c => parents[c] == s).OrderBy(_ => random.Next()).First());

        var text = new StringBuilder();
        text.AppendLine("using System;");
        text.AppendLine("using System.Collections.Generic;");
        text.AppendLine("using StateAlchemist;");
        text.AppendLine($"namespace {Namespace};");
        text.AppendLine("public sealed class Log { public readonly List<string> Entries = new List<string>(); public void Add(string entry) => Entries.Add(entry); }");
        for (var i = 0; i < count; i++)
        {
            var marker = i == 0 ? "IRootState" : $"IState<S{parents[i]}>";
            var isInitial = i > 0 && initial[parents[i]] == i;
            text.AppendLine($"{(isInitial ? "[Initial] " : "")}public struct S{i} : {marker} {{ public int Value; }}");
        }

        text.AppendLine("[Module] public static class RandomModule {");
        var transitions = random.Next(count, count * 3);
        var order = 1;
        for (var t = 0; t < transitions; t++)
        {
            var from = random.Next(count);
            var stay = random.Next(10) < 3;
            var to = stay ? -1 : random.Next(count);
            var trigger = random.Next(10) switch
            {
                < 6 => $"On({random.Next(6)})",
                < 8 => $"OnRange({random.Next(4)}, {random.Next(4, 7)})",
                _ => "OnAny",
            };
            var guarded = random.Next(4) == 0;
            var completed = random.Next(3) == 0;
            var attribute = $"[Transition(From = typeof(S{from}){(to < 0 ? "" : $", To = typeof(S{to})")}{(guarded ? $", Order = {order++}" : "")}), {trigger}]";
            var k = random.Next(1, 9);

            string parameters;
            string body;
            if (to < 0)
            {
                parameters = $"ref S{from} self";
                body = $"self.Value += {k};";
            }
            else
            {
                var list = new List<string> { $"ref S{to} to" };
                var source = "0";
                if (IsLeaf(from))
                {
                    list.Insert(0, $"in S{from} from");
                    source = "from.Value";
                }

                if (to != 0 && from != 0)
                {
                    list.Add("ref S0 root");
                }

                parameters = string.Join(", ", list);
                body = $"to.Value = {source} * 3 + {k};" + (list.Contains("ref S0 root") ? " root.Value += 1;" : "");
            }

            if (!guarded && !completed)
            {
                text.AppendLine($"  {attribute} public static void T{t}({parameters}) {{ {body} }}");
                continue;
            }

            text.AppendLine($"  {attribute} public static class T{t} {{");
            if (guarded)
            {
                text.AppendLine($"    public static bool Guard(in S{from} from) => from.Value % 3 != 1;");
            }

            text.AppendLine($"    public static void Transform({parameters}) {{ {body} }}");
            if (completed)
            {
                text.AppendLine($"    public static void Completed(Log log) => log.Add(\"completed T{t}\");");
            }

            text.AppendLine("  }");
        }

        // Runs: stays on or-else or a range, taking the whole run (spec §6.7).
        for (var i = 0; i < count; i++)
        {
            if (random.Next(4) == 0)
            {
                var trigger = random.Next(2) == 0 ? "OnAny" : $"OnRange({random.Next(3)}, {random.Next(3, 7)})";
                text.AppendLine($"  [Transition(From = typeof(S{i})), {trigger}, Run] public static void Run{i}(ref S{i} self, ReadOnlySpan<byte> run) {{ self.Value += run.Length * {random.Next(2, 9)} + run[0]; }}");
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (random.Next(3) == 0)
            {
                text.AppendLine($"  [Entered(typeof(S{i}))] public static void Enter{i}(Log log, S{i} state) => log.Add(\"entered S{i} \" + state.Value);");
            }

            if (random.Next(3) == 0)
            {
                text.AppendLine($"  [Exited(typeof(S{i}))] public static void Exit{i}(Log log, S{i} state) => log.Add(\"exited S{i} \" + state.Value);");
            }
        }

        text.AppendLine("}");
        text.AppendLine("[Machine(Root = typeof(S0), Value = typeof(byte), Context = typeof(Log))]");
        text.AppendLine("[Include(typeof(RandomModule))]");
        text.AppendLine("public sealed partial class RandomMachine { }");
        return text.ToString();
    }
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
        var compilation = TestCompilation.Create(source);
        CSharpGeneratorDriver.Create(new MachineGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
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

Runs only form in batches, so each step now fires one to four values at once. The test also requires runs in at
least one checked machine in six, so an unlucky generator cannot pass it without exercising them.

- [ ] **Step 3: Run it, and see it bite**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: PASS — 909 tests on three frameworks: 71 model, 35 runtime, 93 reference, 28 generator and 76 generated
per framework. Then, in `WriteRuns`, replace the `while (count < span.Length …)` line with a comment and run again:
the random test fails within a few batches with diverging values. Put it back.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "The C# 7.3 test and random agreement cover decisions, the inbox and runs"
```

---

## Findings from validating Plan 5

- **Plan 4's refusal did not compile** in a warnings-as-errors application: its constructor threw before assigning
  the context, an unassigned-field warning. Fixed in Plan 4's text; the refusal is gone here anyway.
- **An inbox is only for machines that need one**, and its pending parts only for machines that decide
  asynchronously — otherwise unused fields are warnings. `reference/generated-api.md` now says which machines get an
  inbox.
- **The outcome is read from the union's `Value`**, noted in `concepts/decisions.md`, so generated code needs no
  C# 15 pattern matching.
- **The bounded inbox waits for Plan 6.** Neither the interpreter nor generated code honours `InboxCapacity` yet; it
  arrives with the `Channel` inbox.

## Plan 5 exit gate

- `dotnet test --solution StateAlchemist.slnx` passes on net8.0, net10.0 and net11.0 (909 tests when this plan was
  validated), repeatedly, and CI is green.
- Every contract runs against both implementations. From here, a behaviour change is a contract change first.
- Read a generated deciding machine (`obj/.../generated/`) against `ReferenceMachine.Inbox.cs`: Plan 6 will rewrite
  its hot paths for speed and must not change what it does.

# StateAlchemist Plan 8 — Exported Modules and the Package — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make "add a package, get a protocol" work without giving up the composition root (D25), and make the
package releasable the way the other libraries in this family are released — version from the git tag, symbols and
Source Link, package validation, and a workflow that publishes without a stored key.

**Architecture:** A library offers a module once, in its own assembly: `[assembly: ExportsModule(typeof(M))]`. A
machine takes what its references offer: `[IncludeExported]`, with `Except` for the ones it does not want. Both
ends opt in, and the front-end reads **only the assembly attributes** of the compilation and its references —
never the types inside a reference, which is a scan Roslyn cannot do incrementally. Everything after that is
unchanged: an exported include produces the same model an explicit `[Include]` would, which is a test.

**Tech Stack:** as Plans 4–7, plus MinVer for versioning and the SDK's package validation.

**Spec:** [`docs/superpowers/specs/2026-09-11-statealchemist-design.md`](../specs/2026-09-11-statealchemist-design.md)
— D25, §5.7, §8's `SALCH0108`/`SALCH0109`. The migration this unblocks:
[`2026-09-11-tnc-4.0-migration-design.md`](../specs/2026-09-11-tnc-4.0-migration-design.md). The roadmap:
[`2026-09-11-00-roadmap.md`](2026-09-11-00-roadmap.md).

> **Validated (2026-09-11):** built on Plans 1–7 and run on net8.0, net10.0 and net11.0 — 1,077 tests, all
> passing. The package is packed with its symbols, checked, and consumed by a throwaway application; the AOT
> sample still publishes with no warnings. The code below is that code.

## Global Constraints

Plans 4–7's constraints hold, and two more:

- **Both ends opt in.** A module never joins a machine that did not ask, and a machine never receives a module its
  library did not offer. Anything else would make a package reference change a machine's behaviour silently.
- **Only assembly attributes are read.** Not the types in a reference: Roslyn's own guidance is that scanning
  referenced types for markers is expensive and cannot be done incrementally. The exported set projects to
  metadata names, so the generator pipeline still caches.

## File structure

```
src/StateAlchemist/Declarations/
    ExportsModuleAttribute.cs    new: a library's offer, on the assembly
    IncludeExportedAttribute.cs  new: a machine's acceptance, with Except
src/StateAlchemist.Generators/SymbolModelBuilder.cs   changed: the exported include path
src/StateAlchemist.Reference/Reflection/             changed: the same, by reflection
src/StateAlchemist.Model/Diagnostics/DiagnosticCatalog.cs   changed: SALCH0108, SALCH0109
tests/StateAlchemist.ExportingLibrary/               new: a library that offers a module, as a package would
Directory.Build.props · src/StateAlchemist/StateAlchemist.csproj   changed: MinVer, symbols, validation
.github/workflows/release.yml · docs/releasing.md    new: how a version ships
```

---

### Task 1: A library offers, a machine accepts

**Files:**
- Create: `src/StateAlchemist/Declarations/ExportsModuleAttribute.cs`, `IncludeExportedAttribute.cs`
- Modify: `src/StateAlchemist.Generators/KnownTypes.cs`, `SymbolModelBuilder.cs`
- Modify: `src/StateAlchemist.Model/Diagnostics/DiagnosticCatalog.cs`
- Modify: `src/StateAlchemist.Reference/Reflection/MachineSpec.cs`, `ReflectionModelBuilder.cs`
- Modify: `tests/StateAlchemist.Tests/Api/RuntimeApi.verified.txt`

**Interfaces:**
- Consumes: Plan 4's front-ends and the model's diagnostics.
- Produces: `ExportsModuleAttribute`, `IncludeExportedAttribute`, `ExportedModule`, `MachineSpec.Exported`,
  `DiagnosticCatalog.ExportedTypeIsNotAModule` (`SALCH0108`) and `NothingExported` (`SALCH0109`).

- [ ] **Step 1: The two attributes**

`src/StateAlchemist/Declarations/ExportsModuleAttribute.cs`:

```csharp
using System;

namespace StateAlchemist;

/// <summary>
/// Offers a module to any machine that asks for what its references export: a library says this once, in its own
/// assembly, and an application takes it with <see cref="IncludeExportedAttribute"/>. Both ends opt in, so a
/// library's modules never arrive in a machine that did not ask, and a machine never picks up a module the library
/// meant to keep to itself.
/// </summary>
/// <param name="module">A static class marked <see cref="ModuleAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ExportsModuleAttribute(Type module) : Attribute
{
    /// <summary>The exported module.</summary>
    public Type Module { get; } = module;
}
```

`src/StateAlchemist/Declarations/IncludeExportedAttribute.cs`:

```csharp
using System;

namespace StateAlchemist;

/// <summary>
/// Includes every module that this assembly and its references export with
/// <see cref="ExportsModuleAttribute"/> — so adding a protocol is adding a package reference. Modules named by
/// <see cref="IncludeAttribute"/> are included as well; a module named twice is included once.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IncludeExportedAttribute : Attribute
{
    /// <summary>Exported modules to leave out, for a machine that wants all but a few.</summary>
    public Type[] Except { get; set; } =
#if NET8_0_OR_GREATER
        [];
#else
        Array.Empty<Type>();
#endif
}
```

- [ ] **Step 2: The diagnostics**

`src/StateAlchemist.Model/Diagnostics/DiagnosticCatalog.cs`:

```csharp
using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>
/// Every diagnostic the analysis reports (spec §8). The generator maps these onto Roslyn diagnostics; the reference
/// interpreter throws on errors; <c>docs/reference/diagnostics.md</c> documents each one, and a test keeps the two in step.
/// </summary>
public static class DiagnosticCatalog
{
    private const string Library = "declaring library";
    private const string App = "app";
    private const string Anywhere = "anywhere";

    public static readonly DiagnosticDescriptor InvalidState = new("SALCH0001", Severity.Error, "Invalid state declaration",
        "State '{0}' must be a public struct with exactly one parent marker; {1}", Library);

    public static readonly DiagnosticDescriptor NotPublicStatic = new("SALCH0002", Severity.Error, "Member is not public static",
        "'{0}' must be public and static", Library);

    public static readonly DiagnosticDescriptor InvalidHierarchy = new("SALCH0003", Severity.Error, "Invalid state hierarchy",
        "State '{0}' {1}", App);

    public static readonly DiagnosticDescriptor InitialChild = new("SALCH0004", Severity.Error, "Initial child required",
        "State '{0}' has {1} [Initial] children; it needs exactly one", App);

    public static readonly DiagnosticDescriptor ConflictingTransitions = new("SALCH0101", Severity.Error, "Conflicting transitions",
        "'{0}' and '{1}' both handle {2} in state '{3}' without a guard", App);

    public static readonly DiagnosticDescriptor AmbiguousGuardOrder = new("SALCH0102", Severity.Error, "Ambiguous guard order",
        "Guarded transitions '{0}' and '{1}' both handle {2} in state '{3}' with Order {4}", App);

    public static readonly DiagnosticDescriptor AmbiguousActionOrder = new("SALCH0103", Severity.Error, "Ambiguous action order",
        "'{0}' and '{1}' both run when '{2}' is {3}, from different modules, with Order {4}", App);

    public static readonly DiagnosticDescriptor TriggerOutOfRange = new("SALCH0104", Severity.Error, "Trigger outside the value type",
        "'{0}' fires on {1}, which is outside the value type '{2}'", App);

    public static readonly DiagnosticDescriptor UnsupportedValueType = new("SALCH0105", Severity.Error, "Unsupported value type",
        "The value type '{0}' is not supported: use an integral type or an enum of 16 bits or fewer", App);

    public static readonly DiagnosticDescriptor InvalidTransition = new("SALCH0106", Severity.Error, "Invalid transition declaration",
        "'{0}' {1}", Library);

    public static readonly DiagnosticDescriptor IncompleteMachine = new("SALCH0107", Severity.Error, "Incomplete machine declaration",
        "Machine '{0}' {1}", App);

    public static readonly DiagnosticDescriptor ExportedTypeIsNotAModule = new("SALCH0108", Severity.Error, "Exported type is not a module",
        "'{0}' exports '{1}', which is not a [Module]", App);

    public static readonly DiagnosticDescriptor NothingExported = new("SALCH0109", Severity.Warning, "Nothing exported to include",
        "'{0}' includes exported modules, but nothing this assembly references exports one", App);

    public static readonly DiagnosticDescriptor RefOnExitingState = new("SALCH0201", Severity.Error, "Writing to an exiting state",
        "Parameter '{0}' of '{1}' takes '{2}' by ref, but the transition exits it; take it as in", App);

    public static readonly DiagnosticDescriptor StateNotAvailable = new("SALCH0202", Severity.Error, "State not available here",
        "Parameter '{0}' of '{1}' names '{2}', which {3}", App);

    public static readonly DiagnosticDescriptor InvalidPhaseSignature = new("SALCH0203", Severity.Error, "Invalid phase signature",
        "'{0}' must {1}", App);

    public static readonly DiagnosticDescriptor UnbindableParameter = new("SALCH0204", Severity.Error, "Unbindable parameter",
        "Parameter '{0}' of '{1}' cannot be bound: {2}", App);

    public static readonly DiagnosticDescriptor ContextUnderStrictPurity = new("SALCH0205", Severity.Error, "Context under strict purity",
        "'{0}' takes the context, which machine '{1}' forbids with Purity.Strict", App);

    public static readonly DiagnosticDescriptor UnknownPhase = new("SALCH0206", Severity.Error, "Unknown phase method",
        "'{0}' is not a phase; a class-form transition may declare Guard, Transform, Decide, DecideAsync, Complete, Completed and CompletedAsync", Library);

    public static readonly DiagnosticDescriptor AsyncSuffix = new("SALCH0207", Severity.Error, "Async suffix does not match",
        "'{0}' {1}", Library);

    public static readonly DiagnosticDescriptor TwoDecideMethods = new("SALCH0208", Severity.Error, "Two decide methods",
        "Decision '{0}' declares both Decide and DecideAsync", Library);

    public static readonly DiagnosticDescriptor UncheckedWithAsync = new("SALCH0209", Severity.Warning, "Unchecked machine with async actions",
        "Machine '{0}' is Unchecked but has async actions or decisions; await every FireAsync before calling the next", App);

    public static readonly DiagnosticDescriptor ReentryClearsData = new("SALCH0301", Severity.Warning, "Re-entry clears data",
        "'{0}' re-enters '{1}', which has data; the data is cleared", App);

    public static readonly DiagnosticDescriptor OutcomeCompletions = new("SALCH0401", Severity.Error, "Decision outcome not completed exactly once",
        "Decision '{0}' has {1} Complete methods for outcome '{2}'; it needs exactly one", App);

    public static readonly DiagnosticDescriptor UnknownOutcome = new("SALCH0402", Severity.Error, "Unknown decision outcome",
        "'{0}' completes '{1}', which is not an outcome of decision '{2}'", App);

    public static readonly DiagnosticDescriptor UnhandledValues = new("SALCH0501", Severity.Warning, "Unhandled values",
        "Leaf '{0}' leaves {1} values unhandled and no [OnAny] covers them", App);

    public static readonly DiagnosticDescriptor UnreachableState = new("SALCH0502", Severity.Warning, "Unreachable state",
        "State '{0}' cannot be reached from the initial state", App);

    public static readonly DiagnosticDescriptor SyncFireOnAsyncMachine = new("SALCH0601", Severity.Error, "Synchronous fire on an async machine",
        "'{0}' has async actions or decisions: fire it with FireAsync and await that", App);

    public static readonly DiagnosticDescriptor InvalidRun = new("SALCH0701", Severity.Error, "Invalid run transition",
        "[Run] on '{0}' is invalid: {1}", App);

    public static readonly DiagnosticDescriptor FiredBeforeStarted = new("SALCH0801", Severity.Warning, "Fired before started",
        "'{0}' is fired before StartAsync on some path", Anywhere);

    public static readonly DiagnosticDescriptor NoPhases = new("SALCH0901", Severity.Info, "Transition declares no phases",
        "Transition '{0}' declares no phases", Library);

    public static readonly DiagnosticDescriptor PhaseCanBeAdded = new("SALCH0902", Severity.Hidden, "Phase can be added",
        "Transition '{0}' can declare {1}", Library);

    /// <summary>Every descriptor, in identifier order.</summary>
    public static IReadOnlyList<DiagnosticDescriptor> All { get; } =
    [
        InvalidState, NotPublicStatic, InvalidHierarchy, InitialChild,
        ConflictingTransitions, AmbiguousGuardOrder, AmbiguousActionOrder, TriggerOutOfRange, UnsupportedValueType, InvalidTransition, IncompleteMachine,
        ExportedTypeIsNotAModule, NothingExported,
        RefOnExitingState, StateNotAvailable, InvalidPhaseSignature, UnbindableParameter, ContextUnderStrictPurity, UnknownPhase, AsyncSuffix, TwoDecideMethods, UncheckedWithAsync,
        ReentryClearsData,
        OutcomeCompletions, UnknownOutcome,
        UnhandledValues, UnreachableState,
        SyncFireOnAsyncMachine,
        InvalidRun,
        FiredBeforeStarted,
        NoPhases, PhaseCanBeAdded,
    ];
}
```

Two identifiers rather than one with two severities: an exported type that is not a module is an error, and asking
when nothing is offered is a warning, and a Roslyn descriptor has one severity.

- [ ] **Step 3: The Roslyn front-end**

`src/StateAlchemist.Generators/KnownTypes.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace StateAlchemist.Generators;

/// <summary>The runtime and BCL types the front-end recognises, resolved once per compilation.</summary>
internal sealed class KnownTypes(Compilation compilation)
{
    /// <summary>The compilation these types came from: the exported-module search reads its assemblies.</summary>
    public Compilation Compilation { get; } = compilation;

    public INamedTypeSymbol? Machine { get; } = compilation.GetTypeByMetadataName("StateAlchemist.MachineAttribute");

    public INamedTypeSymbol? Include { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IncludeAttribute");

    public INamedTypeSymbol? Module { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ModuleAttribute");

    /// <summary>An assembly's offer of a module (D25).</summary>
    public INamedTypeSymbol? ExportsModule { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ExportsModuleAttribute");

    /// <summary>A machine's acceptance of what its references export (D25).</summary>
    public INamedTypeSymbol? IncludeExported { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IncludeExportedAttribute");

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

    /// <summary>
    /// Builds the model of one <c>[Module]</c> on its own, for the analyzer that reports a declaring library's
    /// problems where the module is written (spec §8). There is no machine, so there is no root, no value type and
    /// no options: only what a module's own declarations can be wrong about is knowable here, which is exactly the
    /// diagnostics scoped to the declaring library.
    /// </summary>
    public static SymbolMachine BuildModule(INamedTypeSymbol module, Compilation compilation) => new Builder(module, new KnownTypes(compilation)).RunModule();

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

        /// <summary>One module, with no machine around it: its states are the ones its own declarations name.</summary>
        public SymbolMachine RunModule()
        {
            // Every trigger value fits, because no value type is chosen yet: a value out of range is the app's problem.
            _options = new MachineOptions("System.Int64", new ValueDomain(long.MinValue, long.MaxValue));
            return Build([machine], SourceSpan.None);
        }

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
            AddExported(modules, machineLocation);
            return Build(modules, machineLocation);
        }

        private SymbolMachine Build(List<INamedTypeSymbol> modules, SourceSpan machineLocation)
        {
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

        /// <summary>
        /// What this assembly and its references offer with <c>[assembly: ExportsModule]</c>, for a machine that
        /// asked with <c>[IncludeExported]</c> (D25). Only assembly attributes are read — never the types in a
        /// reference, which is a scan the compiler cannot do incrementally.
        /// </summary>
        private void AddExported(List<INamedTypeSymbol> modules, SourceSpan machineLocation)
        {
            if (known.IncludeExported is null || machine.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.IncludeExported)) is not { } asked)
            {
                return;
            }

            var except = asked.NamedArguments.FirstOrDefault(a => a.Key == "Except").Value;
            var excluded = except.Kind == TypedConstantKind.Array
                ? except.Values.Select(v => v.Value).OfType<INamedTypeSymbol>().ToList()
                : [];

            var found = 0;
            var assemblies = new[] { known.Compilation.Assembly }.Concat(known.Compilation.SourceModule.ReferencedAssemblySymbols);
            foreach (var assembly in assemblies)
            {
                foreach (var export in assembly.GetAttributes().Where(a => Is(a.AttributeClass, known.ExportsModule)))
                {
                    if (export.ConstructorArguments.Length == 0 || export.ConstructorArguments[0].Value is not INamedTypeSymbol module)
                    {
                        continue;
                    }

                    found++;
                    if (!module.GetAttributes().Any(a => Is(a.AttributeClass, known.Module)))
                    {
                        _diagnostics.Add(new(DiagnosticCatalog.ExportedTypeIsNotAModule, machineLocation, assembly.Name, DisplayName(module)));
                        continue;
                    }

                    if (excluded.Any(type => Is(type, module)) || modules.Any(included => Is(included, module)))
                    {
                        continue;
                    }

                    modules.Add(module);
                }
            }

            if (found == 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.NothingExported, machineLocation, machine.Name));
            }
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
                .GroupBy(m => m.Name)
                .Select(group => new UnknownMember(group.Key, Span(group.First().Locations.FirstOrDefault())))
                .ToList();

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

                return new ParameterModel(p.Name.Length == 0 ? "_" : p.Name, FriendlyName(p.Type), kind, passing, state, Span(p.Locations.FirstOrDefault()));
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

`AddExported` reads the compilation's own assembly attributes and those of `ReferencedAssemblySymbols`, in that
order, skipping what `Except` names and what an `[Include]` already brought in. Nothing walks the types of a
reference.

- [ ] **Step 4: The reference front-end, the same way**

`src/StateAlchemist.Reference/Reflection/MachineSpec.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StateAlchemist.Reference;

/// <summary>A module an assembly offers with <c>[assembly: ExportsModule]</c> (D25).</summary>
/// <param name="Assembly">The assembly that exported it, for the diagnostic when it is not a module.</param>
/// <param name="Module">The exported type.</param>
public sealed record ExportedModule(string Assembly, Type Module);

/// <summary>What a machine is made of: the same facts a <c>[Machine]</c> declaration states.</summary>
/// <param name="Name">The machine's name.</param>
/// <param name="Root">The root state, or <see langword="null"/> when the declaration omits it.</param>
/// <param name="Value">The value type, or <see langword="null"/> when the declaration omits it.</param>
/// <param name="Modules">The included modules, in order.</param>
/// <param name="Context">The context type, if any.</param>
/// <param name="Config">The configuration type, if any.</param>
/// <param name="Concurrency">The concurrency mode.</param>
/// <param name="InboxCapacity">The serialized inbox's capacity.</param>
/// <param name="Purity">The purity mode.</param>
/// <param name="Unhandled">The unhandled-trigger mode.</param>
/// <param name="Exported">
/// What this assembly and its references export, when the machine asked for it with <c>[IncludeExported]</c>;
/// <see langword="null"/> when it did not ask, and empty when it asked and nothing was offered.
/// </param>
public sealed record MachineSpec(
    string Name,
    Type? Root,
    Type? Value,
    IReadOnlyList<Type> Modules,
    Type? Context = null,
    Type? Config = null,
    Concurrency Concurrency = Concurrency.Checked,
    int InboxCapacity = 0,
    Purity Purity = Purity.Permissive,
    Unhandled Unhandled = Unhandled.Ignore,
    IReadOnlyList<ExportedModule>? Exported = null)
{
    /// <summary>Reads a <c>[Machine]</c> class's attributes.</summary>
    /// <exception cref="ArgumentException">The class has no <c>[Machine]</c> attribute.</exception>
    public static MachineSpec From(Type machineType)
    {
        var machine = machineType.GetCustomAttribute<MachineAttribute>()
            ?? throw new ArgumentException($"'{machineType.Name}' is not marked [Machine].", nameof(machineType));
        return new MachineSpec(
            machineType.Name,
            machine.Root,
            machine.Value,
            machineType.GetCustomAttributes<IncludeAttribute>().Select(i => i.Module).ToList(),
            machine.Context,
            machine.Config,
            machine.Concurrency,
            machine.InboxCapacity,
            machine.Purity,
            machine.Unhandled,
            ExportedFor(machineType));
    }

    /// <summary>
    /// What the machine's own assembly and its references export, or <see langword="null"/> when the machine did
    /// not ask. A reference that is not loadable at runtime cannot have offered anything this application uses.
    /// </summary>
    private static IReadOnlyList<ExportedModule>? ExportedFor(Type machineType)
    {
        if (machineType.GetCustomAttribute<IncludeExportedAttribute>() is not { } asked)
        {
            return null;
        }

        var except = new HashSet<Type>(asked.Except);
        var exported = new List<ExportedModule>();
        foreach (var assembly in Assemblies(machineType.Assembly))
        {
            foreach (var export in assembly.GetCustomAttributes<ExportsModuleAttribute>())
            {
                if (!except.Contains(export.Module))
                {
                    exported.Add(new ExportedModule(assembly.GetName().Name ?? assembly.FullName ?? "?", export.Module));
                }
            }
        }

        return exported;
    }

    private static IEnumerable<Assembly> Assemblies(Assembly machine)
    {
        yield return machine;
        foreach (var reference in machine.GetReferencedAssemblies())
        {
            Assembly? loaded = null;
            try
            {
                loaded = Assembly.Load(reference);
            }
            catch (Exception exception) when (exception is BadImageFormatException or System.IO.FileNotFoundException or System.IO.FileLoadException)
            {
            }

            if (loaded is not null)
            {
                yield return loaded;
            }
        }
    }
}
```

`src/StateAlchemist.Reference/Reflection/ReflectionModelBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>
/// Builds a <see cref="MachineModel"/> from declarations by reflection — the same model the source generator builds
/// from Roslyn symbols, so both are checked by the same analysis.
/// </summary>
public static class ReflectionModelBuilder
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly string[] TransitionPhases = ["Guard", "Transform", "Completed", "CompletedAsync"];
    private static readonly string[] DecisionPhases = ["Guard", "Decide", "DecideAsync", "Complete", "Completed", "CompletedAsync"];

    /// <summary>Builds the model of a <c>[Machine]</c> class.</summary>
    public static ReflectedMachine FromMachine(Type machineType) => Build(MachineSpec.From(machineType));

    /// <summary>Builds the model of <paramref name="spec"/>. Problems become diagnostics; nothing throws for a bad declaration.</summary>
    public static ReflectedMachine Build(MachineSpec spec)
    {
        var builder = new Builder(spec);
        return builder.Run();
    }

    private sealed class Builder(MachineSpec spec)
    {
        private readonly List<ModelDiagnostic> _diagnostics = [];
        private readonly Dictionary<MethodModel, MethodInfo> _methods = new(ReferenceEqualityComparer.Instance);
        private readonly List<Type> _stateTypes = [];
        private MachineOptions _options = new("System.Byte", ValueDomain.Byte);

        /// <summary>The modules this assembly and its references offered, for a machine that asked (D25).</summary>
        private void AddExported(List<Type> modules)
        {
            if (spec.Exported is not { } exported)
            {
                return;
            }

            if (exported.Count == 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.NothingExported, SourceSpan.None, spec.Name));
                return;
            }

            foreach (var export in exported)
            {
                if (export.Module.GetCustomAttribute<ModuleAttribute>() is null)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.ExportedTypeIsNotAModule, SourceSpan.None, export.Assembly, export.Module.Name));
                }
                else if (!modules.Contains(export.Module))
                {
                    modules.Add(export.Module);
                }
            }
        }

        public ReflectedMachine Run()
        {
            if (spec.Root is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, SourceSpan.None, spec.Name, "does not name a Root state"));
            }

            if (spec.Value is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, SourceSpan.None, spec.Name, "does not name a Value type"));
            }

            _options = Options(spec);
            var modules = spec.Modules.Where(IsModule).ToList();
            AddExported(modules);
            var declarations = modules.SelectMany(Declarations).ToList();
            CollectStates(declarations);
            var stateIndex = _stateTypes.Select((type, index) => (type, index)).ToDictionary(p => p.type, p => p.index);
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
                        actions.Add(new StateActionModel(action.Phase, stateIndex[action.State], action.Order, action.Module.FullName!, action.DeclarationIndex,
                            MethodModelOf(action.Method, DisplayName(action.Module), stateIndex, [])));
                        break;
                }
            }

            var model = new MachineModel(spec.Name, _options, states, transitions, actions, _diagnostics);
            return new ReflectedMachine(spec, model, _stateTypes, _methods);
        }

        private bool IsModule(Type module)
        {
            if (module.GetCustomAttribute<ModuleAttribute>() is not null)
            {
                return true;
            }

            _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, SourceSpan.None, spec.Name, $"includes '{module.Name}', which is not a [Module]"));
            return false;
        }

        private static IEnumerable<object> Declarations(Type module)
        {
            var index = 0;
            foreach (var member in module.GetMembers(Declared).OrderBy(m => m.MetadataToken))
            {
                switch (member)
                {
                    case MethodInfo method when method.GetCustomAttribute<TransitionAttribute>() is { } attribute:
                        yield return new TransitionDeclaration(module, method, null, attribute.From, attribute.To, attribute.Order, false, []);
                        break;
                    case MethodInfo method:
                        foreach (var exited in method.GetCustomAttributes<ExitedAttribute>())
                        {
                            yield return new ActionDeclaration(module, method, ActionPhase.Exited, exited.State, exited.Order, index++);
                        }

                        foreach (var entered in method.GetCustomAttributes<EnteredAttribute>())
                        {
                            yield return new ActionDeclaration(module, method, ActionPhase.Entered, entered.State, entered.Order, index++);
                        }

                        break;
                    case Type nested when nested.GetCustomAttribute<TransitionAttribute>() is { } attribute:
                        yield return new TransitionDeclaration(module, null, nested, attribute.From, attribute.To, attribute.Order, false, []);
                        break;
                    case Type nested when nested.GetCustomAttribute<DecisionAttribute>() is { } attribute:
                        yield return new TransitionDeclaration(module, null, nested, attribute.From, null, attribute.Order, true, attribute.Handle);
                        break;
                }
            }
        }

        private void CollectStates(IReadOnlyList<object> declarations)
        {
            var pending = new List<Type>();
            if (spec.Root is not null)
            {
                pending.Add(spec.Root);
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
                            pending.AddRange(t.Class.GetMethods(Declared).Select(m => m.GetCustomAttribute<ToAttribute>()?.Target).OfType<Type>());
                        }

                        break;
                    case ActionDeclaration a:
                        pending.Add(a.State);
                        break;
                }
            }

            var found = new HashSet<Type>();
            while (pending.Count > 0)
            {
                var type = pending[^1];
                pending.RemoveAt(pending.Count - 1);
                if (!found.Add(type))
                {
                    continue;
                }

                if (ParentOf(type) is { } parent && type != spec.Root)
                {
                    pending.Add(parent);
                }
            }

            var others = found.Where(t => t != spec.Root).OrderBy(t => t.FullName, StringComparer.Ordinal);
            _stateTypes.AddRange(spec.Root is null ? others : [spec.Root, .. others]);
        }

        private static Type? ParentOf(Type state) =>
            state.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IState<>))?.GetGenericArguments()[0];

        private StateModel StateModelOf(Type type, int index, IReadOnlyDictionary<Type, int> stateIndex)
        {
            var markers = type.GetInterfaces().Count(i => i == typeof(IRootState) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IState<>)));
            var parent = type == spec.Root || ParentOf(type) is not { } parentType ? -1 : stateIndex[parentType];
            if (type == spec.Root && ParentOf(type) is not null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, SourceSpan.None, type.Name, "is the machine's root but declares a parent"));
            }

            var reset = type.GetMethod("Reset", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return new StateModel(
                index,
                type.FullName!,
                parent,
                IsInitial: type.GetCustomAttribute<InitialAttribute>() is not null,
                HasData: type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0,
                HasReset: reset is not null && reset.ReturnType == typeof(void),
                IsPublicStruct: type.IsValueType && !type.IsEnum && IsVisible(type),
                ParentMarkers: markers,
                SourceSpan.None);
        }

        private IEnumerable<TransitionModel> TransitionModels(TransitionDeclaration declaration, IReadOnlyDictionary<Type, int> stateIndex)
        {
            var member = (MemberInfo?)declaration.Method ?? declaration.Class!;
            var name = DisplayName(declaration.Module) + "." + member.Name;
            if (declaration.From is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "does not name its From state"));
                yield break;
            }

            var triggers = Triggers(member, name);
            if (triggers.Count == 0)
            {
                yield break;
            }

            var declaringType = declaration.Class is null ? DisplayName(declaration.Module) : DisplayName(declaration.Class);
            var outcomes = declaration.IsDecision ? OutcomesOf(declaration.Class!) : [];
            MethodModel? Phase(string phase) => declaration.Class?.GetMethods(Declared).FirstOrDefault(m => m.Name == phase) is { } method
                ? MethodModelOf(method, declaringType, stateIndex, outcomes)
                : null;
            IReadOnlyList<MethodModel> Phases(params string[] phases) => declaration.Class?.GetMethods(Declared)
                .Where(m => phases.Contains(m.Name)).OrderBy(m => m.MetadataToken)
                .Select(m => MethodModelOf(m, declaringType, stateIndex, outcomes)).ToList() ?? [];

            var transform = declaration.Method is not null ? MethodModelOf(declaration.Method, declaringType, stateIndex, outcomes) : Phase("Transform");
            var known = declaration.IsDecision ? DecisionPhases : TransitionPhases;
            var unknown = declaration.Class?.GetMethods(Declared)
                .Where(m => !m.IsSpecialName && m.IsPublic && !known.Contains(m.Name) && m.DeclaringType == declaration.Class)
                .Select(m => m.Name).Distinct().Select(name => new UnknownMember(name)).ToList() ?? [];

            DecisionModel? decision = null;
            if (declaration.IsDecision)
            {
                var completions = declaration.Class!.GetMethods(Declared).Where(m => m.Name == "Complete").OrderBy(m => m.MetadataToken).Select(m =>
                {
                    var target = m.GetCustomAttribute<ToAttribute>()?.Target;
                    var complete = MethodModelOf(m, declaringType, stateIndex, outcomes);
                    if (target is null)
                    {
                        _diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, SourceSpan.None, complete.FullName, "declare its target with [To]"));
                    }

                    var outcome = m.GetParameters().Select(p => p.ParameterType).FirstOrDefault(t => outcomes.Contains(t.FullName!))?.FullName ?? string.Empty;
                    return new OutcomeCompletion(outcome, target is null ? -1 : stateIndex[target], complete);
                }).Where(c => c.Target >= 0).ToList();
                decision = new DecisionModel(Phase("Decide"), Phase("DecideAsync"), outcomes, completions, declaration.Handle.Select(h => h.FullName!).ToList());
            }

            var source = stateIndex[declaration.From];
            var target = declaration.To is null ? -1 : stateIndex[declaration.To];
            var isRun = member.GetCustomAttribute<RunAttribute>() is not null;
            foreach (var trigger in triggers)
            {
                yield return new TransitionModel(0, name, source, target, trigger, declaration.Order, isRun, Phase("Guard"), transform,
                    Phases("Completed", "CompletedAsync"), decision, unknown, declaration.Module.FullName!, SourceSpan.None);
            }
        }

        private List<TriggerModel> Triggers(MemberInfo member, string name)
        {
            var triggers = new List<TriggerModel>();
            foreach (var on in member.GetCustomAttributes<OnAttribute>())
            {
                if (ToInt64(on.Value) is { } value)
                {
                    triggers.Add(TriggerModel.Value(value));
                }
                else
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, $"fires on '{on.Value}', which is not an integral constant"));
                }
            }

            foreach (var range in member.GetCustomAttributes<OnRangeAttribute>())
            {
                if (ToInt64(range.From) is not { } low || ToInt64(range.To) is not { } high)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "has a range whose ends are not integral constants"));
                }
                else if (high < low)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, $"has an empty range {low}..{high}"));
                }
                else
                {
                    triggers.Add(TriggerModel.Range(low, high));
                }
            }

            if (member.GetCustomAttribute<OnAnyAttribute>() is not null)
            {
                triggers.Add(TriggerModel.Any);
            }

            var onEvent = member.GetCustomAttribute<OnEventAttribute>();
            if (onEvent is not null && triggers.Count > 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "mixes value and event triggers"));
                return [];
            }

            if (onEvent is not null)
            {
                triggers.Add(TriggerModel.Event(onEvent.EventType.FullName!));
            }

            if (triggers.Count == 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "has no trigger"));
            }

            return triggers;
        }

        private MethodModel MethodModelOf(MethodInfo method, string declaringType, IReadOnlyDictionary<Type, int> stateIndex, IReadOnlyList<string> outcomes)
        {
            var parameters = method.GetParameters().Select(p =>
            {
                var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
                var passing = !p.ParameterType.IsByRef ? Passing.Value : p.IsIn ? Passing.In : p.IsOut ? Passing.Out : Passing.Ref;
                var kind = ParameterClassifier.Classify(Facts(type), _options, n => IndexOf(n, stateIndex), outcomes, out var state);
                return new ParameterModel(p.Name ?? "_", FriendlyName(type), kind, passing, state);
            }).ToList();

            var model = new MethodModel(declaringType, method.Name, ReturnShapeOf(method.ReturnType), method.IsStatic, method.IsPublic && IsVisible(method.DeclaringType!), parameters, SourceSpan.None);
            _methods[model] = method;
            return model;
        }

        private static int IndexOf(string fullName, IReadOnlyDictionary<Type, int> stateIndex)
        {
            foreach (var pair in stateIndex)
            {
                if (pair.Key.FullName == fullName)
                {
                    return pair.Value;
                }
            }

            return -1;
        }

        private static TypeFacts Facts(Type type)
        {
            string? ArgumentOf(Type definition) =>
                type.IsGenericType && type.GetGenericTypeDefinition() == definition ? type.GetGenericArguments()[0].FullName : null;

            return new TypeFacts(
                type.FullName ?? type.Name,
                IsEvent: typeof(IEvent).IsAssignableFrom(type),
                SpanOf: ArgumentOf(typeof(ReadOnlySpan<>)),
                MemoryOf: ArgumentOf(typeof(ReadOnlyMemory<>)),
                IsCancellationToken: type == typeof(CancellationToken),
                TransitionInfoOf: ArgumentOf(typeof(TransitionInfo<>)));
        }

        private static IReadOnlyList<string> OutcomesOf(Type decision)
        {
            var decide = decision.GetMethods(Declared).FirstOrDefault(m => m.Name is "Decide" or "DecideAsync");
            var union = decide?.ReturnType;
            if (union is { IsGenericType: true } && (union.GetGenericTypeDefinition() == typeof(ValueTask<>) || union.GetGenericTypeDefinition() == typeof(Task<>)))
            {
                union = union.GetGenericArguments()[0];
            }

            if (union is null || !union.GetCustomAttributes().Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.UnionAttribute"))
            {
                return [];
            }

            return union.GetConstructors().Select(c => c.GetParameters()).Where(p => p.Length == 1).Select(p => p[0].ParameterType.FullName!).ToList();
        }

        private static MachineOptions Options(MachineSpec spec)
        {
            var value = spec.Value ?? typeof(byte);
            var underlying = value.IsEnum ? Enum.GetUnderlyingType(value) : value;
            var domain = underlying switch
            {
                _ when underlying == typeof(byte) => new ValueDomain(byte.MinValue, byte.MaxValue),
                _ when underlying == typeof(sbyte) => new ValueDomain(sbyte.MinValue, sbyte.MaxValue),
                _ when underlying == typeof(short) => new ValueDomain(short.MinValue, short.MaxValue),
                _ when underlying == typeof(ushort) => new ValueDomain(ushort.MinValue, ushort.MaxValue),
                _ when underlying == typeof(char) => new ValueDomain(char.MinValue, char.MaxValue),
                _ => null,
            };

            if (domain is not null && value.IsEnum)
            {
                domain = domain with { Members = Enum.GetValues(value).Cast<object>().Select(v => Convert.ToInt64(v)).Distinct().OrderBy(v => v).ToList() };
            }

            return new MachineOptions(
                value.FullName!,
                domain ?? new ValueDomain(0, 0),
                ValueTypeSupported: domain is not null,
                spec.Context?.FullName,
                spec.Config?.FullName,
                (ConcurrencyMode)spec.Concurrency,
                spec.InboxCapacity,
                (PurityMode)spec.Purity,
                (UnhandledMode)spec.Unhandled);
        }

        private static long? ToInt64(object value) => value switch
        {
            Enum or byte or sbyte or short or ushort or int or uint or long or char => Convert.ToInt64(value),
            _ => null,
        };

        private static ReturnShape ReturnShapeOf(Type type) => type switch
        {
            _ when type == typeof(void) => ReturnShape.Void,
            _ when type == typeof(bool) => ReturnShape.Bool,
            _ when type == typeof(ValueTask) => ReturnShape.ValueTask,
            _ when type == typeof(Task) => ReturnShape.Task,
            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(ValueTask<>) => ReturnShape.ValueTaskOfResult,
            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(Task<>) => ReturnShape.TaskOfResult,
            _ => ReturnShape.Other,
        };

        private static bool IsVisible(Type type) => type.IsPublic || (type.IsNestedPublic && IsVisible(type.DeclaringType!));

        private static string DisplayName(Type type) => type.DeclaringType is null ? type.Name : DisplayName(type.DeclaringType) + "." + type.Name;

        private static string FriendlyName(Type type) => type.IsGenericType
            ? $"{type.GetGenericTypeDefinition().FullName!.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>"
            : type.FullName ?? type.Name;
    }

    private sealed record TransitionDeclaration(Type Module, MethodInfo? Method, Type? Class, Type? From, Type? To, int Order, bool IsDecision, Type[] Handle);

    private sealed record ActionDeclaration(Type Module, MethodInfo Method, ActionPhase Phase, Type State, int Order, int DeclarationIndex);
}
```

Reflection reads the machine's own assembly and the assemblies it references, loading each by name and ignoring
one that cannot be loaded: a reference that is not there at runtime cannot have offered anything this application
uses. The two front-ends therefore agree wherever it matters — a module that is included is a module the generated
code calls, so the emitted assembly records that reference.

- [ ] **Step 5: The public surface changed**

`tests/StateAlchemist.Tests/Api/RuntimeApi.verified.txt`:

```text
﻿namespace StateAlchemist
{
    public enum Concurrency
    {
        Checked = 0,
        Unchecked = 1,
        Serialized = 2,
    }
    public sealed class ConcurrentUseException : System.InvalidOperationException
    {
        public ConcurrentUseException() { }
    }
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple=false, Inherited=false)]
    public sealed class DecisionAttribute : System.Attribute
    {
        public DecisionAttribute() { }
        public System.Type? From { get; set; }
        public System.Type[] Handle { get; set; }
        public int Order { get; set; }
    }
    public readonly struct DecisionFailed : StateAlchemist.IEvent
    {
        public DecisionFailed(string decision, System.Exception exception) { }
        public string Decision { get; }
        public System.Exception Exception { get; }
        public override string ToString() { }
    }
    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple=true, Inherited=false)]
    public sealed class EnteredAttribute : System.Attribute
    {
        public EnteredAttribute(System.Type state) { }
        public int Order { get; set; }
        public System.Type State { get; }
    }
    public enum ExceptionResolution
    {
        Rethrow = 0,
        Skip = 1,
        Continue = 2,
    }
    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple=true, Inherited=false)]
    public sealed class ExitedAttribute : System.Attribute
    {
        public ExitedAttribute(System.Type state) { }
        public int Order { get; set; }
        public System.Type State { get; }
    }
    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple=true, Inherited=false)]
    public sealed class ExportsModuleAttribute : System.Attribute
    {
        public ExportsModuleAttribute(System.Type module) { }
        public System.Type Module { get; }
    }
    public interface IEvent { }
    public interface IMachine<TValue> : System.IAsyncDisposable
        where TValue :  struct
    {
        StateAlchemist.MachineDefinition Definition { get; }
        System.Type StateType { get; }
        StateAlchemist.MachineStatus Status { get; }
        void Enqueue<TEvent>(TEvent e)
            where TEvent :  struct, StateAlchemist.IEvent;
        System.Threading.Tasks.ValueTask FireAsync(System.ReadOnlyMemory<TValue> values);
        System.Threading.Tasks.ValueTask FireAsync(TValue value);
        System.Threading.Tasks.ValueTask FireAsync<TEvent>(TEvent e)
            where TEvent :  struct, StateAlchemist.IEvent;
        bool IsIn<TState>()
            where TState :  struct;
        StateAlchemist.TransitionPlan Plan(TValue value);
        StateAlchemist.TransitionPlan Plan<TEvent>(TEvent e)
            where TEvent :  struct, StateAlchemist.IEvent;
        System.Threading.Tasks.ValueTask StartAsync();
        System.Threading.Tasks.ValueTask StopAsync();
        bool TryGetState<TState>(out TState value)
            where TState :  struct;
    }
    public interface IRootState { }
    public interface IState<TParent>
        where TParent :  struct { }
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple=true, Inherited=false)]
    public sealed class IncludeAttribute : System.Attribute
    {
        public IncludeAttribute(System.Type module) { }
        public System.Type Module { get; }
    }
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple=false, Inherited=false)]
    public sealed class IncludeExportedAttribute : System.Attribute
    {
        public IncludeExportedAttribute() { }
        public System.Type[] Except { get; set; }
    }
    [System.AttributeUsage(System.AttributeTargets.Struct, AllowMultiple=false, Inherited=false)]
    public sealed class InitialAttribute : System.Attribute
    {
        public InitialAttribute() { }
    }
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple=false, Inherited=false)]
    public sealed class MachineAttribute : System.Attribute
    {
        public MachineAttribute() { }
        public StateAlchemist.Concurrency Concurrency { get; set; }
        public System.Type? Config { get; set; }
        public System.Type? Context { get; set; }
        public int InboxCapacity { get; set; }
        public StateAlchemist.Purity Purity { get; set; }
        public System.Type? Root { get; set; }
        public StateAlchemist.Unhandled Unhandled { get; set; }
        public System.Type? Value { get; set; }
    }
    public sealed class MachineDefinition
    {
        public MachineDefinition(System.Type valueType, System.Collections.Generic.IReadOnlyList<StateAlchemist.StateDefinition> states, System.Collections.Generic.IReadOnlyList<StateAlchemist.TransitionDefinition> transitions) { }
        public StateAlchemist.StateDefinition Root { get; }
        public System.Collections.Generic.IReadOnlyList<StateAlchemist.StateDefinition> States { get; }
        public System.Collections.Generic.IReadOnlyList<StateAlchemist.TransitionDefinition> Transitions { get; }
        public System.Type ValueType { get; }
        public System.Collections.Generic.IReadOnlyList<StateAlchemist.StateDefinition> ChildrenOf(StateAlchemist.StateDefinition state) { }
        public int IndexOf(System.Type stateType) { }
    }
    public sealed class MachineNotRunningException : System.InvalidOperationException
    {
        public MachineNotRunningException(StateAlchemist.MachineStatus status) { }
        public StateAlchemist.MachineStatus Status { get; }
    }
    public enum MachineStatus
    {
        NotStarted = 0,
        Running = 1,
        Stopped = 2,
    }
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple=false, Inherited=false)]
    public sealed class ModuleAttribute : System.Attribute
    {
        public ModuleAttribute() { }
    }
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple=false, Inherited=false)]
    public sealed class OnAnyAttribute : System.Attribute
    {
        public OnAnyAttribute() { }
    }
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple=true, Inherited=false)]
    public sealed class OnAttribute : System.Attribute
    {
        public OnAttribute(object value) { }
        public object Value { get; }
    }
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple=false, Inherited=false)]
    public sealed class OnEventAttribute : System.Attribute
    {
        public OnEventAttribute(System.Type eventType) { }
        public System.Type EventType { get; }
    }
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple=true, Inherited=false)]
    public sealed class OnRangeAttribute : System.Attribute
    {
        public OnRangeAttribute(object from, object to) { }
        public object From { get; }
        public object To { get; }
    }
    public enum Phase
    {
        Guard = 0,
        Transform = 1,
        Decide = 2,
        Complete = 3,
        Exited = 4,
        Entered = 5,
        Completed = 6,
    }
    public static class PhaseNames
    {
        public const string Complete = "Complete";
        public const string Completed = "Completed";
        public const string CompletedAsync = "CompletedAsync";
        public const string Decide = "Decide";
        public const string DecideAsync = "DecideAsync";
        public const string Guard = "Guard";
        public const string Transform = "Transform";
        public static System.Collections.Generic.IReadOnlyList<string> All { get; }
    }
    public enum Purity
    {
        Permissive = 0,
        Strict = 1,
    }
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple=false, Inherited=false)]
    public sealed class RunAttribute : System.Attribute
    {
        public RunAttribute() { }
    }
    public sealed class StateDefinition
    {
        public StateDefinition(int index, System.Type type, int parent, bool isInitial) { }
        public int Index { get; }
        public bool IsInitial { get; }
        public bool IsRoot { get; }
        public string Name { get; }
        public int Parent { get; }
        public System.Type Type { get; }
    }
    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple=false, Inherited=false)]
    public sealed class ToAttribute : System.Attribute
    {
        public ToAttribute(System.Type target) { }
        public System.Type Target { get; }
    }
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple=false, Inherited=false)]
    public sealed class TransitionAttribute : System.Attribute
    {
        public TransitionAttribute() { }
        public System.Type? From { get; set; }
        public int Order { get; set; }
        public System.Type? To { get; set; }
    }
    public sealed class TransitionDefinition
    {
        public TransitionDefinition(int index, string name, int source, int target, StateAlchemist.TransitionKind kind, StateAlchemist.TriggerDefinition trigger, int order, bool hasGuard, bool isRun, bool usesContext, bool isDecision) { }
        public bool HasGuard { get; }
        public int Index { get; }
        public bool IsDecision { get; }
        public bool IsRun { get; }
        public StateAlchemist.TransitionKind Kind { get; }
        public string Name { get; }
        public int Order { get; }
        public int Source { get; }
        public int Target { get; }
        public StateAlchemist.TriggerDefinition Trigger { get; }
        public bool UsesContext { get; }
    }
    public readonly struct TransitionInfo<TValue>
        where TValue :  struct
    {
        public TransitionInfo(string transition, System.Type source, System.Type leaf, System.Type target, StateAlchemist.TransitionKind kind, StateAlchemist.Phase phase, TValue value, bool hasValue, System.Type? eventType, System.Type? state) { }
        public System.Type? EventType { get; }
        public bool HasValue { get; }
        public StateAlchemist.TransitionKind Kind { get; }
        public System.Type Leaf { get; }
        public StateAlchemist.Phase Phase { get; }
        public System.Type Source { get; }
        public System.Type? State { get; }
        public System.Type Target { get; }
        public string Transition { get; }
        public TValue Value { get; }
        public override string ToString() { }
        public StateAlchemist.TransitionInfo<TValue> With(StateAlchemist.Phase phase, System.Type? state = null) { }
    }
    public enum TransitionKind
    {
        Stay = 0,
        Move = 1,
        Reenter = 2,
    }
    public sealed class TransitionPlan
    {
        public TransitionPlan(string transition, System.Type leaf, System.Type target, StateAlchemist.TransitionKind kind, System.Collections.Generic.IReadOnlyList<System.Type> exiting, System.Collections.Generic.IReadOnlyList<System.Type> entering, bool isDecision) { }
        public System.Collections.Generic.IReadOnlyList<System.Type> Entering { get; }
        public System.Collections.Generic.IReadOnlyList<System.Type> Exiting { get; }
        public bool Handled { get; }
        public bool IsDecision { get; }
        public StateAlchemist.TransitionKind Kind { get; }
        public System.Type? Leaf { get; }
        public System.Type? Target { get; }
        public string? Transition { get; }
        public static StateAlchemist.TransitionPlan None { get; }
    }
    public readonly struct TriggerDefinition : System.IEquatable<StateAlchemist.TriggerDefinition>
    {
        public System.Type? EventType { get; }
        public long High { get; }
        public StateAlchemist.TriggerKind Kind { get; }
        public long Low { get; }
        public bool Equals(StateAlchemist.TriggerDefinition other) { }
        public override bool Equals(object? obj) { }
        public override int GetHashCode() { }
        public bool Matches(long value) { }
        public override string ToString() { }
        public static StateAlchemist.TriggerDefinition ForAny() { }
        public static StateAlchemist.TriggerDefinition ForEvent(System.Type eventType) { }
        public static StateAlchemist.TriggerDefinition ForRange(long low, long high) { }
        public static StateAlchemist.TriggerDefinition ForValue(long value) { }
        public static bool operator !=(StateAlchemist.TriggerDefinition left, StateAlchemist.TriggerDefinition right) { }
        public static bool operator ==(StateAlchemist.TriggerDefinition left, StateAlchemist.TriggerDefinition right) { }
    }
    public enum TriggerKind
    {
        Value = 0,
        Range = 1,
        Any = 2,
        Event = 3,
    }
    public enum Unhandled
    {
        Ignore = 0,
        Throw = 1,
    }
    public sealed class UnhandledTriggerException : System.InvalidOperationException
    {
        public UnhandledTriggerException(System.Type state, string trigger) { }
        public System.Type State { get; }
        public string Trigger { get; }
    }
}
```

Accept the snapshot by renaming the `.received.txt` the test writes, as in Plan 1; do not paste it.

- [ ] **Step 6: Run the tests**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: PASS — nothing else changes behaviour, and the API snapshot now has the two attributes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "A library offers modules; a machine takes what its references offer"
```

---

### Task 2: Prove it across an assembly boundary

**Files:**
- Create: `tests/StateAlchemist.ExportingLibrary/StateAlchemist.ExportingLibrary.csproj`, `AssemblyInfo.cs`,
  `ExportedPing.cs`
- Create: `tests/StateAlchemist.Generators.Tests/Generation/ExportedModuleTests.cs`
- Create: `tests/StateAlchemist.Generated.Tests/ExportedModuleMachine.cs`, `ExportedModuleTests.cs`
- Modify: `tests/StateAlchemist.Generated.Tests/StateAlchemist.Generated.Tests.csproj`, `StateAlchemist.slnx`

**Interfaces:**
- Consumes: Task 1.
- Produces: `PingRoot`, `PingIdle`, `ExportedPingModule` in a library that exports; `ExportedMachine`, a machine
  that names no module at all.

- [ ] **Step 1: A library that offers a module, as a package would**

`tests/StateAlchemist.ExportingLibrary/StateAlchemist.ExportingLibrary.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- A library that offers a module the way a protocol package would: the only thing under test here is that
         [assembly: ExportsModule] is read from a real reference, not from the compilation being generated. -->
    <TargetFrameworks>net8.0;net10.0;net11.0</TargetFrameworks>
    <RootNamespace>StateAlchemist.ExportingLibrary</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\StateAlchemist\StateAlchemist.csproj" />
  </ItemGroup>
</Project>
```

`tests/StateAlchemist.ExportingLibrary/AssemblyInfo.cs`:

```csharp
// What a protocol package says about itself, in the one place C# allows it: an assembly attribute must precede
// every type in its file, so a library's offer lives here rather than beside the module.
[assembly: StateAlchemist.ExportsModule(typeof(StateAlchemist.ExportingLibrary.ExportedPingModule))]
```

`tests/StateAlchemist.ExportingLibrary/ExportedPing.cs`:

```csharp
using System;

namespace StateAlchemist.ExportingLibrary;

// What a protocol package looks like from the outside: states and a module. The one line that puts the module on
// offer is in AssemblyInfo.cs, where an assembly attribute has to be.

/// <summary>The root of the shape this library extends.</summary>
public struct PingRoot : IRootState
{
    /// <summary>How many pings have been answered.</summary>
    public int Pings;
}

/// <summary>Where the machine rests.</summary>
[Initial]
public struct PingIdle : IState<PingRoot>
{
}

/// <summary>A module offered to any machine that takes what its references export.</summary>
[Module]
public static class ExportedPingModule
{
    /// <summary>9: answer a ping without leaving the state.</summary>
    [Transition(From = typeof(PingIdle)), On(9)]
    public static void Ping(ref PingRoot root) => root.Pings++;
}
```

The offer is in `AssemblyInfo.cs` because C# requires an assembly attribute to precede every type in its file —
which is also why a library puts it there rather than beside the module.

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
    <Project Path="src/StateAlchemist.CodeFixes/StateAlchemist.CodeFixes.csproj" />
    <Project Path="src/StateAlchemist.Generators/StateAlchemist.Generators.csproj" />
    <Project Path="src/StateAlchemist.Model/StateAlchemist.Model.csproj" />
    <Project Path="src/StateAlchemist.Reference/StateAlchemist.Reference.csproj" />
    <Project Path="src/StateAlchemist/StateAlchemist.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/StateAlchemist.Contracts/StateAlchemist.Contracts.csproj" />
    <Project Path="tests/StateAlchemist.ExportingLibrary/StateAlchemist.ExportingLibrary.csproj" />
    <Project Path="tests/StateAlchemist.Generated.Tests/StateAlchemist.Generated.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Generators.Tests/StateAlchemist.Generators.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Model.Tests/StateAlchemist.Model.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Reference.Tests/StateAlchemist.Reference.Tests.csproj" />
    <Project Path="tests/StateAlchemist.Tests/StateAlchemist.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 2: A machine that names no module**

`tests/StateAlchemist.Generated.Tests/StateAlchemist.Generated.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Generated machines, one per contract shape, running the same contract suite as the reference interpreter.
         The declarations live in other assemblies (the contracts, the samples): the whole-program path is the only
         path tested. -->
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net8.0;net10.0;net11.0</TargetFrameworks>
    <RootNamespace>StateAlchemist.Generated.Tests</RootNamespace>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <!-- The contract machines exercise what these warn about on purpose: a re-entry that clears data, values left
         unhandled, an Unchecked machine with async actions. -->
    <NoWarn>$(NoWarn);SALCH0209;SALCH0301;SALCH0501</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" />
    <!-- To parse the documentation's code blocks, nothing more. -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="5.9.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\StateAlchemist.ExportingLibrary\StateAlchemist.ExportingLibrary.csproj" />
    <ProjectReference Include="..\..\src\StateAlchemist\StateAlchemist.csproj" />
    <ProjectReference Include="..\..\src\StateAlchemist.Generators\StateAlchemist.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\StateAlchemist.Contracts\StateAlchemist.Contracts.csproj" />
    <ProjectReference Include="..\..\samples\StateAlchemist.Samples\StateAlchemist.Samples.csproj" />
  </ItemGroup>
</Project>
```

`tests/StateAlchemist.Generated.Tests/ExportedModuleMachine.cs`:

```csharp
using StateAlchemist.ExportingLibrary;

namespace StateAlchemist.Generated.Tests;

// The case [IncludeExported] exists for: this machine names no module at all. Its transitions come from a
// referenced library that offers one, which is what "add a package, get a protocol" has to mean.
[Machine(Root = typeof(PingRoot), Value = typeof(byte))]
[IncludeExported]
public sealed partial class ExportedMachine;
```

`tests/StateAlchemist.Generated.Tests/ExportedModuleTests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.ExportingLibrary;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// D25 end to end: a machine that declares nothing but its root gets its transitions from a referenced library
/// that exports a module, and the generated machine runs them.
/// </summary>
public class ExportedModuleTests
{
    [Test]
    public async Task AMachineBuiltFromAnExportedModuleRuns()
    {
        var machine = new ExportedMachine();
        await machine.StartAsync();
        await machine.FireAsync((byte)9);
        await machine.FireAsync((byte)9);

        machine.TryGetPingRoot(out var root);
        await Assert.That(root.Pings).IsEqualTo(2);
        await Assert.That(machine.IsIn<PingIdle>()).IsTrue();
    }

    [Test]
    public async Task ItsDefinitionNamesTheExportedTransition()
    {
        await Assert.That(ExportedMachine.Definition.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "ExportedPingModule.Ping" });
    }
}
```

- [ ] **Step 3: The front-ends, in source**

`tests/StateAlchemist.Generators.Tests/Generation/ExportedModuleTests.cs`:

```csharp
extern alias generator;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;
using Generated = generator::StateAlchemist.Model;
using SymbolModelBuilder = generator::StateAlchemist.Generators.SymbolModelBuilder;

namespace StateAlchemist.Generators.Tests.Generation;

/// <summary>
/// D25: a library offers its modules with <c>[assembly: ExportsModule]</c>, and a machine takes what its
/// references offer with <c>[IncludeExported]</c> — "add a package, get a protocol", without a machine ever
/// receiving a module that did not offer itself.
/// </summary>
public class ExportedModuleTests
{
    /// <summary>
    /// The declarations every test shares, with <paramref name="exports"/> where C# requires assembly attributes
    /// to be — before any type — and <paramref name="machine"/> naming the modules however the test wants.
    /// </summary>
    private static string Source(string exports, string machine) => $$"""
        using System;
        using StateAlchemist;
        {{exports}}
        public struct Root : IRootState { public int Moves; }
        [Initial] public struct Idle : IState<Root> { }
        public struct Busy : IState<Root> { }
        [Module] public static class CoreModule
        {
            [Transition(From = typeof(Idle), To = typeof(Busy)), On(1)]
            public static void Start(ref Root root) => root.Moves++;
        }
        [Module] public static class ExtraModule
        {
            [Transition(From = typeof(Busy), To = typeof(Idle)), On(2)]
            public static void Stop(ref Root root) => root.Moves++;
        }
        [Module] public static class AlsoModule
        {
            [Transition(From = typeof(Busy)), On(3)]
            public static void Stay(ref Busy self) { }
        }
        public static class NotAModule { }
        [Machine(Root = typeof(Root), Value = typeof(byte))]
        {{machine}}
        public sealed partial class Machine { }
        """;

    [Test]
    public async Task AnExportedModuleJoinsAMachineThatAsksForIt()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start", "ExtraModule.Stop" });
    }

    [Test]
    public async Task AnExportedModuleStaysOutOfAMachineThatDidNotAsk()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start" });
    }

    [Test]
    public async Task ExceptLeavesOneOut()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]\n[assembly: ExportsModule(typeof(AlsoModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported(Except = new[] { typeof(AlsoModule) })]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start", "ExtraModule.Stop" });
    }

    /// <summary>A module named both ways is included once: an explicit include is not a duplicate.</summary>
    [Test]
    public async Task AModuleNamedTwiceIsIncludedOnce()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule)), Include(typeof(ExtraModule))]\n[IncludeExported]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start", "ExtraModule.Stop" });
    }

    [Test]
    public async Task ExportingSomethingThatIsNotAModuleIsSALCH0108()
    {
        var reported = Diagnostics(Source("[assembly: ExportsModule(typeof(NotAModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        await Assert.That(reported).Contains("SALCH0108");
    }

    [Test]
    public async Task AskingWhenNothingIsExportedIsSALCH0109()
    {
        var reported = Diagnostics(Source("", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        await Assert.That(reported).Contains("SALCH0109");
    }

    /// <summary>
    /// The machine an exported include builds is the machine an explicit include builds: same states, same
    /// transitions, same order. Anything else would make the two ways of naming a module mean different things.
    /// </summary>
    [Test]
    public async Task AnExportedIncludeBuildsTheSameMachineAsAnExplicitOne()
    {
        var exported = Text(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        var explicitly = Text(Source("", "[Include(typeof(CoreModule)), Include(typeof(ExtraModule))]"));
        await Assert.That(exported).IsEqualTo(explicitly);
    }

    /// <summary>And the reference front-end, reflecting over the same declarations, agrees with all of it.</summary>
    [Test]
    public async Task TheReferenceFrontEndReadsExportedModulesTheSameWay()
    {
        var source = Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]");
        var byRoslyn = Text(source);

        var reflected = Reference.ReflectionModelBuilder.FromMachine(Compile(source).GetType("Machine")!);
        await Assert.That(Model.ModelText.Of(reflected.Model)).IsEqualTo(byRoslyn);
    }

    private static Generated.MachineModel Build(string source)
    {
        var compilation = Compilation(source);
        return SymbolModelBuilder.Build(compilation.GetTypeByMetadataName("Machine")!, compilation).Model;
    }

    /// <summary>
    /// The source, compiled and checked: an assembly attribute in the wrong place is a C# error, and a test that
    /// did not look would silently prove nothing.
    /// </summary>
    private static CSharpCompilation Compilation(string source)
    {
        var compilation = TestCompilation.Create(source);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal)).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("the test's own source does not compile:\n" + string.Join("\n", errors) + "\n" + source);
        }

        return compilation;
    }

    private static string Text(string source) => Generated.ModelText.Of(Build(source));

    private static string[] Diagnostics(string source)
    {
        var compilation = Compilation(source);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics.Select(d => d.Id).ToArray();
    }

    /// <summary>Compiles <paramref name="source"/> with the generator and loads it, so reflection can read it.</summary>
    private static Assembly Compile(string source)
    {
        var compilation = TestCompilation.Create(LanguageVersion.Latest, TestCompilation.Net8Symbols, source);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        using var image = new MemoryStream();
        var emitted = generated.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        image.Position = 0;
        return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(image);
    }
}
```

The last two tests are the ones that matter: an exported include builds the model an explicit include builds, and
the reference front-end reads the same thing. The rest are the edges — `Except`, a module named twice, a
non-module export, and asking when nothing is offered.

Note that `StateAlchemist.Generators.Tests` does **not** reference the exporting library: its tests compile source
against every assembly the test process loaded, so a real exporting reference there would add a module to every
in-source machine that asks. The cross-assembly case is the generated test project's, where a real machine is
built.

- [ ] **Step 4: Run them**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: PASS. `ExportedMachine` handles 9 because a library offered the module that handles it.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Take a module from a referenced library, end to end"
```

---

### Task 3: The package, releasable

**Files:**
- Modify: `Directory.Build.props`, `Directory.Packages.props`, `src/StateAlchemist/StateAlchemist.csproj`,
  `eng/check-package.sh`
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: Plan 7's package and its check.
- Produces: a package whose version comes from the git tag, with a `.snupkg`, validated on every build, and a
  workflow that publishes it without a stored key.

- [ ] **Step 1: Version, symbols and determinism**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <IsPackable>false</IsPackable>
    <Authors>Harry Cordewener</Authors>
    <Copyright>Copyright © StateAlchemist Contributors 2026</Copyright>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/HarryCordewener/StateAlchemist</PackageProjectUrl>
    <RepositoryUrl>https://github.com/HarryCordewener/StateAlchemist</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageReleaseNotes>https://github.com/HarryCordewener/StateAlchemist/blob/main/CHANGELOG.md</PackageReleaseNotes>
  </PropertyGroup>

  <!--
    Reproducibility and consumer tooling. EmbedUntrackedSources plus a snupkg means a consumer can step into the
    library from their debugger against the exact commit that produced the package; ContinuousIntegrationBuild
    normalises source paths, so it is only correct on CI.
  -->
  <PropertyGroup>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <!-- Only where it ships: normalised paths are what Source Link needs in a package, and what would stop a test
         from finding the documentation it checks, since [CallerFilePath] becomes /_/… under it. -->
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true' AND '$(IsPackable)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <!--
    MinVer is the single source of the version: a `v1.2.3` tag builds 1.2.3, and any other commit builds the next
    patch as `-preview.0.N`. The release workflow therefore does NOT pass /p:Version — it only needs
    `fetch-depth: 0` so the tag is there to be read. A source tree with no git history at all (one extracted from
    the implementation plans, say) builds 0.0.0-alpha.0, which is why MinVer's "no commits" report is not an error.
  -->
  <PropertyGroup>
    <MinVerTagPrefix>v</MinVerTagPrefix>
    <MinVerDefaultPreReleaseIdentifiers>preview.0</MinVerDefaultPreReleaseIdentifiers>
    <MinVerSkip Condition="'$(Configuration)' == 'Debug'">true</MinVerSkip>
  </PropertyGroup>
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
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.8.0" />
    <PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />
    <PackageVersion Include="Stateless" Version="5.20.0" />
    <PackageVersion Include="PolySharp" Version="1.16.0" />
    <PackageVersion Include="MinVer" Version="8.0.0" />
    <PackageVersion Include="TUnit" Version="1.66.27" />
    <PackageVersion Include="TUnit.Core" Version="1.66.27" />
    <PackageVersion Include="TUnit.Assertions" Version="1.66.27" />
    <PackageVersion Include="PublicApiGenerator" Version="11.5.4" />
    <PackageVersion Include="Verify.TUnit" Version="32.0.0" />
  </ItemGroup>
</Project>
```

`src/StateAlchemist/StateAlchemist.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.0;net8.0;net10.0;net11.0</TargetFrameworks>
    <RootNamespace>StateAlchemist</RootNamespace>
    <IsPackable>true</IsPackable>
    <PackageId>StateAlchemist</PackageId>
    <Title>StateAlchemist</Title>
    <Description>A source-generated hierarchical state machine library: states own their data, transitions own the transformation, the compiler writes the machine.</Description>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <IsAotCompatible Condition="'$(TargetFramework)' != 'netstandard2.0'">true</IsAotCompatible>
    <PackageTags>state-machine;statemachine;source-generator;hierarchical;aot;telnet</PackageTags>
    <PackageReadmeFile>PACKAGE.md</PackageReadmeFile>
    <!-- The logo lives with the documentation, which a source tree built from the plans alone does not have. -->
    <PackageIcon Condition="Exists('..\..\docs\images\StateAlchemist.png')">StateAlchemist.png</PackageIcon>
    <!-- Every build is checked for framework compatibility across the package's target frameworks. Once a version
         is on nuget.org, set PackageValidationBaselineVersion to it and every later build is also diffed against
         that published surface, so a removed or re-signatured public member fails here rather than in a
         consumer's restore. See docs/releasing.md. -->
    <EnablePackageValidation>true</EnablePackageValidation>
  </PropertyGroup>

  <!-- The generator and the code fixes travel in this package, under analyzers/, so referencing StateAlchemist is
       all an application does. They are built first, and their output is packed rather than referenced: a runtime
       library must not depend on Roslyn. -->
  <ItemGroup>
    <ProjectReference Include="..\StateAlchemist.Generators\StateAlchemist.Generators.csproj" PrivateAssets="all" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\StateAlchemist.CodeFixes\StateAlchemist.CodeFixes.csproj" PrivateAssets="all" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup>
    <None Include="PACKAGE.md" Pack="true" PackagePath="\" />
    <None Include="..\..\docs\images\StateAlchemist.png" Condition="Exists('..\..\docs\images\StateAlchemist.png')" Pack="true" PackagePath="\" />
    <None Include="..\StateAlchemist.Generators\bin\$(Configuration)\netstandard2.0\StateAlchemist.Generators.dll" Pack="true" PackagePath="analyzers\dotnet\cs" Visible="false" />
    <None Include="..\StateAlchemist.CodeFixes\bin\$(Configuration)\netstandard2.0\StateAlchemist.CodeFixes.dll" Pack="true" PackagePath="analyzers\dotnet\cs" Visible="false" />
  </ItemGroup>

  <ItemGroup>
    <!-- The version comes from the git tag; see docs/releasing.md. -->
    <PackageReference Include="MinVer" PrivateAssets="all" />
  </ItemGroup>

  <PropertyGroup>
    <!-- A source tree with no git history — one extracted from the implementation plans — has no version to read,
         and 0.0.0-preview.0 is the right answer there rather than a warning on every build. -->
    <MSBuildWarningsAsMessages>$(MSBuildWarningsAsMessages);MINVER1001</MSBuildWarningsAsMessages>
  </PropertyGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
    <PackageReference Include="PolySharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" />
    <PackageReference Include="System.Memory" />
  </ItemGroup>
</Project>
```

`MinVerSkip` in Debug keeps everyday builds off git; a tree with no history at all builds `0.0.0-alpha.0`, which is
what a source tree extracted from these plans does.

- [ ] **Step 2: The check sees the symbols**

`eng/check-package.sh`:

```bash
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
```

- [ ] **Step 3: The workflow**

`.github/workflows/release.yml`:

```yaml
name: Release

on:
  workflow_dispatch:
    inputs:
      tags:
        required: true
        type: string
        description: Example - v0.1.0
  push:
    tags:
      - "v[0-9]+.[0-9]+.[0-9]+"

permissions:
  contents: read

jobs:
  test:
    name: Test
    runs-on: ubuntu-latest
    timeout-minutes: 20
    strategy:
      matrix:
        framework: [ net8.0, net10.0, net11.0 ]
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
          dotnet-version: |
            8.0.x
            10.0.x

      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore
      - run: dotnet test --solution StateAlchemist.slnx --no-build --configuration Release --framework ${{ matrix.framework }}

  # What the package is for: an application that references nothing else gets a generated machine and the
  # analyzers' diagnostics. Nothing is published if this fails.
  package:
    name: Check the package
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v6
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json
          dotnet-version: 8.0.x
      - run: eng/check-package.sh

  build:
    name: Pack
    runs-on: ubuntu-latest
    timeout-minutes: 20
    needs: [ test, package ]
    permissions:
      contents: read
      id-token: write
      attestations: write
    steps:
      # MinVer reads the version from the tag on this commit, so the tags and the commits between them have to be
      # present — a shallow clone would build 0.0.0-preview.0.x.
      - uses: actions/checkout@v6
        with:
          fetch-depth: 0

      - uses: actions/setup-dotnet@v5
        with:
          global-json-file: global.json

      - run: dotnet restore

      # A tag on a commit that never reached main would otherwise ship as a release.
      - name: Verify commit exists in origin/main
        run: |
          git fetch --no-tags --prune --depth=100 origin +refs/heads/*:refs/remotes/origin/*
          git branch --remote --contains | grep origin/main

      # Only to confirm the tag and the built version agree; the version itself comes from MinVer.
      - name: Set VERSION variable
        run: |
          if [ "${{ github.event_name }}" = "workflow_dispatch" ]; then
            TAG="${{ inputs.tags }}"
          else
            TAG="${GITHUB_REF#refs/tags/}"
          fi
          echo "VERSION=${TAG#v}" >> $GITHUB_ENV

      - name: Pack
        run: dotnet pack src/StateAlchemist --configuration Release -o packages
        env:
          CI: true

      - name: Verify the packed version matches the tag
        run: |
          if ! ls packages/StateAlchemist.${VERSION}.nupkg >/dev/null 2>&1; then
            echo "::error::Expected packages/StateAlchemist.${VERSION}.nupkg; MinVer produced:"
            ls packages
            exit 1
          fi

      # The generator and the code fixes are what make the package more than a few attributes.
      - name: Verify both Roslyn components are in the package
        run: |
          for entry in analyzers/dotnet/cs/StateAlchemist.Generators.dll analyzers/dotnet/cs/StateAlchemist.CodeFixes.dll; do
            if ! unzip -Z1 packages/StateAlchemist.${VERSION}.nupkg | grep -qx "$entry"; then
              echo "::error::$entry is missing from the package"
              unzip -Z1 packages/StateAlchemist.${VERSION}.nupkg
              exit 1
            fi
          done

      - name: Attest build provenance
        uses: actions/attest-build-provenance@v4
        with:
          subject-path: "packages/*.nupkg"

      - name: Upload NuGet packages
        uses: actions/upload-artifact@v7
        with:
          name: nuget-packages
          path: packages

  release:
    name: Publish
    runs-on: ubuntu-latest
    timeout-minutes: 15
    needs: build
    permissions:
      contents: read
      id-token: write
      packages: write
    steps:
      - name: Download NuGet packages
        uses: actions/download-artifact@v8
        with:
          name: nuget-packages
          path: packages

      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 8.0.x

      - name: Push to GitHub Packages
        run: dotnet nuget push "packages/*.nupkg" --skip-duplicate --source https://nuget.pkg.github.com/HarryCordewener/index.json --api-key ${GITHUB_TOKEN}
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      # Trusted publishing: the OIDC token minted by `id-token: write` is exchanged for a short-lived NuGet key.
      # No long-lived secret exists to leak or rotate. It requires a trusted-publishing policy on nuget.org for
      # this repository — see docs/releasing.md.
      - name: NuGet Login
        uses: NuGet/login@v1
        id: login
        with:
          user: harrycordewener

      - name: Push to NuGet
        run: dotnet nuget push "packages/*.nupkg" --skip-duplicate --source https://api.nuget.org/v3/index.json -k ${{ steps.login.outputs.NUGET_API_KEY }}
```

Nothing is published by CI on `main`: only a `v1.2.3` tag, or a manual dispatch, reaches the publish job. The pack
job refuses a tag whose commit is not on `origin/main`, checks that the packed version matches the tag, and checks
that both Roslyn components are inside the package — a package without them is a few attributes.

- [ ] **Step 4: Run the check**

Run: `eng/check-package.sh`
Expected: `== ok:` and a path, with the version MinVer derived from git.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Version from the tag, symbols in the package, and a release workflow"
```

---

### Task 4: Documentation

**Files:**
- Modify: `docs/concepts/machines.md`, `docs/reference/diagnostics.md`, `docs/index.md`, `CHANGELOG.md`
- Create: `docs/releasing.md`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: the flow documented where a reader looks for it, and a release procedure a maintainer can follow.

- [ ] **Step 1: The flow**

`docs/concepts/machines.md`:

````markdown
# Machines

## Modules

A module is a static class marked `[Module]`. It groups transitions, decisions and state actions; a protocol
plugin is typically one module. Modules can live in any library and can add transitions out of states that other
modules declared.

<!-- snippet: sample-gmcp-module -->
<a id='snippet-sample-gmcp-module'></a>
```cs
[Module]
public static class GmcpModule
{
    [Transition(From = typeof(Willing), To = typeof(Idle)), On(GmcpOption)]
    public static class Accept
    {
        public static void Transform(ref Connected root) => root.GmcpEnabled = true;

        public static ValueTask CompletedAsync(TelnetContext context) => context.SendAsync(Iac, Do, GmcpOption);
    }
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/GmcpModule.cs#L7-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-gmcp-module' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Every method a machine calls must be `public static`, because the generated code lives in the application
([`SALCH0002`](../reference/diagnostics.md#salch0002)); analyzers in the declaring library report that where the
method is written.

### Modules a library offers

A library that wants "reference the package, get the protocol" says so once, in its own assembly, and a machine
says it will take what its references offer:

```csharp
// In the library — in AssemblyInfo.cs, because an assembly attribute must precede every type in its file:
[assembly: ExportsModule(typeof(GmcpModule))]
```

```csharp
// In the application:
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[IncludeExported]                                       // every module this app's references export
[Include(typeof(MyOwnModule))]                          // plus one nothing exports
public sealed partial class MudTelnet;
```

Both ends opt in: a library's modules never arrive in a machine that did not ask, and a machine never picks up a
module the library meant to keep to itself. A module named both ways is included once, and
`[IncludeExported(Except = new[] { typeof(MsspModule) })]` takes all but a few. Exporting something that is not a
module is [`SALCH0108`](../reference/diagnostics.md#salch0108); asking when nothing is offered is
[`SALCH0109`](../reference/diagnostics.md#salch0109).

The machine an exported include builds is the machine an explicit `[Include]` would have built — the two are ways
of naming a module, not different kinds of module.

## The machine declaration

An application declares a machine on a `partial class`:

<!-- snippet: sample-machine -->
<a id='snippet-sample-machine'></a>
```cs
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class SampleTelnet
{
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/SampleTelnet.cs#L3-L9' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-machine' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The generator runs in the application and sees every included module, from every library, at once. It emits
one merged `switch` for exactly that set of modules. That is why conflicts between plugins — two modules claiming
the same option in the same state — are compile errors in the application
([`SALCH0101`](../reference/diagnostics.md#salch0101)) instead of surprises at runtime.

Modules are chosen when the application compiles. An application that needs several configurations — a client and
a server, say — declares several machine types.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Root` | required | The root state. |
| `Value` | required | The value-trigger type: an integral type or an enum of 16 bits or fewer. |
| `Context` | none | Your object, passed to actions, decisions, and — unless `Purity` is `Strict` — guards and transforms. |
| `Config` | none | Immutable configuration, passed as `in TConfig` to anything that asks for it. |
| `Concurrency` | `Checked` | How concurrent callers are handled; see [concurrency](concurrency.md). |
| `InboxCapacity` | 0 | For `Serialized`: a bounded inbox, so event producers wait instead of queueing without limit. |
| `Purity` | `Permissive` | `Strict` forbids guards, transforms and completions from taking the context. |
| `Unhandled` | `Ignore` | `Throw` throws `UnhandledTriggerException` when nothing handles a trigger. |

A missing `Root` or `Value` is [`SALCH0107`](../reference/diagnostics.md#salch0107).

### Purity

With `Purity.Strict`, a transition's `Guard`, `Transform` and a decision's `Complete` depend only on state data,
the trigger and the configuration. That makes the machine's [pure layer](../guides/testing.md#2-the-pure-layer)
exact: `Plan()` predicts what a trigger will do with no outside influence, and a transform can be tested with
nothing but values. Taking the context then is [`SALCH0205`](../reference/diagnostics.md#salch0205). With the
default, `Permissive`, the context is allowed, and the machine's definition records which transitions use it.

## What one machine costs

A machine *instance* is its state data — one slot per state — plus a few fields: the active leaf, a pending
decision, a small event queue. The machine *definition* is generated code and static data, shared by every
instance. Creating a machine for a new connection builds nothing.

## Diagrams

Every machine carries its own picture, as constants:

```csharp
Console.WriteLine(MudTelnet.Mermaid);   // a Mermaid stateDiagram-v2
File.WriteAllText("telnet.dot", MudTelnet.Dot);   // a Graphviz digraph
```

Both are written at compile time from the same model the machine runs — a composite state for every parent, its
`[Initial]` child marked, one arrow per transition labelled with its trigger, and `run` or `decide` where that is
what happens. Because they are `const string`s, a diagram costs nothing to read and cannot drift from the code: a
README that pastes `Mermaid` is as current as the build.
````

- [ ] **Step 2: The diagnostics**

`docs/reference/diagnostics.md`:

```markdown
# Diagnostics

Every problem StateAlchemist can find before your code runs. **Declaring library** diagnostics are reported where
a module or state is written; **app** diagnostics where a machine is assembled, because they depend on which
modules it includes.

| Range | About |
|---|---|
| `SALCH00xx` | states and the tree |
| `SALCH01xx` | triggers, conflicts, the machine declaration |
| `SALCH02xx` | method shapes, parameters, roles |
| `SALCH03xx` | re-entry |
| `SALCH04xx` | decisions |
| `SALCH05xx` | coverage and reachability |
| `SALCH06xx` | generated API |
| `SALCH07xx` | runs |
| `SALCH08xx` | lifecycle |
| `SALCH09xx` | authoring assistance: hints whose code fixes write code for you |

## Where each diagnostic is reported

Two components report these, and which one depends on what the problem needs to know.

- **The analyzer** reports a module's own problems, in the project where the module is written: a state that is not
  a public struct, a member that is not public static, a transition with no trigger, a phase whose name or
  signature is wrong. It has to, because Roslyn cannot see a referenced assembly's non-public members — a library's
  mistakes would otherwise be invisible until an application assembled a machine, and then be reported in the wrong
  place. It also carries the `SALCH09xx` hints whose code fixes write a phase for you.
- **The generator** reports everything that depends on which modules a machine includes: conflicts, coverage,
  reachability, roles, and the machine declaration itself. Those answers change with the machine, so they belong to
  the application that chose it.
- **Two analyzers read your calls** rather than your declarations: `SALCH0801`, when a method constructs a machine
  and fires it without starting it, and `SALCH0601`, when a machine that can suspend is fired synchronously.

Both ship in the package, so referencing StateAlchemist is all it takes.

---

## SALCH0001

**Invalid state declaration** · error · declaring library

> State '{0}' must be a public struct with exactly one parent marker; {1}

A state is a `public struct` implementing `IRootState` or exactly one `IState<TParent>`. A class, a non-public
struct, a struct implementing two markers, or a type used as a state that implements neither, is refused.

**Fix:** make it `public struct` and give it exactly one parent marker.

## SALCH0002

**Member is not public static** · error · declaring library

> '{0}' must be public and static

The generated machine lives in the application and calls your transitions, decisions and actions directly, so
each must be `public static`, in a public type.

## SALCH0003

**Invalid state hierarchy** · error · app

> State '{0}' {1}

The states of a machine form one tree: one root, every other state's parent in the machine, no loops. Reported
for a second root, a parent missing from the machine, or a parent chain that loops.

## SALCH0004

**Initial child required** · error · app

> State '{0}' has {1} [Initial] children; it needs exactly one

The machine rests only in leaves, so entering a state with children must know which child to enter.

**Fix:** mark exactly one child `[Initial]`.

## SALCH0101

**Conflicting transitions** · error · app

> '{0}' and '{1}' both handle {2} in state '{3}' without a guard

Two unguarded transitions of the same kind — two exact values, two overlapping ranges, two `[OnAny]`, two of the
same event — from the same state. Often two modules claiming the same option.

**Fix:** remove one, or guard them and give them distinct `Order`s. An exact value and a range, or a range and
`[OnAny]`, never conflict: the more specific one wins.

## SALCH0102

**Ambiguous guard order** · error · app

> Guarded transitions '{0}' and '{1}' both handle {2} in state '{3}' with Order {4}

Guarded transitions for the same trigger are tried in `Order`; two with the same `Order` have no defined order.

**Fix:** give them distinct `Order` values.

## SALCH0103

**Ambiguous action order** · error · app

> '{0}' and '{1}' both run when '{2}' is {3}, from different modules, with Order {4}

`[Exited]` or `[Entered]` actions for the same state from different modules run in `Order`; within one module,
declaration order decides. Two from different modules with the same `Order` have no defined order.

## SALCH0104

**Trigger outside the value type** · error · app

> '{0}' fires on {1}, which is outside the value type '{2}'

`[On(300)]` on a `byte` machine can never fire.

## SALCH0105

**Unsupported value type** · error · app

> The value type '{0}' is not supported: use an integral type or an enum of 16 bits or fewer

The generator emits a dense `switch` over the value type and scans it with vector instructions; both need a
small integral type.

## SALCH0106

**Invalid transition declaration** · error · declaring library

> '{0}' {1}

A transition does not name its `From` state, has no trigger, mixes value and event triggers, has an empty range,
or names a trigger value that is not an integral constant.

## SALCH0107

**Incomplete machine declaration** · error · app

> Machine '{0}' {1}

A `[Machine]` without `Root` or `Value`, or an `[Include]` of a type that is not a `[Module]`.

## SALCH0108

**Exported type is not a module** · error · app

> '{0}' exports '{1}', which is not a [Module]

An assembly's `[assembly: ExportsModule(typeof(X))]` offers `X` to any machine that writes `[IncludeExported]`, so
`X` has to be a `[Module]`. Reported on the machine that asked, because that is the build where the offer was taken
up — the library itself may never have been compiled against a machine.

**Fix:** mark the exported type `[Module]`, or stop exporting it.

## SALCH0109

**Nothing exported to include** · warning · app

> '{0}' includes exported modules, but nothing this assembly references exports one

The machine asks for what its references export and nothing does, so `[IncludeExported]` added nothing. Either a
package that was meant to offer a module does not, or the attribute is left over.

**Fix:** name the module with `[Include]`, or remove `[IncludeExported]`. See
[modules and the machine declaration](../concepts/machines.md#modules).

## SALCH0201

**Writing to an exiting state** · error · app

> Parameter '{0}' of '{1}' takes '{2}' by ref, but the transition exits it; take it as in

A state the transition leaves is cleared when the transition ends, so an edit to it would vanish.

**Fix:** take it as `in`, and write anything that must survive into a state that stays or is entered — there is a
code fix that does the first half. See
[what a transition may touch](../concepts/transitions.md#what-a-transition-may-touch).

## SALCH0202

**State not available here** · error · app

> Parameter '{0}' of '{1}' names '{2}', which {3}

The parameter names a state the transition does not touch — a sibling, an unrelated branch — or, for a
transition declared on a parent, a state that binds differently depending on the active leaf. For a state action,
a state that is neither the action's state nor one of its ancestors.

## SALCH0203

**Invalid phase signature** · error · app

> '{0}' must {1}

`Guard` returns `bool`; `Transform` and `Complete` return `void`; `Completed` returns `void` and
`CompletedAsync` `ValueTask`; `DecideAsync` returns `ValueTask<TOutcome>`; a decision declares `Decide` or
`DecideAsync`.

## SALCH0204

**Unbindable parameter** · error · app

> Parameter '{0}' of '{1}' cannot be bound: {2}

A parameter that is not a state, the value, the event that fired, a run, the configuration, the context, a
decision outcome, a `CancellationToken` or the transition info — or one of those where its method cannot take it,
such as a `CancellationToken` on a transform, or a value on an event transition.

## SALCH0205

**Context under strict purity** · error · app

> '{0}' takes the context, which machine '{1}' forbids with Purity.Strict

See [purity](../concepts/machines.md#purity).

## SALCH0206

**Unknown phase method** · error · declaring library

> '{0}' is not a phase; a class-form transition may declare Guard, Transform, Decide, DecideAsync, Complete, Completed and CompletedAsync

In a class-form transition, a method's name says when it runs. A name that is not a phase would never run.

## SALCH0207

**Async suffix does not match** · error · declaring library

> '{0}' {1}

A phase returning a task is named with `Async`; one that does not is named without. `Guard`, `Transform` and
`Complete` run before the state changes and cannot be async. The code fix renames the method.

## SALCH0208

**Two decide methods** · error · declaring library

> Decision '{0}' declares both Decide and DecideAsync

## SALCH0209

**Unchecked machine with async actions** · warning · app

> Machine '{0}' is Unchecked but has async actions or decisions; await every FireAsync before calling the next

See [concurrency](../concepts/concurrency.md#unchecked).

## SALCH0301

**Re-entry clears data** · warning · app

> '{0}' re-enters '{1}', which has data; the data is cleared

`To` equal to `From` exits and re-enters the state. If you meant to keep going, omit `To`: that is a stay. Not
reported for the root: it is never exited, so its data survives.

## SALCH0401

**Decision outcome not completed exactly once** · error · app

> Decision '{0}' has {1} Complete methods for outcome '{2}'; it needs exactly one

Every case of a decision's outcome union needs exactly one `Complete` overload.

## SALCH0402

**Unknown decision outcome** · error · app

> '{0}' completes '{1}', which is not an outcome of decision '{2}'

## SALCH0501

**Unhandled values** · warning · app

> Leaf '{0}' leaves {1} values unhandled and no [OnAny] covers them

In that leaf, those values match no transition at any level and fall to the machine's
[unhandled](../concepts/triggers.md#unhandled-triggers) behaviour. Add an `[OnAny]` on the leaf or an ancestor if
that is not what you want.

## SALCH0502

**Unreachable state** · warning · app

> State '{0}' cannot be reached from the initial state

No sequence of transitions, assuming every guard passes, reaches it.

## SALCH0601

**Synchronous fire on an async machine** · error · app

> '{0}' has async actions or decisions: fire it with FireAsync and await that

Every machine has the synchronous `Fire`, because the generator writes one machine at a time and cannot know how a
caller means to use it. On a machine that can suspend the call would block the calling thread until the action came
back — a deadlock waiting for a synchronization context — so using it there is an error at the call, where the
choice is made.

## SALCH0701

**Invalid run transition** · error · app

> [Run] on '{0}' is invalid: {1}

A run is a stay on `[OnAny]` or a range, without a guard, whose transform takes
`(ref TState self, ReadOnlySpan<TValue> run)` and optionally the configuration and context. See
[runs](../concepts/runs.md).

## SALCH0801

**Fired before started** · warning · anywhere

> '{0}' is fired before StartAsync on some path

See [lifecycle](../concepts/lifecycle.md#why-starting-is-separate).

## SALCH0901

**Transition declares no phases** · info · declaring library

> Transition '{0}' declares no phases

A class-form transition with no methods only changes state. That is valid, but usually unfinished. The code fixes
add **Guard**, **Transform** and **Completed**, each with the parameters this transition's own states give it: the
source as `in` for a move, the state itself as `ref` for a stay, the target as `ref`. A decision's `Decide` and
`Complete` are not offered — their outcome type is a union you choose, and a fix cannot invent one. If the
transition really is state-only, write it as a method instead.

## SALCH0902

**Phase can be added** · hidden · declaring library

> Transition '{0}' can declare {1}

Never shown as a warning: it exists so the same **Add Guard** / **Add Transform** / **Add Completed** code fixes
are available on a transition that already declares some phases. In Rider, Visual Studio and VS Code they appear
where the IDE offers quick-fixes.
```

- [ ] **Step 3: Releasing**

`docs/releasing.md`:

````markdown
# Releasing

For maintainers. One package, `StateAlchemist`, carrying the runtime under `lib/` and both Roslyn components —
the generator and the code fixes — under `analyzers/dotnet/cs`.

## How a version is decided

[MinVer](https://github.com/adamralph/minver) reads it from git, so there is no version number in the repository
to forget to bump:

| Commit | Version |
|---|---|
| tagged `v1.2.3` | `1.2.3` |
| anything else | the next patch as `1.2.4-preview.0.N` |
| a tree with no git history (extracted from the plans) | `0.0.0-alpha.0` |

That is why the release job checks out with `fetch-depth: 0`, and why the workflow does **not** pass `/p:Version` —
it would fight MinVer. A "Verify the packed version matches the tag" step fails the build if the two disagree.

## Cutting a release

1. Move every entry under `## Unreleased` in [`CHANGELOG.md`](../CHANGELOG.md) into a new version heading, and
   commit it to `main`.
2. Tag and push:

   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```

   The tag triggers `.github/workflows/release.yml`; `workflow_dispatch` with the tag name as input does the same
   thing by hand.
3. After the release lands on nuget.org, set `PackageValidationBaselineVersion` to it in
   `src/StateAlchemist/StateAlchemist.csproj`. Package validation then diffs every later build against that
   published surface and fails on a break — including one you did not mean to make.

   A release that *does* remove or re-signature public API is the exception: drop the property for that build,
   release, then set it to the new version. The API snapshot test (`RuntimeApi.verified.txt`) still records the
   change, so the removal is reviewed rather than merely permitted.

## What the release workflow does

| Job | |
|---|---|
| `test` | builds Release and runs the whole suite on net8.0, net10.0 and net11.0 |
| `package` | runs [`eng/check-package.sh`](../eng/check-package.sh): packs, checks the contents, then builds and runs a throwaway application that references nothing but the package — the machine is generated, the diagram prints, and the analyzer's `SALCH0002` fails that application's build when it should |
| `build` | refuses a tag whose commit is not on `origin/main`, packs, checks that the version matches the tag and that both Roslyn components are inside, attests build provenance, and uploads the `.nupkg`/`.snupkg` |
| `release` | pushes to GitHub Packages, then to nuget.org |

Provenance attestation means a consumer can verify that a given `.nupkg` was built by this workflow, from this
repository, at that commit.

## Authentication

There are **no long-lived NuGet API keys in this repository** — no secret to leak, rotate or expire. Two
mechanisms, both short-lived:

- **GitHub Packages** uses the workflow's own `GITHUB_TOKEN`.
- **nuget.org** uses [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing):
  `NuGet/login@v1` exchanges the workflow's OIDC token for a key that lasts minutes. It requires a
  trusted-publishing policy on nuget.org for the `harrycordewener` account naming this repository and the
  `Release` workflow. Without that policy the push step fails with an authentication error, which is the first
  release's usual surprise.

## Before the first release

- The package has never been published, so there is no `PackageValidationBaselineVersion` yet, and package
  validation only checks that the frameworks in the package are consistent with each other.
- Decide the first version deliberately: `0.1.0` says "the design is implemented, the API may still move", which
  is what the [roadmap](superpowers/plans/2026-09-11-00-roadmap.md) currently claims.
- Check `dotnet pack src/StateAlchemist -c Release` locally and read `eng/check-package.sh`'s output once by hand.
````

`docs/index.md`:

```markdown
# StateAlchemist documentation

> *"To obtain, something of equal value must be lost."*
> — Alphonse Elric, *Fullmetal Alchemist*

StateAlchemist is a hierarchical state machine library for .NET where the machine is **compiled, not
interpreted**. Libraries declare states and transitions; an application picks the modules it wants; a source
generator writes the machine as straight-line code.

> **Status: implemented, not released.** These pages describe the library, and the code is built against them —
> the plans in the [roadmap](superpowers/plans/2026-09-11-00-roadmap.md) are all written and validated. Most code
> samples here are included from the compiled samples project, so they cannot drift; the rest are checked to
> parse. Nothing is on NuGet yet; [releasing](releasing.md) says what happens when it is.

## Start here

- [Getting started](guides/getting-started.md) — a small telnet machine, end to end.

## Concepts

| Page | What it covers |
|---|---|
| [States](concepts/states.md) | State structs, the tree they form, how long their data lives |
| [Transitions](concepts/transitions.md) | Two forms, three kinds, and which states a transition may read or write |
| [Triggers](concepts/triggers.md) | Values and events; which transition wins; guards; unhandled triggers |
| [Actions](concepts/actions.md) | Code that runs after the state changes, and the exact order of a transition |
| [Decisions](concepts/decisions.md) | When outside code chooses the outcome; pending states, deferral, backpressure |
| [Runs](concepts/runs.md) | Consuming a whole stretch of input in one call |
| [Machines](concepts/machines.md) | Modules, the machine declaration, and its options |
| [Lifecycle](concepts/lifecycle.md) | Starting, stopping, disposing |
| [Concurrency](concepts/concurrency.md) | `Checked`, `Unchecked` and `Serialized`, and what each costs |
| [Exceptions](concepts/exceptions.md) | What happens when your code throws, and how to choose the recovery |

## Guides

- [Testing a machine](guides/testing.md) — the pure layer, the machine interface, and the reference interpreter.
- [Writing a module](guides/writing-a-module.md) — adding a protocol to a machine you do not own.

## Reference

- [Generated API](reference/generated-api.md) — every member the generator adds to a machine.
- [Diagnostics](reference/diagnostics.md) — every `SALCH` diagnostic, with its cause and fix.
- [Releasing](releasing.md) — for maintainers: how a version is decided and what the release workflow does.
- [Design specification](superpowers/specs/2026-09-11-statealchemist-design.md) — every decision and why.
```

`CHANGELOG.md`:

```markdown
# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added
- Exported modules: a library offers a module with `[assembly: ExportsModule(typeof(M))]` and a machine takes what
  its references offer with `[IncludeExported]`, optionally `Except` some — so adding a protocol is adding a
  package reference. New diagnostics `SALCH0108` and `SALCH0109`.
- Packaging: MinVer decides the version from the git tag, the package carries a `.snupkg` and Source Link,
  package validation runs on every build, and `.github/workflows/release.yml` publishes to GitHub Packages and
  nuget.org through trusted publishing. See [docs/releasing.md](docs/releasing.md).
- Repository skeleton, state markers (`IRootState`, `IState<TParent>`, `[Initial]`) and `IEvent`.
```

- [ ] **Step 4: Check the documentation**

Run: `dotnet test --solution StateAlchemist.slnx && dotnet mdsnippets && git diff --exit-code -- '*.md'`
Expected: PASS, and no diff.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Document exported modules and how a release happens"
```

---

## What Plan 8 leaves

- **Nothing is published.** The first release is a decision: a version number, a trusted-publishing policy on
  nuget.org, and a `v0.1.0` tag. [`docs/releasing.md`](../../releasing.md) is the procedure.
- **`PackageValidationBaselineVersion` is unset**, because there is no published version to diff against yet. Set
  it to the first release immediately after it lands.
- **TNC's side of the handshake** — `[ProtocolModule]` on a plugin and the analyzer that pairs it with an
  `[Include]` — belongs to the [migration](../specs/2026-09-11-tnc-4.0-migration-design.md), not here.

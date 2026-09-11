# StateAlchemist Plan 2 — Reference Interpreter and Contract Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Write the library's semantics down as tests against `IMachine<TValue>` alone — the contract suite — and
make them pass on a reference interpreter that runs a machine by reflection over Plan 1's model. The generator
(Plan 4 onward) is later held to the same suite.

**Architecture:** `tests/StateAlchemist.Contracts` is a library, not a test project: purpose-built machines, and one
abstract TUnit class per concept page, each test written against `IMachine<byte>` and nothing else. An
implementation's test project derives from each class with `[InheritsTests]` and supplies one factory method. The
reference interpreter (`ReferenceMachine<TValue>` in `StateAlchemist.Reference`) is deliberately naive: the model's
own `Resolver` picks the transition, `PathPlanner` plans it, every phase is invoked by reflection, and state data
lives in boxed slots. It exists to be obviously right.

**Tech Stack:** as Plan 1 — .NET 11 SDK RC1 (`11.0.100-rc.1.26425.128`), C# `latest`, TUnit 1.66.27 (`TUnit.Core`
and `TUnit.Assertions` in the contract library, `TUnit` in test projects), Microsoft.Testing.Platform.

**Spec:** [`docs/superpowers/specs/2026-09-11-statealchemist-design.md`](../specs/2026-09-11-statealchemist-design.md)
— §6 (semantics) is what the suite tests; §6.2 (order of operations) and §6.9 (exceptions) are what the interpreter
implements step by step. The roadmap: [`2026-09-11-00-roadmap.md`](2026-09-11-00-roadmap.md).

> **Validated (2026-09-11):** every task was built on top of Plan 1's validated tree and run on net8.0, net10.0 and
> net11.0 — 507 tests, all passing. The code below is that code. The design findings validation surfaced are
> already in the spec and docs (see [Findings](#findings-from-validating-plan-2)); this plan does not edit docs.

## Global Constraints

Plan 1's constraints hold unchanged: warnings are errors, `Nullable` on, `ImplicitUsings` off, TUnit on
Microsoft.Testing.Platform, projects added to the solution with `--include-references false`, and every task ends
with `dotnet build` and `dotnet test --solution StateAlchemist.slnx` green. In addition:

- **Contract tests use only `IMachine<byte>`** and the runtime's public types. A test that needs to reach into an
  implementation is not a contract test — put it in that implementation's project.
- **Contract machines are ordinary declarations** (`[Module]` classes and state structs) so a generated machine can
  compile the same code in Plan 4.
- **Every contract names its source**: the class summary links the doc page it enforces. A test that cannot be
  traced to the docs either finds a gap in the docs — fix the docs first — or is testing an implementation detail.
- **Decisions are out of scope** until Plan 3; the interpreter throws `NotSupportedException` for them.
- **Performance is out of scope.** The interpreter allocates freely; no contract test measures time or allocation.

## File structure

```
src/StateAlchemist.Reference/Interpreter/     new in this plan
    ReferenceMachine.cs            IMachine<TValue>: queries, lifecycle, firing, the event queue, plans
    ReferenceMachine.Execution.cs  §6.2 steps 1–8, hooks, exception resolution, binding by reflection
    ReferenceHooks.cs              what a generated machine's partial hooks are, as delegates
    DefinitionBuilder.cs           the model as a MachineDefinition
    InvalidMachineException.cs     a machine with errors cannot be interpreted
tests/StateAlchemist.Contracts/               new: the contract suite (a library)
    MachineShape.cs · MachineContract.cs · ContractHooks.cs
    Machines/   RecordingContext, Recording (states), RecorderModule, Guards, Failures, Shapes
    Suite/      Lifecycle, Transition, Resolution, Event, Unhandled, Exception, Plan, Definition,
                Concurrency, Telnet contracts
tests/StateAlchemist.Reference.Tests/
    Contracts/  ReferenceHarness, ReferenceContracts ([InheritsTests] per contract)
    Interpreter/ReferenceMachineTests
```

Modified: `src/StateAlchemist.Model/Analysis/RoleValidator.cs` (Task 1), `src/StateAlchemist/Runtime/IMachine.cs`
(XML docs, Task 4), `tests/StateAlchemist.Reference.Tests/StateAlchemist.Reference.Tests.csproj` (Task 4).

---

### Task 1: Re-entering the root is not warned

A re-entry clears the state it re-enters, so `SALCH0301` warns when that state has data. The root is never exited
(spec §6.3), so re-entering it clears nothing, and the warning would be wrong. The interpreter's contract machine
has exactly such a transition (`ToRoot`, Task 2), which is how validation found this.

**Files:**
- Modify: `tests/StateAlchemist.Model.Tests/RoleValidatorTests.cs` (one test added)
- Modify: `src/StateAlchemist.Model/Analysis/RoleValidator.cs` (one condition)

**Interfaces:**
- Consumes: `RoleValidator`, `TestModel` (Plan 1, Tasks 8 and 6).
- Produces: nothing new; `SALCH0301` is no longer reported for a re-entry whose source is the root.

- [ ] **Step 1: Write the failing test**

Add `ReenteringTheRootClearsNothingSoIsNotWarned` after `AReentryMayReadTheOldDataAndWriteTheNew`. The whole file:

`tests/StateAlchemist.Model.Tests/RoleValidatorTests.cs`:

```csharp
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class RoleValidatorTests
{
    private readonly TestModel _model = new();
    private readonly int _root;
    private readonly int _idle;
    private readonly int _sub;
    private readonly int _awaiting;
    private readonly int _naws;

    public RoleValidatorTests()
    {
        _root = _model.Root(hasData: true);
        _idle = _model.State("Idle", _root, initial: true);
        _sub = _model.State("Sub", _root, hasData: true);
        _awaiting = _model.State("Awaiting", _sub, initial: true);
        _naws = _model.State("Naws", _sub, hasData: true);
    }

    private string Problems()
    {
        var built = _model.Build();
        return RoleValidator.Validate(built, new Hierarchy(built.States)).Describe();
    }

    [Test]
    public async Task ASiblingMoveMayWriteTheParentAndTheTarget()
    {
        _model.Add("Begin", _awaiting, _naws, TriggerModel.Value(31), _model.In(_awaiting), _model.Ref(_sub), _model.Ref(_naws));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task WritingAnExitingStateIsSalch0201()
    {
        _model.Add("Leave", _naws, _idle, TriggerModel.Value(1), _model.Ref(_sub));
        await Assert.That(Problems()).IsEqualTo("SALCH0201: Parameter 'sub' of 'Leave.Transform' takes 'Sub' by ref, but the transition exits it; take it as in");
    }

    [Test]
    public async Task NamingAStateTheTransitionDoesNotTouchIsSalch0202()
    {
        _model.Add("Begin", _awaiting, _naws, TriggerModel.Value(31), _model.In(_idle));
        await Assert.That(Problems()).IsEqualTo("SALCH0202: Parameter 'idle' of 'Begin.Transform' names 'Idle', which has no role in this transition");
    }

    [Test]
    public async Task AnAncestorDeclaredTransitionMayNameAStateThatBindsTheSameWayFromEveryLeaf()
    {
        // From Sub to Naws: from Awaiting, Naws is entering; from Naws itself, Naws is started over. Either way
        // `ref Naws` writes the fresh Naws, and `ref Sub` the Sub that stays.
        _model.Add("Jump", _sub, _naws, TriggerModel.Value(9), _model.Ref(_sub), _model.Ref(_naws));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task AStateTouchedFromOnlySomeLeavesIsSalch0202()
    {
        // `in Awaiting` reads the state being left when the leaf is Awaiting; from Naws there is nothing to read.
        _model.Add("Peek", _sub, _naws, TriggerModel.Value(8), _model.In(_awaiting));
        await Assert.That(Problems()).IsEqualTo("SALCH0202: Parameter 'awaiting' of 'Peek.Transform' names 'Awaiting', which is not touched from every leaf this transition fires from");
    }

    [Test]
    public async Task AReentryMayReadTheOldDataAndWriteTheNew()
    {
        _model.Add("Restart", _naws, _naws, TriggerModel.Value(1), _model.In(_naws), _model.Ref(_naws));
        await Assert.That(Problems()).IsEqualTo("SALCH0301: 'Restart' re-enters 'Naws', which has data; the data is cleared");
    }

    [Test]
    public async Task ReenteringTheRootClearsNothingSoIsNotWarned()
    {
        _model.Add("Restart", _root, _root, TriggerModel.Value(6), _model.Ref(_root));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task AGuardOnlyReads()
    {
        var guarded = _model.Add("Maybe", _naws, _idle, TriggerModel.Value(2), guarded: true, order: 0);
        _model.Add(guarded with
        {
            Index = 0,
            Trigger = TriggerModel.Value(3),
            Guard = TestModel.Method("Maybe", "Guard", ReturnShape.Bool, _model.Ref(_naws)),
        });
        await Assert.That(Problems()).IsEqualTo("SALCH0204: Parameter 'naws' of 'Maybe.Guard' cannot be bound: a guard only reads states: take it as in");
    }

    [Test]
    public async Task AStateActionReadsItsStateAndItsAncestors()
    {
        _model.Action(ActionPhase.Exited, _naws, "T.Module", 0, _model.In(_naws), _model.In(_sub), _model.In(_idle));
        await Assert.That(Problems()).IsEqualTo("SALCH0202: Parameter 'idle' of 'T.Module.OnExited' names 'Idle', which is not 'Naws' or one of its ancestors");
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: FAIL — `ReenteringTheRootClearsNothingSoIsNotWarned` receives
`"SALCH0301: 'Restart' re-enters 'Root', which has data; the data is cleared"`.

- [ ] **Step 3: Exempt the root**

In `RoleValidator.Validate`, the re-entry check becomes
`transition.Kind == MoveKind.Reenter && model.States[transition.Source].HasData && transition.Source != hierarchy.Root`.
The whole file:

`src/StateAlchemist.Model/Analysis/RoleValidator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks every state parameter against its role in its transition, for every leaf the transition can fire from:
/// a parameter must bind the same way from every leaf (spec §6.3) (SALCH0201, SALCH0202, SALCH0204 for state
/// passing, SALCH0301).
/// </summary>
public static class RoleValidator
{
    private enum Use
    {
        Guard,
        Transform,
        Action,
    }

    /// <summary>What a state parameter reads or writes.</summary>
    private enum Binding
    {
        None,
        Old,
        Live,
        Fresh,
    }

    /// <summary>Every role problem.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var transition in model.Transitions)
        {
            var leaves = hierarchy.LeavesUnder(transition.Source).ToList();
            Func<int, TransitionPath> plan = leaf => PathPlanner.Plan(hierarchy, transition, leaf);

            if (transition.Guard is not null)
            {
                Check(model, hierarchy, transition.Guard, Use.Guard, leaves, plan, diagnostics);
            }

            if (transition.Transform is not null)
            {
                Check(model, hierarchy, transition.Transform, Use.Transform, leaves, plan, diagnostics);
            }

            foreach (var completed in transition.Completed)
            {
                Check(model, hierarchy, completed, Use.Action, leaves, plan, diagnostics);
            }

            foreach (var completion in transition.Decision?.Completions ?? [])
            {
                Check(model, hierarchy, completion.Complete, Use.Transform, leaves, leaf => PathPlanner.Move(hierarchy, leaf, completion.Target), diagnostics);
            }

            // A re-entry of the root is a move to the root, which never exits it: nothing is cleared.
            if (transition.Kind == MoveKind.Reenter && model.States[transition.Source].HasData && transition.Source != hierarchy.Root)
            {
                diagnostics.Add(new(DiagnosticCatalog.ReentryClearsData, transition.Location, transition.Name, model.States[transition.Source].Name));
            }
        }

        foreach (var action in model.StateActions)
        {
            foreach (var parameter in action.Method.Parameters.Where(p => p.Kind == ParameterKind.State))
            {
                if (!hierarchy.IsAncestorOrSelf(parameter.State, action.State))
                {
                    diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, action.Method.Location, parameter.Name, action.Method.FullName,
                        model.States[parameter.State].Name, $"is not '{model.States[action.State].Name}' or one of its ancestors"));
                }
                else if (parameter.Passing is Passing.Ref or Passing.Out)
                {
                    diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, action.Method.Location, parameter.Name, action.Method.FullName,
                        "an action reads states: take it by value or as in"));
                }
            }
        }

        return diagnostics;
    }

    private static void Check(
        MachineModel model,
        Hierarchy hierarchy,
        MethodModel method,
        Use use,
        IReadOnlyList<int> leaves,
        Func<int, TransitionPath> plan,
        List<ModelDiagnostic> diagnostics)
    {
        var parameters = method.Parameters.Where(p => p.Kind == ParameterKind.State).ToList();
        foreach (var group in parameters.GroupBy(p => p.State).Where(g => g.Count() > 1))
        {
            var passings = group.Select(p => p.Passing).OrderBy(p => p).ToList();
            var startedOver = leaves.All(leaf => Roles.Of(hierarchy, plan(leaf), group.Key).HasFlag(Role.Exiting | Role.Entering));
            if (!(use == Use.Transform && passings.SequenceEqual([Passing.In, Passing.Ref]) && startedOver))
            {
                diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, method.Location, group.Last().Name, method.FullName,
                    $"'{model.States[group.Key].Name}' appears more than once"));
            }
        }

        foreach (var parameter in parameters)
        {
            var stateName = model.States[parameter.State].Name;
            var roles = leaves.Select(leaf => Roles.Of(hierarchy, plan(leaf), parameter.State)).ToList();
            if (roles.All(role => role == Role.None))
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "has no role in this transition"));
                continue;
            }

            var passingProblem = (use, parameter.Passing) switch
            {
                (Use.Guard, not Passing.In) => "a guard only reads states: take it as in",
                (Use.Transform, Passing.Value or Passing.Out) => "take a state as in or ref",
                (Use.Action, Passing.Ref or Passing.Out) => "an action reads states: take it by value or as in",
                _ => null,
            };
            if (passingProblem is not null)
            {
                diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, method.Location, parameter.Name, method.FullName, passingProblem));
                continue;
            }

            if (use == Use.Transform && parameter.Passing == Passing.Ref && roles.Any(role => role == Role.Exiting))
            {
                diagnostics.Add(new(DiagnosticCatalog.RefOnExitingState, method.Location, parameter.Name, method.FullName, stateName));
                continue;
            }

            var bindings = roles.Select(role => Bind(role, parameter.Passing)).Distinct().ToList();
            if (bindings.Contains(Binding.None))
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "is not touched from every leaf this transition fires from"));
                continue;
            }

            if (bindings.Count > 1)
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "binds differently depending on the active leaf"));
                continue;
            }

            if (use == Use.Guard && bindings[0] == Binding.Fresh)
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "is not active when the guard runs"));
            }
        }
    }

    /// <summary>
    /// What a parameter binds to, given the state's role: <c>ref</c> writes the state as it will be (entering, or
    /// staying); <c>in</c> reads it as it was (exiting, or staying) — or, for a state only being entered, its fresh value.
    /// </summary>
    private static Binding Bind(Role role, Passing passing) => passing switch
    {
        Passing.Ref when role.HasFlag(Role.Entering) => Binding.Fresh,
        Passing.Ref when role.HasFlag(Role.Staying) => Binding.Live,
        Passing.Ref => Binding.None,
        _ when role.HasFlag(Role.Exiting) => Binding.Old,
        _ when role.HasFlag(Role.Staying) => Binding.Live,
        _ when role.HasFlag(Role.Entering) => Binding.Fresh,
        _ => Binding.None,
    };
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "SALCH0301: re-entering the root clears nothing, so is not warned"
```

---

### Task 2: The contract machines

Before any contract can be written, it needs machines to run. The sample telnet machine (Plan 1, Task 5) shows the
docs' patterns but cannot show *order*: its actions do not say when they ran. These machines exist to be observed.
Every transition, action and guard records what it did into the context's log, so a test compares one joined string
— `"exited A1 (0) | exited A1 (extra) | entered A2 | completed Sibling"` — and an ordering bug shows as a diff.

**Files:**
- Create: `tests/StateAlchemist.Contracts/StateAlchemist.Contracts.csproj`, `MachineShape.cs`, `ContractHooks.cs`,
  `MachineContract.cs`
- Create: `tests/StateAlchemist.Contracts/Machines/RecordingContext.cs`, `Recording.cs`, `RecorderModule.cs`,
  `Guards.cs`, `Failures.cs`, `Shapes.cs`
- Modify: `StateAlchemist.slnx`

**Interfaces:**
- Consumes: the runtime API (Plan 1, Tasks 1–4) and the samples (Plan 1, Task 5).
- Produces (namespace `StateAlchemist.Contracts`):
  - `sealed record MachineShape(string Name, Type Root, IReadOnlyList<Type> Modules, Type Context, Concurrency
    Concurrency = Checked, Purity Purity = Permissive, Unhandled Unhandled = Ignore)` — a machine described as a
    `[Machine]` declaration would describe it.
  - `abstract class MachineContract` — `protected abstract IMachine<byte> Create(MachineShape shape, object context,
    ContractHooks? hooks)`; `protected Task<IMachine<byte>> StartAsync(MachineShape, object context, ContractHooks?
    hooks = null)`, which also hands the machine to the hooks and to a `RecordingContext`.
  - `sealed class ContractHooks(List<string> log)` — `HandleExceptions`, `Resolution` (`Func<Phase,
    ExceptionResolution>`), `AfterException` (`Action<IMachine<byte>>`), `Machine`; hook bodies `Transitioned(in
    TransitionInfo<byte>)`, `UnhandledValue(Type, byte)`, `UnhandledEvent(Type, object)`, `Exception(Exception, in
    TransitionInfo<byte>)` returning the resolution. An implementation wires its own hook mechanism to these.
  - `Machines.RecordingContext` — `Log`, `Trace` (the log joined with `" | "`), `Failing` (entries that throw
    `"{entry} failed"` when recorded), `Allow` (guards that pass), `Gate` (a `TaskCompletionSource` transition 11
    awaits), `Machine`.
  - `Machines.Shapes` — `Recorder`, `Guards`, `GuardsThatThrow`, `Failures`, `Telnet`.
- The machines:
  - **Recorder** (`Machines.Recording`): `Root {Counter}` → `A [Initial] {Value}` → `A1 [Initial] {Value}`, `A2
    {Value}`; `Root` → `B {Value}` → `B1 [Initial] {Buffer, Count; Reset()}`; event `Ping {Amount}`. Transitions on
    values 1–11: a sibling move, a cousin move, a stay, a re-entry, a move declared on a parent, a move to the root,
    a move back, a capture into `B1`'s buffer, a stay that enqueues an event, an action that fires its own machine,
    and an action that waits on `Gate`. Every state records its `[Exited]` and `[Entered]`; `A1` has a second
    `[Exited]` from another module (`RecorderExtras`).
  - **Guards** (`Machines.Guards`): three transitions on value 1 — two guarded (`Order` 1 and 2), one unguarded — a
    range 10–19, an `[OnAny]`, a root-level transition on 42, and a way back. The chosen transition's name lands in
    `Chosen.By`.
  - **Failures** (`Machines.Failures`): one transition whose guard, transform, `[Exited]`, two `[Entered]` and
    `Completed` each record and can be made to throw through `Failing`; a `Recover` event returns to the start.

- [ ] **Step 1: Create the project**

`tests/StateAlchemist.Contracts/StateAlchemist.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- The contract suite: abstract tests against IMachine<byte>, and the machines they run. Every implementation's
         test project inherits these with [InheritsTests]; this library runs nothing on its own. -->
    <TargetFrameworks>net8.0;net10.0;net11.0</TargetFrameworks>
    <RootNamespace>StateAlchemist.Contracts</RootNamespace>
    <IsTestProject>false</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit.Core" />
    <PackageReference Include="TUnit.Assertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\StateAlchemist\StateAlchemist.csproj" />
    <ProjectReference Include="..\..\samples\StateAlchemist.Samples\StateAlchemist.Samples.csproj" />
  </ItemGroup>
</Project>
```

`IsTestProject` is `false`: this library runs nothing itself. Referencing only `TUnit.Core` and `TUnit.Assertions`
— not the `TUnit` meta-package — keeps it from becoming an executable test host.

`tests/StateAlchemist.Contracts/MachineShape.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace StateAlchemist.Contracts;

/// <summary>
/// A contract machine, described the way a <c>[Machine]</c> declaration would describe it. Each implementation under
/// test turns a shape into a machine: the reference interpreter by reflection, the generated test project by the
/// <c>[Machine]</c> class it declares for the same shape.
/// </summary>
/// <param name="Name">The machine's name.</param>
/// <param name="Root">The root state.</param>
/// <param name="Modules">The included modules, in order.</param>
/// <param name="Context">The context type.</param>
/// <param name="Concurrency">The concurrency mode.</param>
/// <param name="Purity">The purity mode.</param>
/// <param name="Unhandled">The unhandled-trigger mode.</param>
public sealed record MachineShape(
    string Name,
    Type Root,
    IReadOnlyList<Type> Modules,
    Type Context,
    Concurrency Concurrency = Concurrency.Checked,
    Purity Purity = Purity.Permissive,
    Unhandled Unhandled = Unhandled.Ignore);
```

`tests/StateAlchemist.Contracts/MachineContract.cs`:

```csharp
using System.Threading.Tasks;

namespace StateAlchemist.Contracts;

/// <summary>
/// The base of every contract. An implementation's test project derives from each contract with
/// <c>[InheritsTests]</c> and says how to build a machine; every test is then written against
/// <see cref="IMachine{TValue}"/> alone.
/// </summary>
public abstract class MachineContract
{
    /// <summary>Builds a machine of <paramref name="shape"/>, not yet started.</summary>
    /// <param name="shape">The machine.</param>
    /// <param name="context">Its context.</param>
    /// <param name="hooks">Its hooks, or <see langword="null"/> for none implemented.</param>
    protected abstract IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks);

    /// <summary>Builds and starts a machine.</summary>
    protected async Task<IMachine<byte>> StartAsync(MachineShape shape, object context, ContractHooks? hooks = null)
    {
        var machine = Create(shape, context, hooks);
        if (hooks is not null)
        {
            hooks.Machine = machine;
        }

        if (context is Machines.RecordingContext recording)
        {
            recording.Machine = machine;
        }

        await machine.StartAsync();
        return machine;
    }
}
```

`tests/StateAlchemist.Contracts/ContractHooks.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace StateAlchemist.Contracts;

/// <summary>
/// What a contract machine's hooks do, whichever implementation runs it: record into a shared log and, for exception
/// hooks, return the resolution a test asks for. An implementation installs the exception hook only when
/// <see cref="HandleExceptions"/> is set — as a generated machine emits no <c>try</c>/<c>catch</c> unless the
/// application implements the hook.
/// </summary>
/// <param name="log">The log to record into, usually the context's.</param>
public sealed class ContractHooks(List<string> log)
{
    /// <summary>Whether the exception hooks are implemented.</summary>
    public bool HandleExceptions { get; init; }

    /// <summary>The resolution the exception hooks return, by phase. Defaults to <see cref="ExceptionResolution.Rethrow"/>.</summary>
    public Func<Phase, ExceptionResolution> Resolution { get; init; } = _ => ExceptionResolution.Rethrow;

    /// <summary>Run by an exception hook after recording, with the machine — for example to enqueue a recovery event.</summary>
    public Action<IMachine<byte>>? AfterException { get; init; }

    /// <summary>The machine, set by the harness once it is created.</summary>
    public IMachine<byte>? Machine { get; set; }

    /// <summary><c>OnTransitioned</c>.</summary>
    public void Transitioned(in TransitionInfo<byte> transition) => log.Add($"transitioned {transition.Transition}");

    /// <summary><c>OnUnhandled</c> for a value.</summary>
    public void UnhandledValue(Type state, byte value) => log.Add($"unhandled {value} in {state.Name}");

    /// <summary><c>OnUnhandled</c> for an event.</summary>
    public void UnhandledEvent(Type state, object e) => log.Add($"unhandled {e.GetType().Name} in {state.Name}");

    /// <summary><c>On{Phase}Exception</c>.</summary>
    public ExceptionResolution Exception(Exception exception, in TransitionInfo<byte> transition)
    {
        log.Add($"hook {transition.Phase}{(transition.State is null ? "" : " " + transition.State.Name)}: {exception.Message}");
        AfterException?.Invoke(Machine!);
        return Resolution(transition.Phase);
    }
}
```

`HandleExceptions` exists because an exception hook changes behaviour merely by being implemented: without one, a
generated machine emits no `try`/`catch` at all (spec §6.9). A harness installs its exception hook only when a test
sets it.

- [ ] **Step 2: Write the machines**

`tests/StateAlchemist.Contracts/Machines/RecordingContext.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StateAlchemist.Contracts.Machines;

/// <summary>
/// The context of every contract machine except the telnet sample. Actions and guards record into <see cref="Log"/>;
/// a test makes any recorded step throw by adding its entry to <see cref="Failing"/>.
/// </summary>
public sealed class RecordingContext
{
    /// <summary>What ran, in order.</summary>
    public List<string> Log { get; } = [];

    /// <summary>Entries that throw instead of being recorded.</summary>
    public HashSet<string> Failing { get; } = [];

    /// <summary>Guards named here pass.</summary>
    public HashSet<string> Allow { get; } = [];

    /// <summary>What an action awaits, when a test needs a transition held open.</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The machine, for actions that enqueue or fire.</summary>
    public IMachine<byte>? Machine { get; set; }

    /// <summary>Records <paramref name="entry"/>, or throws if a test listed it in <see cref="Failing"/>.</summary>
    public void Record(string entry)
    {
        if (Failing.Contains(entry))
        {
            throw new InvalidOperationException($"{entry} failed");
        }

        Log.Add(entry);
    }

    /// <summary>The log, joined: the form order-of-operations assertions compare.</summary>
    public string Trace => string.Join(" | ", Log);
}
```

`tests/StateAlchemist.Contracts/Machines/Recording.cs`:

```csharp
namespace StateAlchemist.Contracts.Machines.Recording;

// Root ─┬─ A [Initial] ─┬─ A1 [Initial]
//       │               └─ A2
//       └─ B ────────────── B1 [Initial]

public struct Root : IRootState
{
    public int Counter;
}

[Initial]
public struct A : IState<Root>
{
    public int Value;
}

[Initial]
public struct A1 : IState<A>
{
    public int Value;
}

public struct A2 : IState<A>
{
    public int Value;
}

public struct B : IState<Root>
{
    public int Value;
}

[Initial]
public struct B1 : IState<B>
{
    public int[]? Buffer;
    public int Count;

    public void Reset() => Count = 0;
}

public readonly struct Ping : IEvent
{
    public int Amount { get; init; }
}
```

`tests/StateAlchemist.Contracts/Machines/RecorderModule.cs`:

```csharp
using System.Threading.Tasks;

namespace StateAlchemist.Contracts.Machines.Recording;

/// <summary>A machine that records every step, for the order-of-operations and data-lifetime contracts.</summary>
[Module]
public static class RecorderModule
{
    /// <summary>1: to a sibling; the shared parent stays and is written.</summary>
    [Transition(From = typeof(A1), To = typeof(A2)), On(1)]
    public static class Sibling
    {
        public static void Transform(in A1 from, ref A parent, ref A2 to)
        {
            parent.Value += 1;
            to.Value = from.Value + 10;
        }

        public static void Completed(RecordingContext context) => context.Record("completed Sibling");
    }

    /// <summary>2: to a cousin; both parents change.</summary>
    [Transition(From = typeof(A1), To = typeof(B1)), On(2)]
    public static class Cousin
    {
        public static void Transform(in A1 from, in A parent, ref Root root, ref B b, ref B1 to)
        {
            root.Counter += from.Value + parent.Value;
            b.Value = 7;
            to.Count = 1;
        }

        public static void Completed(RecordingContext context) => context.Record("completed Cousin");
    }

    /// <summary>3: a stay.</summary>
    [Transition(From = typeof(A1)), On(3)]
    public static void Stay(ref A1 self) => self.Value++;

    /// <summary>4: a re-entry: the old data in, the new data out.</summary>
    [Transition(From = typeof(A1), To = typeof(A1)), On(4)]
    public static void Restart(in A1 from, ref A1 to) => to.Value = from.Value * 100;

    /// <summary>5: declared on A, into A1 — a move from A2, a start-over from A1.</summary>
    [Transition(From = typeof(A), To = typeof(A1)), On(5)]
    public static void Home(ref A1 to) => to.Value = 50;

    /// <summary>6: to the root, which is never exited.</summary>
    [Transition(From = typeof(A), To = typeof(Root)), On(6)]
    public static void ToRoot(ref Root root) => root.Counter = -1;

    /// <summary>7: from B1 back into A.</summary>
    [Transition(From = typeof(B1), To = typeof(A)), On(7)]
    public static void Back(in B1 from)
    {
    }

    /// <summary>8: a stay that captures into a buffer B1 keeps across entries.</summary>
    [Transition(From = typeof(B1)), On(8)]
    public static void Capture(ref B1 self, byte value)
    {
        self.Buffer ??= new int[8];
        self.Buffer[self.Count++ % 8] = value;
    }

    /// <summary>9: an action that enqueues an event.</summary>
    [Transition(From = typeof(A2)), On(9)]
    public static class Echo
    {
        public static void Completed(RecordingContext context)
        {
            context.Record("completed Echo");
            context.Machine!.Enqueue(new Ping { Amount = 100 });
        }
    }

    /// <summary>10: an action that fires its own machine — a mistake the machine must catch.</summary>
    [Transition(From = typeof(A1)), On(10)]
    public static class Reenter
    {
        public static async ValueTask CompletedAsync(RecordingContext context) => await context.Machine!.FireAsync((byte)3);
    }

    /// <summary>11: an action held open until a test releases it.</summary>
    [Transition(From = typeof(A1)), On(11)]
    public static class Held
    {
        public static async ValueTask CompletedAsync(RecordingContext context)
        {
            context.Record("held");
            await context.Gate.Task;
        }
    }

    /// <summary>Ping: declared on the root, so it reaches every state.</summary>
    [Transition(From = typeof(Root)), OnEvent(typeof(Ping))]
    public static class Pinged
    {
        public static void Transform(ref Root root, in Ping ping) => root.Counter += ping.Amount;

        public static void Completed(RecordingContext context, Ping ping) => context.Record($"pinged {ping.Amount}");
    }

    [Entered(typeof(Root))]
    public static void EnterRoot(RecordingContext context) => context.Record("entered Root");

    [Entered(typeof(A))]
    public static void EnterA(RecordingContext context) => context.Record("entered A");

    [Entered(typeof(A1))]
    public static void EnterA1(RecordingContext context, A1 state) => context.Record($"entered A1 ({state.Value})");

    [Entered(typeof(A2))]
    public static void EnterA2(RecordingContext context) => context.Record("entered A2");

    [Entered(typeof(B))]
    public static void EnterB(RecordingContext context) => context.Record("entered B");

    [Entered(typeof(B1))]
    public static void EnterB1(RecordingContext context) => context.Record("entered B1");

    [Exited(typeof(Root))]
    public static void ExitRoot(RecordingContext context) => context.Record("exited Root");

    [Exited(typeof(A))]
    public static void ExitA(RecordingContext context, A state) => context.Record($"exited A ({state.Value})");

    [Exited(typeof(A1))]
    public static void ExitA1(RecordingContext context, A1 state) => context.Record($"exited A1 ({state.Value})");

    [Exited(typeof(A2))]
    public static void ExitA2(RecordingContext context) => context.Record("exited A2");

    [Exited(typeof(B))]
    public static void ExitB(RecordingContext context) => context.Record("exited B");

    [Exited(typeof(B1))]
    public static void ExitB1(RecordingContext context) => context.Record("exited B1");
}

/// <summary>A second module adding an action to a state the first owns, ordered after it.</summary>
[Module]
public static class RecorderExtras
{
    [Exited(typeof(A1), Order = 1)]
    public static void AlsoExitA1(RecordingContext context) => context.Record("exited A1 (extra)");
}
```

`tests/StateAlchemist.Contracts/Machines/Guards.cs`:

```csharp
namespace StateAlchemist.Contracts.Machines.Guards;

// GuardRoot ─┬─ Waiting [Initial]
//            └─ Chosen

public struct GuardRoot : IRootState
{
    public int Hits;
}

[Initial]
public struct Waiting : IState<GuardRoot>
{
}

public struct Chosen : IState<GuardRoot>
{
    public string? By;
}

/// <summary>Resolution order: exact before range before any, guards in Order, the unguarded one last, a child's or-else before its parent.</summary>
[Module]
public static class GuardModule
{
    [Transition(From = typeof(Waiting), To = typeof(Chosen), Order = 1), On(1)]
    public static class First
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("First");

        public static void Transform(ref Chosen to) => to.By = "First";
    }

    [Transition(From = typeof(Waiting), To = typeof(Chosen), Order = 2), On(1)]
    public static class Second
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Second");

        public static void Transform(ref Chosen to) => to.By = "Second";
    }

    [Transition(From = typeof(Waiting), To = typeof(Chosen)), On(1)]
    public static void Fallback(ref Chosen to) => to.By = "Fallback";

    [Transition(From = typeof(Waiting), To = typeof(Chosen)), OnRange(10, 19)]
    public static void Range(ref Chosen to) => to.By = "Range";

    [Transition(From = typeof(Waiting), To = typeof(Chosen)), OnAny]
    public static void Anything(ref Chosen to) => to.By = "Any";

    [Transition(From = typeof(GuardRoot)), On(42)]
    public static void RootStay(ref GuardRoot root) => root.Hits++;

    [Transition(From = typeof(Chosen), To = typeof(Waiting)), On(0)]
    public static void Again(in Chosen from)
    {
    }
}
```

`tests/StateAlchemist.Contracts/Machines/Failures.cs`:

```csharp
namespace StateAlchemist.Contracts.Machines.Failures;

// FailRoot ─┬─ Calm [Initial]
//           └─ Moved

public struct FailRoot : IRootState
{
    public int Value;
}

[Initial]
public struct Calm : IState<FailRoot>
{
    public int Value;
}

public struct Moved : IState<FailRoot>
{
}

public readonly struct Recover : IEvent
{
}

/// <summary>Every phase records, so a test can make any one of them throw.</summary>
[Module]
public static class FailureModule
{
    [Transition(From = typeof(Calm), To = typeof(Moved)), On(1)]
    public static class Go
    {
        public static bool Guard(RecordingContext context)
        {
            context.Record("guard Go");
            return true;
        }

        public static void Transform(RecordingContext context) => context.Record("transform Go");

        public static void Completed(RecordingContext context) => context.Record("completed Go");
    }

    [Transition(From = typeof(FailRoot), To = typeof(Calm)), OnEvent(typeof(Recover))]
    public static class Recovered
    {
        public static void Completed(RecordingContext context) => context.Record("recovered");
    }

    [Exited(typeof(Calm))]
    public static void LeaveCalm(RecordingContext context) => context.Record("exited Calm");

    [Entered(typeof(Moved))]
    public static void ArriveMoved(RecordingContext context) => context.Record("entered Moved");

    [Entered(typeof(Moved))]
    public static void ArriveMovedAgain(RecordingContext context) => context.Record("entered Moved again");
}
```

`tests/StateAlchemist.Contracts/Machines/Shapes.cs`:

```csharp
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Contracts.Machines;

/// <summary>The contract machines. A generated test project declares one <c>[Machine]</c> class per shape, with the same modules.</summary>
public static class Shapes
{
    public static readonly MachineShape Recorder = new("Recorder", typeof(Root), [typeof(RecorderModule), typeof(RecorderExtras)], typeof(RecordingContext));

    public static readonly MachineShape Guards = new("Guards", typeof(GuardRoot), [typeof(GuardModule)], typeof(RecordingContext));

    public static readonly MachineShape GuardsThatThrow = Guards with { Name = "GuardsThatThrow", Unhandled = Unhandled.Throw };

    public static readonly MachineShape Failures = new("Failures", typeof(FailRoot), [typeof(FailureModule)], typeof(RecordingContext));

    public static readonly MachineShape Telnet = new("SampleTelnet", typeof(Connected), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule)], typeof(TelnetContext));
}
```

- [ ] **Step 3: Build**

```bash
dotnet sln StateAlchemist.slnx add tests/StateAlchemist.Contracts/StateAlchemist.Contracts.csproj --include-references false
dotnet build
```

Expected: `0 Warning(s)`, `0 Error(s)`. The machines compiling is the declaration API's second test: a sibling
move, a cousin move, re-entry, a transition declared on a parent, an `[OnRange]` and an `[OnAny]` beside exact
values, an event transition on the root, and class-form transitions with every non-decision phase all type-check.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Contract machines: small declarations that record what they do"
```

---

### Task 3: The contract suite

The semantics, as tests. Each class enforces one concept page; each test states one sentence of it. They compile
against `IMachine<byte>` alone — which is the point: nothing here can depend on how a machine is built. No test
runs yet; Task 4 gives them an implementation.

**Files:**
- Create: `tests/StateAlchemist.Contracts/Suite/LifecycleContract.cs`, `TransitionContract.cs`,
  `ResolutionContract.cs`, `EventContract.cs`, `UnhandledContract.cs`, `ExceptionContract.cs`, `PlanContract.cs`,
  `DefinitionContract.cs`, `ConcurrencyContract.cs`, `TelnetContract.cs`

**Interfaces:**
- Consumes: Task 2's machines and `MachineContract`.
- Produces (namespace `StateAlchemist.Contracts.Suite`): ten abstract classes deriving from `MachineContract`. An
  implementation derives a sealed class from each, marked `[InheritsTests]`, overriding `Create`.

What each contract pins down, and where the docs say so:

| Contract | Enforces | Notably |
|---|---|---|
| `LifecycleContract` | `concepts/lifecycle.md` | nothing runs before `StartAsync`; `StartAsync` twice throws; stopping twice is harmless |
| `TransitionContract` | `concepts/transitions.md`, `states.md`, `actions.md` | the exact step order for sibling, cousin, stay, re-entry, parent-declared and to-root moves; leaving clears data; `Reset` keeps a buffer |
| `ResolutionContract` | `concepts/triggers.md` | guards in `Order`, unguarded last; exact › range › any per level; a leaf's `[OnAny]` shadows its parent; `[OnAny]` never swallows an event |
| `EventContract` | `concepts/triggers.md`, `actions.md` | root events reach every leaf; an enqueued event runs after its transition; `Enqueue` from outside throws |
| `UnhandledContract` | `concepts/triggers.md` | the hook sees it, then it is ignored — or `UnhandledTriggerException` with the documented message |
| `ExceptionContract` | `concepts/exceptions.md` | every row of both tables: what commits, what runs, what `Skip` and `Continue` mean per phase, queued recovery |
| `PlanContract` | `guides/testing.md` | `Plan` reports the transition, path and kind, runs guards, and changes nothing |
| `DefinitionContract` | `guides/testing.md` | states root first with parents and initial flags; transitions with kind, trigger, target, guard and context use |
| `ConcurrencyContract` | `concepts/concurrency.md` | `Checked` refuses a second caller mid-transition, and an action firing its own machine, without corrupting state |
| `TelnetContract` | `guides/getting-started.md` | the docs' own machine end to end: GMCP accepted, others refused, NAWS read, text counted, `Error` recovers |

- [ ] **Step 1: Write the lifecycle and transition contracts**

`tests/StateAlchemist.Contracts/Suite/LifecycleContract.cs`:

```csharp
using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/lifecycle.md.</summary>
public abstract class LifecycleContract : MachineContract
{
    [Test]
    public async Task AMachineIsNotRunningUntilStarted()
    {
        var context = new RecordingContext();
        var machine = Create(Shapes.Recorder, context, null);
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.NotStarted);
        await Assert.That(machine.StateType).IsEqualTo(typeof(A1));
        var refused = await Assert.ThrowsAsync<MachineNotRunningException>(async () => await machine.FireAsync((byte)3));
        await Assert.That(refused!.Status).IsEqualTo(MachineStatus.NotStarted);
        await Assert.That(context.Trace).IsEqualTo("");
    }

    [Test]
    public async Task StartingRunsTheInitialPathsEnteredActionsRootFirst()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Running);
        await Assert.That(context.Trace).IsEqualTo("entered Root | entered A | entered A1 (0)");
    }

    [Test]
    public async Task StartingTwiceIsRefused()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(async () => await machine.StartAsync()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StoppingRunsExitedActionsFromTheLeafToTheRoot()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        context.Log.Clear();
        await machine.StopAsync();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | exited A (0) | exited Root");
    }

    [Test]
    public async Task AStoppedMachineCannotBeFiredAndStoppingAgainDoesNothing()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await machine.StopAsync();
        context.Log.Clear();
        await machine.StopAsync();
        await Assert.That(context.Trace).IsEqualTo("");
        var refused = await Assert.ThrowsAsync<MachineNotRunningException>(async () => await machine.FireAsync((byte)3));
        await Assert.That(refused!.Status).IsEqualTo(MachineStatus.Stopped);
    }

    [Test]
    public async Task DisposingAnUnstartedMachineRunsNothing()
    {
        var context = new RecordingContext();
        var machine = Create(Shapes.Recorder, context, null);
        await machine.DisposeAsync();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("");
    }
}
```

`tests/StateAlchemist.Contracts/Suite/TransitionContract.cs`:

```csharp
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/transitions.md, docs/concepts/states.md and the order of docs/concepts/actions.md.</summary>
public abstract class TransitionContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(ContractHooks? hooks = null, RecordingContext? context = null)
    {
        context ??= new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context, hooks);
        context.Log.Clear();
        return (machine, context);
    }

    private static T Data<T>(IMachine<byte> machine)
        where T : struct => machine.TryGetState(out T value) ? value : throw new System.InvalidOperationException($"{typeof(T).Name} is not active");

    [Test]
    public async Task ASiblingMoveWritesTheParentAndRunsEveryStepInOrder()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)1);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | entered A2 | completed Sibling");
        await Assert.That(machine.IsIn<A2>()).IsTrue();
        await Assert.That(machine.IsIn<A>()).IsTrue();
        await Assert.That(machine.IsIn<A1>()).IsFalse();
        await Assert.That(Data<A>(machine).Value).IsEqualTo(1);
        await Assert.That(Data<A2>(machine).Value).IsEqualTo(10);
    }

    [Test]
    public async Task ACousinMoveExitsInnermostFirstAndEntersOutermostFirst()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)2);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | exited A (0) | entered B | entered B1 | completed Cousin");
        await Assert.That(Data<B>(machine).Value).IsEqualTo(7);
        await Assert.That(Data<B1>(machine).Count).IsEqualTo(1);
    }

    [Test]
    public async Task AStayRunsNoStateActionsAndKeepsItsData()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)3);
        await machine.FireAsync((byte)3);
        await Assert.That(context.Trace).IsEqualTo("");
        await Assert.That(Data<A1>(machine).Value).IsEqualTo(2);
    }

    [Test]
    public async Task AReentryReadsTheOldDataAndWritesTheNew()
    {
        var (machine, context) = await Started();
        await machine.FireAsync(new byte[] { 3, 3 });
        await machine.FireAsync((byte)4);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (2) | exited A1 (extra) | entered A1 (200)");
        await Assert.That(Data<A1>(machine).Value).IsEqualTo(200);
    }

    [Test]
    public async Task AParentDeclaredTransitionMovesFromOneLeafAndStartsTheOtherOver()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)5);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | entered A1 (50)");

        await machine.FireAsync((byte)1);
        context.Log.Clear();
        await machine.FireAsync((byte)5);
        await Assert.That(context.Trace).IsEqualTo("exited A2 | entered A1 (50)");
        await Assert.That(Data<A>(machine).Value).IsEqualTo(1);
    }

    [Test]
    public async Task AMoveToTheRootNeverExitsTheRoot()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)6);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | exited A (0) | entered A | entered A1 (0)");
        await Assert.That(Data<Root>(machine).Counter).IsEqualTo(-1);
    }

    [Test]
    public async Task LeavingAStateClearsItsData()
    {
        var (machine, _) = await Started();
        await machine.FireAsync(new byte[] { 1, 5 });
        await Assert.That(Data<A>(machine).Value).IsEqualTo(1);

        await machine.FireAsync(new byte[] { 2, 7 });
        await Assert.That(Data<A>(machine).Value).IsEqualTo(0);
        await Assert.That(Data<Root>(machine).Counter).IsEqualTo(51);
    }

    [Test]
    public async Task AStateWithResetKeepsWhatResetKeeps()
    {
        var (machine, _) = await Started();
        await machine.FireAsync(new byte[] { 2, 8, 8 });
        var first = Data<B1>(machine);
        await Assert.That(first.Count).IsEqualTo(3);

        await machine.FireAsync(new byte[] { 7, 2 });
        var second = Data<B1>(machine);
        await Assert.That(second.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(first.Buffer, second.Buffer)).IsTrue();
    }

    [Test]
    public async Task TheTransitionedHookSeesEveryTransition()
    {
        var context = new RecordingContext();
        var (machine, _) = await Started(new ContractHooks(context.Log), context);
        await machine.FireAsync(new byte[] { 3, 1 });
        await Assert.That(context.Trace).IsEqualTo(
            "transitioned RecorderModule.Stay | exited A1 (1) | exited A1 (extra) | entered A2 | completed Sibling | transitioned RecorderModule.Sibling");
    }
}
```

The trace strings are the order of operations of spec §6.2 made visible. In
`ACousinMoveExitsInnermostFirstAndEntersOutermostFirst`, `exited A1 (0) | exited A1 (extra) | exited A (0) | entered
B | entered B1 | completed Cousin` is steps 5, 6 and 7; `exited A1 (extra)` comes second because both `[Exited]`
actions on `A1` have `Order` 0 and `RecorderExtras` is included after `RecorderModule`.

- [ ] **Step 2: Write the resolution, event and unhandled contracts**

`tests/StateAlchemist.Contracts/Suite/ResolutionContract.cs`:

```csharp
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/triggers.md: which transition wins.</summary>
public abstract class ResolutionContract : MachineContract
{
    private async Task<string?> ChosenBy(byte value, params string[] allow)
    {
        var context = new RecordingContext();
        context.Allow.UnionWith(allow);
        var machine = await StartAsync(Shapes.Guards, context);
        await machine.FireAsync(value);
        return machine.TryGetState(out Chosen chosen) ? chosen.By : "(not chosen)";
    }

    [Test]
    public async Task WhenNoGuardPassesTheUnguardedTransitionRuns() => await Assert.That(await ChosenBy(1)).IsEqualTo("Fallback");

    [Test]
    public async Task GuardedTransitionsAreTriedInOrder()
    {
        await Assert.That(await ChosenBy(1, "Second")).IsEqualTo("Second");
        await Assert.That(await ChosenBy(1, "First", "Second")).IsEqualTo("First");
    }

    [Test]
    public async Task ExactBeatsRangeBeatsAny()
    {
        await Assert.That(await ChosenBy(15)).IsEqualTo("Range");
        await Assert.That(await ChosenBy(200)).IsEqualTo("Any");
    }

    [Test]
    public async Task AStatesOrElseShadowsItsParent()
    {
        await Assert.That(await ChosenBy(42)).IsEqualTo("Any");
    }

    [Test]
    public async Task WithNothingAtALevelResolutionReachesTheParent()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Guards, context);
        await machine.FireAsync(new byte[] { 1, 42 });
        await Assert.That(machine.TryGetState(out GuardRoot root) ? root.Hits : -1).IsEqualTo(1);
        await Assert.That(machine.IsIn<Chosen>()).IsTrue();
    }

    [Test]
    public async Task AFailingGuardFallsThroughToTheParentsOrElse()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 250, 31, 0, 80, 255, 240 });
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
        await Assert.That(telnet.TryGetState(out Connected root) ? root.Width : -1).IsEqualTo(0);
    }

    [Test]
    public async Task OrElseNeverSwallowsAnEvent()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 251 });
        await telnet.FireAsync(new Error());
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
        await Assert.That(context.Sent.Count).IsEqualTo(0);
    }
}
```

`tests/StateAlchemist.Contracts/Suite/EventContract.cs`:

```csharp
using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/triggers.md (events) and docs/concepts/actions.md (run to completion).</summary>
public abstract class EventContract : MachineContract
{
    [Test]
    public async Task AnEventDeclaredOnTheRootReachesEveryState()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await machine.FireAsync(new Ping { Amount = 5 });
        await machine.FireAsync((byte)1);
        await machine.FireAsync(new Ping { Amount = 2 });
        await Assert.That(machine.TryGetState(out Root root) ? root.Counter : 0).IsEqualTo(7);
    }

    [Test]
    public async Task AnEventEnqueuedByAnActionRunsAfterItsTransitionCompletes()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await machine.FireAsync((byte)1);
        context.Log.Clear();
        await machine.FireAsync((byte)9);
        await Assert.That(context.Trace).IsEqualTo("completed Echo | pinged 100");
        await Assert.That(machine.TryGetState(out Root root) ? root.Counter : 0).IsEqualTo(100);
    }

    [Test]
    public async Task EnqueueOutsideATransitionIsRefused()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(() => machine.Enqueue(new Ping())).Throws<InvalidOperationException>();
    }
}
```

`tests/StateAlchemist.Contracts/Suite/UnhandledContract.cs`:

```csharp
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Guards;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/triggers.md: unhandled triggers.</summary>
public abstract class UnhandledContract : MachineContract
{
    [Test]
    public async Task AnUnhandledValueCallsTheHookAndIsIgnored()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Guards, context, new ContractHooks(context.Log));
        await machine.FireAsync(new byte[] { 1, 99 });
        await Assert.That(context.Trace).IsEqualTo("transitioned GuardModule.Fallback | unhandled 99 in Chosen");
        await Assert.That(machine.IsIn<Chosen>()).IsTrue();
    }

    [Test]
    public async Task AMachineDeclaredUnhandledThrowThrows()
    {
        var machine = await StartAsync(Shapes.GuardsThatThrow, new RecordingContext());
        await machine.FireAsync((byte)1);
        var refused = await Assert.ThrowsAsync<UnhandledTriggerException>(async () => await machine.FireAsync((byte)99));
        await Assert.That(refused!.Message).IsEqualTo("No transition handles 99 in state 'Chosen'.");
    }
}
```

- [ ] **Step 3: Write the exception contract**

`tests/StateAlchemist.Contracts/Suite/ExceptionContract.cs`:

```csharp
using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Failures;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/exceptions.md: the defaults, and every resolution a hook can choose.</summary>
public abstract class ExceptionContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Failing(string entry, ContractHooks? hooks = null, RecordingContext? context = null)
    {
        context ??= new RecordingContext();
        context.Failing.Add(entry);
        return (await StartAsync(Shapes.Failures, context, hooks), context);
    }

    private static ContractHooks Resolving(RecordingContext context, ExceptionResolution resolution, Action<IMachine<byte>>? after = null) =>
        new(context.Log) { HandleExceptions = true, Resolution = _ => resolution, AfterException = after };

    [Test]
    public async Task AGuardThatThrowsCommitsNothing()
    {
        var (machine, context) = await Failing("guard Go");
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("");
    }

    [Test]
    public async Task ATransformThatThrowsCommitsNothingAndRunsNoAction()
    {
        var (machine, context) = await Failing("transform Go");
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go");
    }

    [Test]
    public async Task AnActionThatThrowsHasCommittedAndSkipsTheRest()
    {
        var (machine, context) = await Failing("entered Moved");
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Moved>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go | transform Go | exited Calm");
    }

    [Test]
    public async Task SkipOnAGuardCountsItAsFalse()
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("guard Go", Resolving(context, ExceptionResolution.Skip), context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("hook Guard: guard Go failed | unhandled 1 in Calm");
    }

    [Test]
    [Arguments(ExceptionResolution.Skip)]
    [Arguments(ExceptionResolution.Continue)]
    public async Task SkipOrContinueOnATransformDropsTheTrigger(ExceptionResolution resolution)
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("transform Go", Resolving(context, resolution), context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go | hook Transform: transform Go failed");
    }

    [Test]
    public async Task SkipOnAnActionSkipsTheRestAndReturnsNormally()
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("exited Calm", Resolving(context, ExceptionResolution.Skip), context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Moved>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go | transform Go | hook Exited Calm: exited Calm failed | transitioned FailureModule.Go");
    }

    [Test]
    public async Task ContinueOnAnActionRunsTheRest()
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("entered Moved", Resolving(context, ExceptionResolution.Continue), context);
        await machine.FireAsync((byte)1);
        await Assert.That(context.Trace).IsEqualTo(
            "guard Go | transform Go | exited Calm | hook Entered Moved: entered Moved failed | entered Moved again | completed Go | transitioned FailureModule.Go");
    }

    [Test]
    public async Task AHookCanQueueRecoveryThatRunsWhenTheTransitionEnds()
    {
        var context = new RecordingContext();
        var hooks = Resolving(context, ExceptionResolution.Skip, machine => machine.Enqueue(new Recover()));
        var (machine, _) = await Failing("completed Go", hooks, context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo(
            "guard Go | transform Go | exited Calm | entered Moved | entered Moved again | hook Completed: completed Go failed | transitioned FailureModule.Go | recovered | transitioned FailureModule.Recovered");
    }

    [Test]
    public async Task EventsQueuedBeforeARethrowRunBeforeTheNextTrigger()
    {
        var context = new RecordingContext();
        var hooks = Resolving(context, ExceptionResolution.Rethrow, machine => machine.Enqueue(new Recover()));
        var (machine, _) = await Failing("completed Go", hooks, context);
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Moved>()).IsTrue();

        await machine.FireAsync((byte)2);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Log.Contains("recovered")).IsTrue();
    }
}
```

Each test is one cell of the resolution table in `concepts/exceptions.md`. The last two show that a hook's queued
recovery survives either way: with `Skip` it runs as the transition ends; with `Rethrow` the exception propagates
first and the queued `Recover` runs before the next trigger.

- [ ] **Step 4: Write the plan, definition, concurrency and telnet contracts**

`tests/StateAlchemist.Contracts/Suite/PlanContract.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/guides/testing.md: the pure layer.</summary>
public abstract class PlanContract : MachineContract
{
    [Test]
    public async Task APlanSaysWhatWouldHappenWithoutDoingIt()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        context.Log.Clear();

        var plan = machine.Plan(1);

        await Assert.That(plan.Handled).IsTrue();
        await Assert.That(plan.Transition).IsEqualTo("RecorderModule.Sibling");
        await Assert.That(plan.Leaf).IsEqualTo(typeof(A1));
        await Assert.That(plan.Target).IsEqualTo(typeof(A2));
        await Assert.That(plan.Kind).IsEqualTo(TransitionKind.Move);
        await Assert.That(string.Join(",", plan.Exiting.Select(t => t.Name))).IsEqualTo("A1");
        await Assert.That(string.Join(",", plan.Entering.Select(t => t.Name))).IsEqualTo("A2");
        await Assert.That(machine.IsIn<A1>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("");
    }

    [Test]
    public async Task APlanForATriggerNothingHandlesIsNone()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(machine.Plan(99).Handled).IsFalse();
        await Assert.That(machine.Plan(new Ping()).Transition).IsEqualTo("RecorderModule.Pinged");
    }

    [Test]
    public async Task APlanEvaluatesGuards()
    {
        var context = new RecordingContext();
        context.Allow.Add("Second");
        var machine = await StartAsync(Shapes.Guards, context);
        await Assert.That(machine.Plan(1).Transition).IsEqualTo("GuardModule.Second");
    }
}
```

`tests/StateAlchemist.Contracts/Suite/DefinitionContract.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/guides/testing.md: the machine as data.</summary>
public abstract class DefinitionContract : MachineContract
{
    [Test]
    public async Task TheDefinitionListsStatesRootFirstWithTheirParents()
    {
        var definition = Create(Shapes.Recorder, new RecordingContext(), null).Definition;
        await Assert.That(definition.Root.Type).IsEqualTo(typeof(Root));
        await Assert.That(string.Join(",", definition.States.Select(s => s.Name))).IsEqualTo("Root,A,A1,A2,B,B1");
        await Assert.That(string.Join(",", definition.ChildrenOf(definition.States[definition.IndexOf(typeof(A))]).Select(s => s.Name))).IsEqualTo("A1,A2");
        await Assert.That(definition.States[definition.IndexOf(typeof(B1))].IsInitial).IsTrue();
    }

    [Test]
    public async Task TheDefinitionDescribesEachTransition()
    {
        var definition = Create(Shapes.Recorder, new RecordingContext(), null).Definition;
        var sibling = definition.Transitions.Single(t => t.Name == "RecorderModule.Sibling");
        await Assert.That(sibling.Kind).IsEqualTo(TransitionKind.Move);
        await Assert.That(sibling.Trigger.ToString()).IsEqualTo("1");
        await Assert.That(definition.States[sibling.Target].Name).IsEqualTo("A2");
        await Assert.That(definition.Transitions.Single(t => t.Name == "RecorderModule.Pinged").Trigger.ToString()).IsEqualTo("event Ping");
        await Assert.That(definition.Transitions.Single(t => t.Name == "RecorderModule.Stay").Kind).IsEqualTo(TransitionKind.Stay);
    }

    [Test]
    public async Task TheDefinitionRecordsGuardsAndContextUse()
    {
        var definition = Create(Shapes.Guards, new RecordingContext(), null).Definition;
        var first = definition.Transitions.Single(t => t.Name == "GuardModule.First");
        await Assert.That(first.HasGuard).IsTrue();
        await Assert.That(first.UsesContext).IsTrue();
        await Assert.That(definition.Transitions.Single(t => t.Name == "GuardModule.Fallback").UsesContext).IsFalse();
    }
}
```

`tests/StateAlchemist.Contracts/Suite/ConcurrencyContract.cs`:

```csharp
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/concurrency.md: a Checked machine refuses a second caller and never corrupts state.</summary>
public abstract class ConcurrencyContract : MachineContract
{
    [Test]
    public async Task ACheckedMachineRefusesASecondCallerWhileATransitionRuns()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        var held = machine.FireAsync((byte)11).AsTask();

        await Assert.That(async () => await machine.FireAsync((byte)3)).Throws<ConcurrentUseException>();

        context.Gate.SetResult();
        await held;
        await Assert.That(machine.TryGetState(out Machines.Recording.A1 a1) ? a1.Value : -1).IsEqualTo(0);
    }

    [Test]
    public async Task ACheckedMachineCatchesAnActionFiringItsOwnMachine()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(async () => await machine.FireAsync((byte)10)).Throws<ConcurrentUseException>();
    }
}
```

`tests/StateAlchemist.Contracts/Suite/TelnetContract.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/guides/getting-started.md, end to end, on the documentation's own machine.</summary>
public abstract class TelnetContract : MachineContract
{
    private static string Sent(TelnetContext context) => string.Join(" | ", context.Sent.Select(s => string.Join(",", s)));

    [Test]
    public async Task GmcpIsAcceptedAndOtherOptionsRefused()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 251, 201, 255, 251, 42 });
        await Assert.That(Sent(context)).IsEqualTo("255,253,201 | 255,254,42");
        await Assert.That(telnet.TryGetState(out Connected root) && root.GmcpEnabled).IsTrue();
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
    }

    [Test]
    public async Task NawsReadsTheWindowSize()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 250, 31, 0, 80, 0, 24, 255, 240 });
        await Assert.That(telnet.TryGetState(out Connected root) ? (root.Width, root.Height) : (0, 0)).IsEqualTo((80, 24));
        await Assert.That(string.Join(" | ", context.Log)).IsEqualTo("ready | ready | window 80x24");
    }

    [Test]
    public async Task TextCountsTheLineAndLineFeedEndsIt()
    {
        var telnet = await StartAsync(Shapes.Telnet, new TelnetContext());
        await telnet.FireAsync("hi"u8.ToArray());
        await Assert.That(telnet.TryGetState(out Idle idle) ? idle.LineLength : -1).IsEqualTo(2);
        await telnet.FireAsync((byte)'\n');
        await Assert.That(telnet.TryGetState(out idle) ? idle.LineLength : -1).IsEqualTo(0);
    }

    [Test]
    public async Task AnErrorRecoversFromAnywhere()
    {
        var telnet = await StartAsync(Shapes.Telnet, new TelnetContext());
        await telnet.FireAsync(new byte[] { 255, 250, 31, 1 });
        await Assert.That(telnet.IsIn<Naws>()).IsTrue();
        await telnet.FireAsync(new Error());
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
    }

    [Test]
    public async Task AnUnknownSubnegotiationIsAbandoned()
    {
        var telnet = await StartAsync(Shapes.Telnet, new TelnetContext());
        await telnet.FireAsync(new byte[] { 255, 250, 99 });
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
    }
}
```

`ACheckedMachineCatchesAnActionFiringItsOwnMachine` holds for `Checked` machines only, as `concepts/concurrency.md`
says: the action's nested `FireAsync` finds the machine busy and throws `ConcurrentUseException` out of the outer
call.

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: `0 Warning(s)`, `0 Error(s)`. No new tests run: abstract classes are not discovered on their own.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "The contract suite: the docs' semantics as tests against IMachine alone"
```

---

### Task 4: The reference interpreter

**Files:**
- Modify: `tests/StateAlchemist.Reference.Tests/StateAlchemist.Reference.Tests.csproj` (reference the contracts)
- Create: `tests/StateAlchemist.Reference.Tests/Contracts/ReferenceHarness.cs`, `ReferenceContracts.cs`
- Create: `tests/StateAlchemist.Reference.Tests/Interpreter/ReferenceMachineTests.cs`
- Create: `src/StateAlchemist.Reference/Interpreter/ReferenceHooks.cs`, `InvalidMachineException.cs`,
  `DefinitionBuilder.cs`, `ReferenceMachine.cs`, `ReferenceMachine.Execution.cs`
- Modify: `src/StateAlchemist/Runtime/IMachine.cs` (document the two `InvalidOperationException`s)

**Interfaces:**
- Consumes: `ReflectedMachine`, `ReflectionModelBuilder`, `MachineSpec` (Plan 1, Task 11); `Hierarchy`,
  `PathPlanner`, `Resolver` (Plan 1, Tasks 7–9); the runtime types (Plan 1, Task 4); the contract suite (Task 3).
- Produces (namespace `StateAlchemist.Reference`):
  - `sealed partial class ReferenceMachine<TValue> : IMachine<TValue> where TValue : struct` —
    `static Create(ReflectedMachine machine, object? context = null, object? config = null,
    ReferenceHooks<TValue>? hooks = null)`; throws `InvalidMachineException` when `Validate()` reports errors and
    `ArgumentException` when `TValue` is not the machine's value type.
  - `sealed class ReferenceHooks<TValue>` — `Transitioned` (`TransitionedHook(in TransitionInfo<TValue>)`),
    `UnhandledValue` (`Action<Type, TValue>`), `UnhandledEvent` (`Action<Type, object>`), `Exception`
    (`ExceptionHook(Exception, in TransitionInfo<TValue>, ref ExceptionResolution)`). `null` means not implemented.
  - `sealed class InvalidMachineException : Exception` — `Errors` (`IReadOnlyList<ModelDiagnostic>`).
  - Plan 3 extends `ReferenceMachine` with decisions, deferral, runs and the other concurrency modes.

- [ ] **Step 1: Write the harness and inherit every contract**

`tests/StateAlchemist.Reference.Tests/StateAlchemist.Reference.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net8.0;net10.0;net11.0</TargetFrameworks>
    <RootNamespace>StateAlchemist.Reference.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\StateAlchemist.Reference\StateAlchemist.Reference.csproj" />
    <ProjectReference Include="..\..\samples\StateAlchemist.Samples\StateAlchemist.Samples.csproj" />
    <ProjectReference Include="..\StateAlchemist.Contracts\StateAlchemist.Contracts.csproj" />
  </ItemGroup>
</Project>
```

`tests/StateAlchemist.Reference.Tests/Contracts/ReferenceHarness.cs`:

```csharp
using System.Collections.Concurrent;
using StateAlchemist.Contracts;

namespace StateAlchemist.Reference.Tests.Contracts;

/// <summary>Builds contract machines on the reference interpreter.</summary>
internal static class ReferenceHarness
{
    // Reflecting a machine is slow; each shape is reflected once per test run.
    private static readonly ConcurrentDictionary<MachineShape, ReflectedMachine> Reflected = new();

    public static IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks)
    {
        var machine = Reflected.GetOrAdd(shape, static s => ReflectionModelBuilder.Build(new MachineSpec(
            s.Name, s.Root, typeof(byte), s.Modules, s.Context, null, s.Concurrency, 0, s.Purity, s.Unhandled)));
        return ReferenceMachine<byte>.Create(machine, context, hooks: hooks is null ? null : Adapt(hooks));
    }

    private static ReferenceHooks<byte> Adapt(ContractHooks hooks) => new()
    {
        Transitioned = hooks.Transitioned,
        UnhandledValue = hooks.UnhandledValue,
        UnhandledEvent = hooks.UnhandledEvent,
        Exception = hooks.HandleExceptions
            ? (System.Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) =>
                resolution = hooks.Exception(exception, transition)
            : null,
    };
}
```

`tests/StateAlchemist.Reference.Tests/Contracts/ReferenceContracts.cs`:

```csharp
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Suite;
using TUnit.Core;

namespace StateAlchemist.Reference.Tests.Contracts;

// Every contract, run against the reference interpreter. A generated machine's test project has the same list.

[InheritsTests]
public sealed class ReferenceLifecycle : LifecycleContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceTransitions : TransitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceResolution : ResolutionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceEvents : EventContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceUnhandled : UnhandledContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceExceptions : ExceptionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferencePlans : PlanContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceDefinitions : DefinitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceConcurrency : ConcurrencyContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceTelnet : TelnetContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}
```

`tests/StateAlchemist.Reference.Tests/Interpreter/ReferenceMachineTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Reference.Tests.FrontEnd;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Reference.Tests.Interpreter;

/// <summary>What only the interpreter does: refuse what a generator would refuse to compile.</summary>
public class ReferenceMachineTests
{
    private static ReflectedMachine Telnet(params Type[] extra) =>
        ReflectionModelBuilder.Build(new MachineSpec(
            "Telnet", typeof(Connected), typeof(byte), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule), .. extra], typeof(TelnetContext)));

    [Test]
    public async Task AMachineWithErrorsIsRefusedWithThem()
    {
        var refused = Assert.Throws<InvalidMachineException>(() => ReferenceMachine<byte>.Create(Telnet(typeof(RivalGmcpModule)), new TelnetContext()));
        await Assert.That(string.Join(",", refused!.Errors.Select(e => e.Id))).IsEqualTo("SALCH0101");
        await Assert.That(refused.Message).StartsWith("Machine 'Telnet' has errors:\n  SALCH0101: ");
    }

    [Test]
    public async Task TheValueTypeMustBeTheMachines()
    {
        await Assert.That(() => ReferenceMachine<ushort>.Create(Telnet(), new TelnetContext())).Throws<ArgumentException>();
    }

    [Test]
    public async Task AValidMachineStartsInItsInitialLeaf()
    {
        var machine = ReferenceMachine<byte>.Create(Telnet(), new TelnetContext());
        await Assert.That(machine.StateType).IsEqualTo(typeof(Idle));
    }
}
```

The harness is the whole of what an implementation supplies: a `MachineShape` becomes a machine. Plan 4's
generated test project has the same `ReferenceContracts.cs` list with a harness that switches over shapes to the
`[Machine]` classes it declares.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build`
Expected: FAIL — `CS0246: The type or namespace name 'ReferenceMachine<>' could not be found`, and likewise
`ReferenceHooks<>` and `InvalidMachineException`.

- [ ] **Step 3: Write the hooks, the definition and the exception**

`src/StateAlchemist.Reference/Interpreter/ReferenceHooks.cs`:

```csharp
using System;

namespace StateAlchemist.Reference;

/// <summary>
/// The reference interpreter's hooks: what a generated machine's <c>partial</c> hook methods are. A hook left
/// <see langword="null"/> is a hook not implemented.
/// </summary>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public sealed class ReferenceHooks<TValue>
    where TValue : struct
{
    /// <summary>Called after a transition is observed.</summary>
    public delegate void TransitionedHook(in TransitionInfo<TValue> transition);

    /// <summary>Called when a phase throws; sets the resolution.</summary>
    public delegate void ExceptionHook(Exception exception, in TransitionInfo<TValue> transition, ref ExceptionResolution resolution);

    /// <summary>After every transition: <c>OnTransitioned</c>.</summary>
    public TransitionedHook? Transitioned { get; set; }

    /// <summary>A value nothing handled: <c>OnUnhandled(StateId, TValue)</c>.</summary>
    public Action<Type, TValue>? UnhandledValue { get; set; }

    /// <summary>An event nothing handled: <c>OnUnhandled(StateId, in TEvent)</c>.</summary>
    public Action<Type, object>? UnhandledEvent { get; set; }

    /// <summary>A phase threw: <c>On{Phase}Exception</c>. The phase is <see cref="TransitionInfo{TValue}.Phase"/>.</summary>
    public ExceptionHook? Exception { get; set; }
}
```

`src/StateAlchemist.Reference/Interpreter/InvalidMachineException.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>A machine with errors cannot be interpreted — just as it could not be generated.</summary>
public sealed class InvalidMachineException : Exception
{
    /// <summary>Creates the exception from the errors.</summary>
    public InvalidMachineException(string machine, IReadOnlyList<ModelDiagnostic> errors)
        : base($"Machine '{machine}' has errors:\n" + string.Join("\n", errors.Select(e => $"  {e.Id}: {e.Message}")))
    {
        Errors = errors;
    }

    /// <summary>The errors.</summary>
    public IReadOnlyList<ModelDiagnostic> Errors { get; }
}
```

`src/StateAlchemist.Reference/Interpreter/DefinitionBuilder.cs`:

```csharp
using System;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>Describes a reflected machine as a <see cref="MachineDefinition"/> — the same data a generated machine exposes.</summary>
internal static class DefinitionBuilder
{
    public static MachineDefinition Build(ReflectedMachine machine)
    {
        var model = machine.Model;
        var states = model.States
            .Select(s => new StateDefinition(s.Index, machine.StateTypes[s.Index], s.Parent, s.IsInitial))
            .ToList();
        var transitions = model.Transitions
            .Select(t => new TransitionDefinition(
                t.Index,
                t.Name,
                t.Source,
                t.Target,
                (TransitionKind)t.Kind,
                Trigger(t.Trigger),
                t.Order,
                t.IsGuarded,
                t.IsRun,
                new[] { t.Guard, t.Transform }.OfType<MethodModel>().Any(m => m.Parameters.Any(p => p.Kind == ParameterKind.Context)),
                t.IsDecision))
            .ToList();
        return new MachineDefinition(machine.Spec.Value!, states, transitions);
    }

    private static TriggerDefinition Trigger(TriggerModel trigger) => trigger.Kind switch
    {
        MatchKind.Value => TriggerDefinition.ForValue(trigger.Low),
        MatchKind.Range => TriggerDefinition.ForRange(trigger.Low, trigger.High),
        MatchKind.Any => TriggerDefinition.ForAny(),
        _ => TriggerDefinition.ForEvent(FindType(trigger.EventType!)),
    };

    private static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t is not null)
        ?? throw new InvalidOperationException($"Event type '{fullName}' is not loaded.");
}
```

- [ ] **Step 4: Write the interpreter**

The public surface — queries, lifecycle, firing, the event queue and plans:

`src/StateAlchemist.Reference/Interpreter/ReferenceMachine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>
/// Runs a machine by the book: the model's own <see cref="Resolver"/> picks the transition, <see cref="PathPlanner"/>
/// plans it, and every phase is invoked by reflection in the order spec §6.2 gives. Slow and allocation-heavy on
/// purpose — it exists to be obviously right, so the generated machine can be checked against it.
/// </summary>
/// <remarks>
/// One difference is known and deliberately untested. A <c>Transform</c> that edits a staying state by <c>ref</c> and
/// then throws leaves that edit in place in a generated machine (spec §6.9: not rolled back). Reflection copies
/// <c>ref</c> arguments back only when the method returns, so here the edit is lost. The contract suite never asserts
/// what a half-run transform left behind.
/// </remarks>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public sealed partial class ReferenceMachine<TValue> : IMachine<TValue>
    where TValue : struct
{
    private readonly ReflectedMachine _machine;
    private readonly MachineModel _model;
    private readonly Hierarchy _hierarchy;
    private readonly Resolver _resolver;
    private readonly object?[] _slots;
    private readonly object? _context;
    private readonly object? _config;
    private readonly ReferenceHooks<TValue> _hooks;
    private readonly Queue<object> _queue = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lazy<MachineDefinition> _definition;
    private int _leaf;
    private int _busy;
    private bool _inTransition;

    private ReferenceMachine(ReflectedMachine machine, object? context, object? config, ReferenceHooks<TValue>? hooks)
    {
        _machine = machine;
        _model = machine.Model;
        _hierarchy = new Hierarchy(_model.States);
        _resolver = new Resolver(_model, _hierarchy);
        _context = context;
        _config = config;
        _hooks = hooks ?? new ReferenceHooks<TValue>();
        _slots = machine.StateTypes.Select(t => Activator.CreateInstance(t)).ToArray();
        _leaf = _hierarchy.InitialLeaf(_hierarchy.Root);
        _definition = new Lazy<MachineDefinition>(() => DefinitionBuilder.Build(machine));
    }

    /// <summary>Interprets <paramref name="machine"/>.</summary>
    /// <exception cref="InvalidMachineException">The machine has errors.</exception>
    public static ReferenceMachine<TValue> Create(ReflectedMachine machine, object? context = null, object? config = null, ReferenceHooks<TValue>? hooks = null)
    {
        if (machine.Spec.Value != typeof(TValue))
        {
            throw new ArgumentException($"Machine '{machine.Model.Name}' fires {machine.Spec.Value?.Name}, not {typeof(TValue).Name}.", nameof(machine));
        }

        var errors = machine.Validate().Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidMachineException(machine.Model.Name, errors);
        }

        return new ReferenceMachine<TValue>(machine, context, config, hooks);
    }

    /// <inheritdoc/>
    public MachineDefinition Definition => _definition.Value;

    /// <inheritdoc/>
    public MachineStatus Status { get; private set; } = MachineStatus.NotStarted;

    /// <inheritdoc/>
    public Type StateType => _machine.StateTypes[_leaf];

    /// <inheritdoc/>
    public bool IsIn<TState>()
        where TState : struct =>
        IndexOf(typeof(TState)) is var index && index >= 0 && _hierarchy.IsAncestorOrSelf(index, _leaf);

    /// <inheritdoc/>
    public bool TryGetState<TState>(out TState value)
        where TState : struct
    {
        if (IsIn<TState>())
        {
            value = (TState)_slots[IndexOf(typeof(TState))]!;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask StartAsync()
    {
        if (Status != MachineStatus.NotStarted)
        {
            throw new InvalidOperationException("The machine has already been started.");
        }

        foreach (var state in _hierarchy.PathFromRoot(_leaf))
        {
            await RunStateActionsAsync(ActionPhase.Entered, state, Lifecycle(Phase.Entered, state), snapshots: null);
        }

        Status = MachineStatus.Running;
    }

    /// <inheritdoc/>
    public async ValueTask StopAsync()
    {
        if (Status != MachineStatus.Running)
        {
            Status = MachineStatus.Stopped;
            return;
        }

        Status = MachineStatus.Stopped;
        _lifetime.Cancel();
        var path = _hierarchy.PathFromRoot(_leaf);
        for (var i = path.Count - 1; i >= 0; i--)
        {
            await RunStateActionsAsync(ActionPhase.Exited, path[i], Lifecycle(Phase.Exited, path[i]), snapshots: null);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => StopAsync();

    /// <inheritdoc/>
    public ValueTask FireAsync(TValue value) => GuardedAsync(() => FireValueAsync(value));

    /// <inheritdoc/>
    public ValueTask FireAsync(ReadOnlyMemory<TValue> values) => GuardedAsync(async () =>
    {
        for (var i = 0; i < values.Length; i++)
        {
            await FireValueAsync(values.Span[i]);
        }
    });

    /// <inheritdoc/>
    public ValueTask FireAsync<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        GuardedAsync(() => FireEventAsync(e));

    /// <inheritdoc/>
    public void Enqueue<TEvent>(TEvent e)
        where TEvent : struct, IEvent
    {
        if (!_inTransition)
        {
            throw new InvalidOperationException("Enqueue is for code running inside a transition, such as an action or a hook. From outside, use FireAsync.");
        }

        _queue.Enqueue(e);
    }

    /// <inheritdoc/>
    public TransitionPlan Plan(TValue value) => PlanFor(_resolver.ForValue(_leaf, ToInt64(value)), Trigger.OfValue(value));

    /// <inheritdoc/>
    public TransitionPlan Plan<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        PlanFor(_resolver.ForEvent(_leaf, typeof(TEvent).FullName!), Trigger.OfEvent(e));

    private async ValueTask GuardedAsync(Func<ValueTask> fire)
    {
        if (Status != MachineStatus.Running)
        {
            throw new MachineNotRunningException(Status);
        }

        var checkedMode = _model.Options.Concurrency == ConcurrencyMode.Checked;
        if (checkedMode && Interlocked.Exchange(ref _busy, 1) != 0)
        {
            throw new ConcurrentUseException();
        }

        try
        {
            await fire();
        }
        finally
        {
            if (checkedMode)
            {
                Volatile.Write(ref _busy, 0);
            }
        }
    }

    private ValueTask FireValueAsync(TValue value) => ProcessAsync(Trigger.OfValue(value));

    private ValueTask FireEventAsync(object e) => ProcessAsync(Trigger.OfEvent(e));

    /// <summary>
    /// One trigger, then step 9: the events its transition queued. Events kept from a transition that threw run first,
    /// so queued events always run in the order they were queued.
    /// </summary>
    private async ValueTask ProcessAsync(Trigger trigger)
    {
        await DrainAsync();
        await RunOneAsync(Candidates(trigger), trigger);
        await DrainAsync();
    }

    private async ValueTask DrainAsync()
    {
        while (_queue.Count > 0)
        {
            var queued = Trigger.OfEvent(_queue.Dequeue());
            await RunOneAsync(Candidates(queued), queued);
        }
    }

    private IReadOnlyList<TransitionModel> Candidates(Trigger trigger) => trigger.HasValue
        ? _resolver.ForValue(_leaf, ToInt64(trigger.Value))
        : _resolver.ForEvent(_leaf, trigger.Event!.GetType().FullName!);

    private int IndexOf(Type stateType)
    {
        for (var i = 0; i < _machine.StateTypes.Count; i++)
        {
            if (_machine.StateTypes[i] == stateType)
            {
                return i;
            }
        }

        return -1;
    }

    private static long ToInt64(TValue value) => Convert.ToInt64(value);

    private TransitionInfo<TValue> Lifecycle(Phase phase, int state) =>
        new("(lifecycle)", _machine.StateTypes[_hierarchy.Root], StateType, StateType, TransitionKind.Stay, phase, default, false, null, _machine.StateTypes[state]);

    /// <summary>What fired: a value or an event.</summary>
    private readonly record struct Trigger(TValue Value, bool HasValue, object? Event)
    {
        public static Trigger OfValue(TValue value) => new(value, true, null);

        public static Trigger OfEvent(object e) => new(default, false, e);

        public override string ToString() => HasValue ? Value.ToString()! : "event " + Event!.GetType().Name;
    }
}
```

How it works, in the order a trigger meets it:

- **Guarding.** `FireAsync` refuses a machine that is not running, then — in `Checked` mode — takes the busy flag
  with `Interlocked.Exchange`; a second caller finds it set and gets `ConcurrentUseException`. A batch takes it
  once for the whole batch.
- **Processing.** Events queued earlier run first (they may be left over from a transition that threw), then the
  trigger, then whatever it queued.
- **Enqueue** works only while a transition runs — from an action or a hook — and throws otherwise.

The execution — spec §6.2 steps 1 to 8, one comment per step:

`src/StateAlchemist.Reference/Interpreter/ReferenceMachine.Execution.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

public sealed partial class ReferenceMachine<TValue>
{
    private enum Use
    {
        Guard,
        Transform,
        Completed,
        Exited,
        Entered,
    }

    private async ValueTask RunOneAsync(IReadOnlyList<TransitionModel> candidates, Trigger trigger)
    {
        _inTransition = true;
        try
        {
            var chosen = Choose(candidates, trigger, hooks: true);
            if (chosen is null)
            {
                Unhandled(trigger);
                return;
            }

            if (chosen.IsDecision)
            {
                throw new NotSupportedException("Decisions are interpreted from Plan 3.");
            }

            await ExecuteAsync(chosen, trigger);
        }
        finally
        {
            _inTransition = false;
        }
    }

    /// <summary>Step 1: the first candidate whose guard passes.</summary>
    private TransitionModel? Choose(IReadOnlyList<TransitionModel> candidates, Trigger trigger, bool hooks)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Guard is not { } guard)
            {
                return candidate;
            }

            var path = PathPlanner.Plan(_hierarchy, candidate, _leaf);
            var info = Info(candidate, path, trigger, Phase.Guard);
            try
            {
                if ((bool)Invoke(guard, Bind(guard, Use.Guard, path, trigger, info, snapshots: null))!)
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (hooks)
            {
                if (Resolve(exception, info) == ExceptionResolution.Rethrow)
                {
                    ExceptionDispatchInfo.Capture(exception).Throw();
                }
            }
        }

        return null;
    }

    /// <summary>Steps 2–8 of spec §6.2.</summary>
    private async ValueTask ExecuteAsync(TransitionModel transition, Trigger trigger)
    {
        var path = PathPlanner.Plan(_hierarchy, transition, _leaf);
        var startedOver = new HashSet<int>(path.Exiting.Intersect(path.Entering));
        var snapshots = startedOver.ToDictionary(state => state, state => RuntimeHelpers.GetObjectValue(_slots[state]));

        // 2. reset the entering states
        foreach (var state in path.Entering)
        {
            _slots[state] = Cleared(state);
        }

        // 3. transform
        var info = Info(transition, path, trigger, Phase.Transform);
        if (transition.Transform is { } transform)
        {
            var arguments = Bind(transform, Use.Transform, path, trigger, info, snapshots);
            try
            {
                Invoke(transform, arguments);
            }
            catch (Exception exception)
            {
                // Nothing commits. A state being started over was reset in step 2 while still active: put it back.
                foreach (var (state, snapshot) in snapshots)
                {
                    _slots[state] = snapshot;
                }

                if (Resolve(exception, info) == ExceptionResolution.Rethrow)
                {
                    ExceptionDispatchInfo.Capture(exception).Throw();
                }

                return;
            }

            WriteBack(transform, arguments);
        }

        // 4. commit
        _leaf = path.TargetLeaf;
        try
        {
            // 5. exited, leaf first; 6. entered, outermost first; 7. completed
            var go = true;
            foreach (var state in path.Exiting)
            {
                go = go && await RunStateActionsAsync(ActionPhase.Exited, state, info.With(Phase.Exited, _machine.StateTypes[state]), snapshots);
            }

            foreach (var state in path.Entering)
            {
                go = go && await RunStateActionsAsync(ActionPhase.Entered, state, info.With(Phase.Entered, _machine.StateTypes[state]), snapshots);
            }

            foreach (var completed in transition.Completed)
            {
                go = go && await RunActionAsync(completed, Use.Completed, path, trigger, info.With(Phase.Completed), snapshots);
            }
        }
        finally
        {
            // 8. clear the states left (a state started over now holds its new data)
            foreach (var state in path.Exiting.Where(s => !startedOver.Contains(s)))
            {
                _slots[state] = Cleared(state);
            }
        }

        _hooks.Transitioned?.Invoke(info.With(Phase.Completed));
    }

    private async ValueTask<bool> RunStateActionsAsync(ActionPhase phase, int state, TransitionInfo<TValue> info, IReadOnlyDictionary<int, object?>? snapshots)
    {
        var actions = _model.StateActions
            .Where(a => a.Phase == phase && a.State == state)
            .OrderBy(a => a.Order)
            .ThenBy(a => ModuleOrder(a.Module))
            .ThenBy(a => a.DeclarationIndex);
        foreach (var action in actions)
        {
            var use = phase == ActionPhase.Exited ? Use.Exited : Use.Entered;
            if (!await RunActionAsync(action.Method, use, PathPlanner.Stay(_leaf), default, info, snapshots))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Runs one action; false means "skip the rest" (<see cref="ExceptionResolution.Skip"/>).</summary>
    private async ValueTask<bool> RunActionAsync(MethodModel method, Use use, TransitionPath path, Trigger trigger, TransitionInfo<TValue> info, IReadOnlyDictionary<int, object?>? snapshots)
    {
        try
        {
            var result = Invoke(method, Bind(method, use, path, trigger, info, snapshots));
            switch (result)
            {
                case ValueTask pending:
                    await pending;
                    break;
                case Task pending:
                    await pending;
                    break;
            }

            return true;
        }
        catch (Exception exception)
        {
            switch (Resolve(exception, info))
            {
                case ExceptionResolution.Continue:
                    return true;
                case ExceptionResolution.Skip:
                    return false;
                default:
                    ExceptionDispatchInfo.Capture(exception).Throw();
                    return false;
            }
        }
    }

    private object?[] Bind(MethodModel method, Use use, TransitionPath path, Trigger trigger, TransitionInfo<TValue> info, IReadOnlyDictionary<int, object?>? snapshots)
    {
        var arguments = new object?[method.Parameters.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = method.Parameters[i];
            arguments[i] = parameter.Kind switch
            {
                ParameterKind.State => StateArgument(parameter, use, snapshots),
                ParameterKind.Value => trigger.Value,
                ParameterKind.Event => trigger.Event,
                ParameterKind.Config => _config,
                ParameterKind.Context => _context,
                ParameterKind.CancellationToken => _lifetime.Token,
                ParameterKind.TransitionInfo => info,
                _ => throw new NotSupportedException($"Parameter kind {parameter.Kind} is interpreted from Plan 3."),
            };
        }

        return arguments;
    }

    /// <summary>
    /// Which copy of a state a parameter sees: a state being started over is read, before the change, from its
    /// snapshot — by a transform's <c>in</c> parameter, and by that state's own <c>[Exited]</c> actions. Everything
    /// else reads the live slot.
    /// </summary>
    private object? StateArgument(ParameterModel parameter, Use use, IReadOnlyDictionary<int, object?>? snapshots)
    {
        var oldData = (use == Use.Transform && parameter.Passing == Passing.In) || use == Use.Exited;
        return oldData && snapshots is not null && snapshots.TryGetValue(parameter.State, out var snapshot)
            ? RuntimeHelpers.GetObjectValue(snapshot)
            : _slots[parameter.State];
    }

    private void WriteBack(MethodModel method, object?[] arguments)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            if (method.Parameters[i] is { Kind: ParameterKind.State, Passing: Passing.Ref } parameter)
            {
                _slots[parameter.State] = arguments[i];
            }
        }
    }

    private object? Invoke(MethodModel method, object?[] arguments) =>
        _machine.MethodOf(method).Invoke(null, BindingFlags.DoNotWrapExceptions, null, arguments, null);

    /// <summary>A state's data cleared: <c>Reset()</c> on a copy if the state declares it, otherwise <c>default</c>.</summary>
    private object? Cleared(int state)
    {
        var type = _machine.StateTypes[state];
        if (!_model.States[state].HasReset)
        {
            return Activator.CreateInstance(type);
        }

        var copy = RuntimeHelpers.GetObjectValue(_slots[state]);
        type.GetMethod("Reset", Type.EmptyTypes)!.Invoke(copy, null);
        return copy;
    }

    private ExceptionResolution Resolve(Exception exception, TransitionInfo<TValue> info)
    {
        var resolution = ExceptionResolution.Rethrow;
        _hooks.Exception?.Invoke(exception, in info, ref resolution);
        return resolution;
    }

    private void Unhandled(Trigger trigger)
    {
        if (trigger.HasValue)
        {
            _hooks.UnhandledValue?.Invoke(StateType, trigger.Value);
        }
        else
        {
            _hooks.UnhandledEvent?.Invoke(StateType, trigger.Event!);
        }

        if (_model.Options.Unhandled == UnhandledMode.Throw)
        {
            throw new UnhandledTriggerException(StateType, trigger.ToString());
        }
    }

    private TransitionPlan PlanFor(IReadOnlyList<TransitionModel> candidates, Trigger trigger)
    {
        var chosen = Choose(candidates, trigger, hooks: false);
        if (chosen is null)
        {
            return TransitionPlan.None;
        }

        var path = PathPlanner.Plan(_hierarchy, chosen, _leaf);
        return new TransitionPlan(
            chosen.Name,
            _machine.StateTypes[path.Leaf],
            _machine.StateTypes[path.TargetLeaf],
            (TransitionKind)chosen.Kind,
            path.Exiting.Select(s => _machine.StateTypes[s]).ToList(),
            path.Entering.Select(s => _machine.StateTypes[s]).ToList(),
            chosen.IsDecision);
    }

    private TransitionInfo<TValue> Info(TransitionModel transition, TransitionPath path, Trigger trigger, Phase phase) =>
        new(transition.Name,
            _machine.StateTypes[transition.Source],
            _machine.StateTypes[path.Leaf],
            _machine.StateTypes[path.TargetLeaf],
            (TransitionKind)transition.Kind,
            phase,
            trigger.Value,
            trigger.HasValue,
            trigger.Event?.GetType(),
            null);

    private int ModuleOrder(string module)
    {
        for (var i = 0; i < _machine.Spec.Modules.Count; i++)
        {
            if (_machine.Spec.Modules[i].FullName == module)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
```

Three details carry the semantics:

- **Snapshots.** A state being started over is both exiting and entering. Step 2 resets its slot, so its old data is
  copied first; the transform's `in` parameter and the state's own `[Exited]` actions read the copy. If the
  transform throws, the copy goes back: nothing commits.
- **`ref` parameters.** Reflection boxes a struct argument; `WriteBack` copies each `ref` state back into its slot
  after the transform returns. That is also the interpreter's one known difference from a generated machine,
  written into the class's remarks: a transform that throws after editing a staying state loses that edit here.
  The contracts never test a half-run transform's leftovers.
- **Skip.** An action's `Skip` ends the transition's remaining actions (`go` turns false) but still runs step 8 and
  the `Transitioned` hook; `Rethrow` runs step 8 (the `finally`) and propagates.

The two runtime behaviours the interpreter settled are documented on the interface:

`src/StateAlchemist/Runtime/IMachine.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace StateAlchemist;

/// <summary>
/// What every machine does, generated or not. The generated class adds strongly typed members (a <c>StateId</c>
/// enum, one <c>FireAsync(in TEvent)</c> per event, <c>TryGet{State}</c> per state); this interface is what tests and
/// hosts that should not depend on one machine's shape program against.
/// </summary>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public interface IMachine<TValue> : IAsyncDisposable
    where TValue : struct
{
    /// <summary>The machine described as data.</summary>
    MachineDefinition Definition { get; }

    /// <summary>Not started, running, or stopped.</summary>
    MachineStatus Status { get; }

    /// <summary>The active leaf's state type.</summary>
    Type StateType { get; }

    /// <summary>Whether <typeparamref name="TState"/> is the active leaf or one of its ancestors.</summary>
    /// <typeparam name="TState">A state struct.</typeparam>
    bool IsIn<TState>()
        where TState : struct;

    /// <summary>Copies an active state's data.</summary>
    /// <typeparam name="TState">A state struct.</typeparam>
    /// <param name="value">The data, when the state is active.</param>
    /// <returns>Whether the state is active.</returns>
    bool TryGetState<TState>(out TState value)
        where TState : struct;

    /// <summary>Runs the initial path's <c>[Entered]</c> actions, root first. Call once, before firing.</summary>
    /// <exception cref="InvalidOperationException">The machine has already been started.</exception>
    ValueTask StartAsync();

    /// <summary>Cancels a pending decision and runs <c>[Exited]</c> actions from the leaf to the root.</summary>
    ValueTask StopAsync();

    /// <summary>Fires one value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Completes when the value has been processed, including waiting for any decision it started.</returns>
    /// <exception cref="MachineNotRunningException">The machine is not running, or was stopped while waiting on a decision.</exception>
    ValueTask FireAsync(TValue value);

    /// <summary>Fires values in order, consuming runs of values in one call.</summary>
    /// <param name="values">The values. The machine holds them until the returned task completes; nothing is copied.</param>
    /// <returns>
    /// Completes when every value has been processed, including waiting for any decision one of them started — so a
    /// read loop that awaits it stops reading while a decision is pending, which is the backpressure.
    /// </returns>
    /// <exception cref="MachineNotRunningException">The machine is not running, or was stopped while waiting on a decision.</exception>
    ValueTask FireAsync(ReadOnlyMemory<TValue> values);

    /// <summary>
    /// Fires an event. While a decision is pending, the machine accepts events from any caller: one the pending state
    /// handles is processed at once, and any other is queued until the decision resolves.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="e">The event.</param>
    ValueTask FireAsync<TEvent>(TEvent e)
        where TEvent : struct, IEvent;

    /// <summary>Queues an event to run after the current transition, for recovery from hooks and actions.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="e">The event.</param>
    /// <exception cref="InvalidOperationException">
    /// Called from outside a transition. From outside the machine, use <see cref="FireAsync{TEvent}(TEvent)"/>.
    /// </exception>
    void Enqueue<TEvent>(TEvent e)
        where TEvent : struct, IEvent;

    /// <summary>What <paramref name="value"/> would do now, evaluating guards, without doing it.</summary>
    /// <param name="value">The value.</param>
    TransitionPlan Plan(TValue value);

    /// <summary>What <paramref name="e"/> would do now, evaluating guards, without doing it.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="e">The event.</param>
    TransitionPlan Plan<TEvent>(TEvent e)
        where TEvent : struct, IEvent;
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: PASS — 507 tests on three frameworks: 68 model, 35 runtime, and 66 reference tests (13 front-end, 3
interpreter, 50 contracts) per framework. The public API snapshot is unchanged: XML documentation is not part of it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Reference interpreter: the contract suite passes on reflection over the model"
```

---

## Findings from validating Plan 2

The contracts found four places where the docs were silent or wrong. All four are fixed in the spec and docs in the
commit that adds this plan; Task 1 is the one code change they needed.

- **A state being started over must be restored if the transform throws.** Spec §6.2 said step 2 resets only
  inactive slots. A started-over state's slot is active: it is reset while the machine still sits in it. The spec
  now says step 2 snapshots it first, and §6.9 says the snapshot goes back when nothing commits.
- **`SALCH0301` is wrong for the root.** Re-entering the root clears nothing. Task 1.
- **`Enqueue` from outside a transition** had no defined behaviour. It throws `InvalidOperationException`: from
  outside, `FireAsync` is the call, and a queue nobody drains would lose the event silently.
- **`StartAsync` twice** had no defined behaviour. It throws `InvalidOperationException`: running the initial
  `[Entered]` actions again would repeat the server's opening offers.
- **Queued events after a rethrow** are kept, as documented; the docs now say *when* they run: before the next
  trigger.

## Plan 2 exit gate

Before Plan 3 starts:

- `dotnet test --solution StateAlchemist.slnx` passes on net8.0, net10.0 and net11.0 (507 tests when this plan was
  validated), and CI is green.
- Read every contract against its doc page. Each concept page should have its rules covered, and each test should
  read as a sentence of the page. Where a page makes a claim no test checks, add the test; where a test checks
  something no page says, fix the page.
- Review `TelnetContract` with TNC in mind: the sample must be able to express every pattern TNC's interpreter uses
  — option negotiation, subnegotiation capture, recovery from `Error` — before the generator is built to match it.

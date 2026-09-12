# StateAlchemist Plan 3 — Decisions, Deferral, Runs and Concurrency, Interpreted — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pin down the hardest semantics — async decisions and their pending state, invisible deferral and
backpressure, run transitions, and the three concurrency modes — as contract tests, and make the reference
interpreter pass them, before any generator exists.

**Architecture:** The interpreter's core changes shape. Plan 2 ran each `FireAsync` inline on its caller; now every
`FireAsync` is an *input* placed in an inbox, and whoever finds the machine idle becomes the *pump* and runs one
trigger at a time — the design spec §6.10 gives the generated `Serialized` machine, applied to every mode, with the
modes differing only in what they refuse. That one mechanism is what lets a decision pause the input that started
it while the machine stays idle and accepts events. Transition execution (spec §6.2) is Plan 2's, generalised so a
decision outcome's `Complete` runs as a move's transform.

**Tech Stack:** as Plans 1–2, plus System.IO.Pipelines 10.0.12 in the contract library (for a real `Pipe`).

**Spec:** [`docs/superpowers/specs/2026-09-11-statealchemist-design.md`](../specs/2026-09-11-statealchemist-design.md)
— §5.6 and §6.5 (decisions), §6.6 (deferral), §6.7 (runs), §6.9 (exceptions), §6.10 (threading). The roadmap:
[`2026-09-11-00-roadmap.md`](2026-09-11-00-roadmap.md). Plan 2's contract suite must keep passing unchanged.

> **Validated (2026-09-11):** built on top of Plans 1–2's validated tree and run on net8.0, net10.0 and net11.0 —
> 597 tests, all passing, and the reference test project run 15 more times in a row without a failure, to shake out
> races in the new concurrency code. The code below is that code. Design findings are already in the spec and docs
> (see [Findings](#findings-from-validating-plan-3)).

## Global Constraints

Plans 1–2's constraints hold. In addition:

- **Contract tests never wait on something that may not happen.** A test that waits for a decision to start also
  watches the `FireAsync` that should start it, so a broken implementation fails the test instead of hanging the
  run. Tests that check something has *not* happened check a condition that cannot become true until the test
  releases it (a gate, an answer), never a timing.
- **Outcome unions use C# 15 `union` declarations**, compiled on every target: below .NET 11 the contract library
  declares `UnionAttribute` and `IUnion` internally, exactly as a declaring library targeting `net8.0` would.
- **No `System.Threading.Channels` in the interpreter.** Its inbox is a locked list, which is easier to read and
  lets it pick a handled event out of order.

## File structure

```
src/StateAlchemist/Events/DecisionFailed.cs         new: the runtime event a failed decision fires
src/StateAlchemist.Reference/Interpreter/
    ReferenceMachine.cs            public surface; FireAsync submits an input; Enqueue by where it is called from
    ReferenceMachine.Inbox.cs      new: inputs, the pump, the modes' refusals, failure and stopping
    ReferenceMachine.Decisions.cs  new: sync and async decisions, the pending state, outcomes, DecisionFailed
    ReferenceMachine.Runs.cs       new: finding a run, and calling a transform that takes a span
    ReferenceMachine.Execution.cs  a trigger resolved and run; §6.2 steps, now also for decision outcomes
tests/StateAlchemist.Contracts/
    Machines/  UnionPolyfill, Deciding (states, events, outcomes), DecidingModule, Runs; RecordingContext and
               Shapes extended
    Suite/     DecisionContract, BackpressureContract, RunContract, SerializedContract (new);
               ConcurrencyContract extended
```

Modified besides: `RoleValidator` and its tests (Task 1), the API snapshot (Task 2), `IMachine.cs` XML docs and
`ReferenceContracts.cs` (Task 5), `Directory.Packages.props` and the contracts project (Task 3).

---

### Task 1: Decisions are role-checked the way they run

Plan 1 checked a decision as if it were a stay: fine for its `Guard`, wrong for the rest. Its `Decide` was never
checked at all, so `ref` to a state compiled into a decision that could never honour it. And its `Completed`
overloads were planned as a stay, so a `Completed` naming the state its outcome moves to was refused (`SALCH0202`)
— the contract machine in Task 3 does exactly that, which is how validation found it.

**Files:**
- Modify: `src/StateAlchemist.Model/Analysis/RoleValidator.cs`
- Modify: `tests/StateAlchemist.Model.Tests/TestModel.cs` (`Copy`, `Outcome`, `Decision` helpers)
- Modify: `tests/StateAlchemist.Model.Tests/RoleValidatorTests.cs` (three tests)

**Interfaces:**
- Consumes: `RoleValidator`, `PathPlanner`, `Roles` (Plan 1).
- Produces: the rule, as the class summary states it. `Decide` reads the active path, which stays, and takes
  states by value or `in` (SALCH0204 otherwise: "a decision reads states: take it by value or as in"). Each
  `Complete` is the transform of a move from every leaf under the source to its own target. A `Completed` taking an
  outcome is checked after that outcome's move; one taking none, after every outcome's move — so it must bind the
  same way after each.
- `Check` now takes the planned paths rather than leaves and a planning function, so one method checks both
  "every leaf" and "every leaf, after every outcome".

- [ ] **Step 1: Write the failing tests**

`tests/StateAlchemist.Model.Tests/TestModel.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model.Tests;

/// <summary>Builds small models by hand, so each test states exactly the tree and transitions it is about.</summary>
internal sealed class TestModel
{
    private readonly List<StateModel> _states = [];
    private readonly List<TransitionModel> _transitions = [];
    private readonly List<StateActionModel> _actions = [];

    public ValueDomain Domain { get; set; } = ValueDomain.Byte;

    public PurityMode Purity { get; set; } = PurityMode.Permissive;

    public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Checked;

    public int Root(string name = "Root", bool hasData = false) => AddState(name, -1, false, hasData);

    public int State(string name, int parent, bool initial = false, bool hasData = false) => AddState(name, parent, initial, hasData);

    public string TypeOf(int state) => _states[state].TypeName;

    public ParameterModel In(int state) => new(_states[state].Name.ToLowerInvariant(), _states[state].TypeName, ParameterKind.State, Passing.In, state);

    public ParameterModel Copy(int state) => new(_states[state].Name.ToLowerInvariant(), _states[state].TypeName, ParameterKind.State, Passing.Value, state);

    public ParameterModel Ref(int state) => new(_states[state].Name.ToLowerInvariant(), _states[state].TypeName, ParameterKind.State, Passing.Ref, state);

    public static ParameterModel Outcome(string typeName) => new("outcome", typeName, ParameterKind.Outcome, Passing.Value);

    public static ParameterModel Value() => new("value", "System.Byte", ParameterKind.Value, Passing.Value);

    public static MethodModel Method(string declaringType, string name, ReturnShape returns, params ParameterModel[] parameters) =>
        new(declaringType, name, returns, IsStatic: true, IsPublic: true, parameters, SourceSpan.None);

    /// <summary>A method-form transition: <paramref name="to"/> −1 is a stay.</summary>
    public TransitionModel Add(string name, int from, int to, TriggerModel trigger, params ParameterModel[] transform) =>
        Add(name, from, to, trigger, guarded: false, order: 0, transform);

    public TransitionModel Add(string name, int from, int to, TriggerModel trigger, bool guarded, int order, params ParameterModel[] transform)
    {
        var transition = new TransitionModel(
            _transitions.Count, name, from, to, trigger, order, IsRun: false,
            guarded ? Method(name, "Guard", ReturnShape.Bool) : null,
            Method(name, "Transform", ReturnShape.Void, transform),
            [], null, [], "T.Module", SourceSpan.None);
        _transitions.Add(transition);
        return transition;
    }

    /// <summary>
    /// An async decision from <paramref name="from"/> on value 5 with outcomes <c>T.Accept</c> and <c>T.Reject</c>,
    /// completed to <paramref name="acceptTo"/> and <paramref name="rejectTo"/>.
    /// </summary>
    public TransitionModel Decision(string name, int from, int acceptTo, int rejectTo, ParameterModel[] decide, ParameterModel[] accept, ParameterModel[] reject, params MethodModel[] completed)
    {
        var decision = new DecisionModel(
            null,
            Method(name, "DecideAsync", ReturnShape.ValueTaskOfResult, decide),
            ["T.Accept", "T.Reject"],
            [
                new OutcomeCompletion("T.Accept", acceptTo, Method(name, "Complete", ReturnShape.Void, [.. accept, Outcome("T.Accept")])),
                new OutcomeCompletion("T.Reject", rejectTo, Method(name, "Complete", ReturnShape.Void, [.. reject, Outcome("T.Reject")])),
            ],
            []);
        return Add(new TransitionModel(0, name, from, -1, TriggerModel.Value(5), 0, IsRun: false, null, null, completed, decision, [], "T.Module", SourceSpan.None));
    }

    /// <summary>Adds a transition built by the caller, renumbered to its position.</summary>
    public TransitionModel Add(TransitionModel transition)
    {
        var numbered = transition with { Index = _transitions.Count };
        _transitions.Add(numbered);
        return numbered;
    }

    public void Action(ActionPhase phase, int state, string module, int order, params ParameterModel[] parameters) =>
        _actions.Add(new StateActionModel(phase, state, order, module, _actions.Count, Method(module, "On" + phase, ReturnShape.Void, parameters)));

    public void Replace(int state, StateModel replacement) => _states[state] = replacement;

    public MachineModel Build(string name = "TestMachine") =>
        new(name, new MachineOptions("System.Byte", Domain, Concurrency: Concurrency, Purity: Purity), _states.ToList(), _transitions.ToList(), _actions.ToList());

    private int AddState(string name, int parent, bool initial, bool hasData)
    {
        _states.Add(new StateModel(_states.Count, "T." + name, parent, initial, hasData, HasReset: false, IsPublicStruct: true, ParentMarkers: 1, SourceSpan.None));
        return _states.Count - 1;
    }
}
```

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
    public async Task ADecisionReadsThePathAndEachOutcomeMovesToItsOwnTarget()
    {
        _model.Decision("Check", _awaiting, _naws, _idle,
            decide: [_model.Copy(_sub)],
            accept: [_model.In(_awaiting), _model.Ref(_sub), _model.Ref(_naws)],
            reject: [_model.In(_awaiting), _model.In(_sub)],
            TestModel.Method("Check", "Completed", ReturnShape.Void, _model.Copy(_naws), TestModel.Outcome("T.Accept")),
            TestModel.Method("Check", "Completed", ReturnShape.Void, _model.Copy(_root)));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task ACompletedWithoutAnOutcomeMustBindAfterEveryOutcome()
    {
        _model.Decision("Check", _awaiting, _naws, _idle, [], [], [],
            TestModel.Method("Check", "Completed", ReturnShape.Void, _model.Copy(_naws)));
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0202: Parameter 'naws' of 'Check.Completed' names 'Naws', which is not touched from every leaf this transition fires from");
    }

    [Test]
    public async Task ADecisionOnlyReadsStates()
    {
        _model.Decision("Check", _awaiting, _naws, _idle, [_model.Ref(_sub)], [], []);
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0204: Parameter 'sub' of 'Check.DecideAsync' cannot be bound: a decision reads states: take it by value or as in");
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

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: FAIL, three tests:
- `ADecisionReadsThePathAndEachOutcomeMovesToItsOwnTarget` and `ACompletedWithoutAnOutcomeMustBindAfterEveryOutcome`
  receive `"SALCH0202: Parameter 'naws' of 'Check.Completed' names 'Naws', which has no role in this transition"`.
- `ADecisionOnlyReadsStates` receives `""`.

- [ ] **Step 3: Check decisions as they run**

`src/StateAlchemist.Model/Analysis/RoleValidator.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks every state parameter against its role in its transition, for every leaf the transition can fire from:
/// a parameter must bind the same way from every leaf (spec §6.3) (SALCH0201, SALCH0202, SALCH0204 for state
/// passing, SALCH0301). A decision is checked the way it runs: its <c>Decide</c> reads the active path, which stays;
/// each <c>Complete</c> is the transform of a move to its own target; and a <c>Completed</c> runs after the move of
/// the outcome it takes — or, taking none, after every outcome's move, so it must bind the same way after each.
/// </summary>
public static class RoleValidator
{
    private enum Use
    {
        Guard,
        Transform,
        Decide,
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
            var paths = leaves.Select(leaf => PathPlanner.Plan(hierarchy, transition, leaf)).ToList();
            IReadOnlyList<TransitionPath> MovesTo(IEnumerable<OutcomeCompletion> completions) =>
                completions.SelectMany(c => leaves.Select(leaf => PathPlanner.Move(hierarchy, leaf, c.Target))).ToList();

            if (transition.Guard is not null)
            {
                Check(model, hierarchy, transition.Guard, Use.Guard, paths, diagnostics);
            }

            if (transition.Transform is not null)
            {
                Check(model, hierarchy, transition.Transform, Use.Transform, paths, diagnostics);
            }

            if (transition.Decision is { } decision)
            {
                if (decision.Decider is { } decider)
                {
                    Check(model, hierarchy, decider, Use.Decide, paths, diagnostics);
                }

                foreach (var completion in decision.Completions)
                {
                    Check(model, hierarchy, completion.Complete, Use.Transform, MovesTo([completion]), diagnostics);
                }
            }

            foreach (var completed in transition.Completed)
            {
                var outcome = completed.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Outcome)?.TypeName;
                var after = transition.Decision is { } made
                    ? MovesTo(made.Completions.Where(c => outcome is null || c.OutcomeType == outcome))
                    : paths;
                if (after.Count > 0)
                {
                    Check(model, hierarchy, completed, Use.Action, after, diagnostics);
                }
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
        IReadOnlyList<TransitionPath> paths,
        List<ModelDiagnostic> diagnostics)
    {
        var parameters = method.Parameters.Where(p => p.Kind == ParameterKind.State).ToList();
        foreach (var group in parameters.GroupBy(p => p.State).Where(g => g.Count() > 1))
        {
            var passings = group.Select(p => p.Passing).OrderBy(p => p).ToList();
            var startedOver = paths.All(path => Roles.Of(hierarchy, path, group.Key).HasFlag(Role.Exiting | Role.Entering));
            if (!(use == Use.Transform && passings.SequenceEqual([Passing.In, Passing.Ref]) && startedOver))
            {
                diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, method.Location, group.Last().Name, method.FullName,
                    $"'{model.States[group.Key].Name}' appears more than once"));
            }
        }

        foreach (var parameter in parameters)
        {
            var stateName = model.States[parameter.State].Name;
            var roles = paths.Select(path => Roles.Of(hierarchy, path, parameter.State)).ToList();
            if (roles.All(role => role == Role.None))
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "has no role in this transition"));
                continue;
            }

            var passingProblem = (use, parameter.Passing) switch
            {
                (Use.Guard, not Passing.In) => "a guard only reads states: take it as in",
                (Use.Decide, Passing.Ref or Passing.Out) => "a decision reads states: take it by value or as in",
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
git commit -m "Role-check a decision the way it runs: Decide reads, each outcome moves, Completed follows its outcome"
```

---

### Task 2: `DecisionFailed`, a runtime event

The docs called `DecisionFailed` a *generated* event. It cannot be: the recovery is a transition declared in a
module — `[OnEvent(typeof(DecisionFailed))]` — and a module compiles before any machine exists. It is a runtime
type, carrying the decision's name (so a guard can tell two decisions apart) and the exception.

**Files:**
- Create: `src/StateAlchemist/Events/DecisionFailed.cs`
- Modify: `tests/StateAlchemist.Tests/Api/RuntimeApi.verified.txt` (accepted from the received file)

**Interfaces:**
- Produces: `public readonly struct DecisionFailed : IEvent` — `DecisionFailed(string decision, Exception exception)`,
  `string Decision`, `Exception Exception`, `ToString()` → `"{Decision} failed: {message}"`.

- [ ] **Step 1: Add the type**

`src/StateAlchemist/Events/DecisionFailed.cs`:

```csharp
using System;

namespace StateAlchemist;

/// <summary>
/// Fired when a decision's <c>Decide</c> or <c>DecideAsync</c> throws. The decision is over: nothing it would have
/// changed has changed, and the machine is back in the state it decided from. A transition on this event — from the
/// decision's source or any ancestor — is the recovery; unhandled, it is an unhandled trigger like any other.
/// </summary>
/// <remarks>
/// A runtime type rather than a generated one, so that a module can declare
/// <c>[Transition(From = typeof(AuthRequested), To = typeof(Idle)), OnEvent(typeof(DecisionFailed))]</c> before any
/// machine includes it.
/// </remarks>
public readonly struct DecisionFailed : IEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="decision">The decision that failed, such as <c>AuthModule.CheckAuth</c>.</param>
    /// <param name="exception">What it threw.</param>
    public DecisionFailed(string decision, Exception exception)
    {
        Decision = decision;
        Exception = exception;
    }

    /// <summary>The decision that failed, such as <c>AuthModule.CheckAuth</c>: a guard can tell two decisions apart.</summary>
    public string Decision { get; }

    /// <summary>What it threw.</summary>
    public Exception Exception { get; }

    /// <summary>For logs: <c>AuthModule.CheckAuth failed: the service is unavailable</c>.</summary>
    public override string ToString() => $"{Decision} failed: {Exception.Message}";
}
```

- [ ] **Step 2: See the API snapshot catch it**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: FAIL — `RuntimeApiIsUnchanged`, once per framework, with the new struct as the only difference.

- [ ] **Step 3: Accept the new surface**

Check that the difference is exactly the struct above, then accept it:

```bash
cd tests/StateAlchemist.Tests/Api
mv RuntimeApi.DotNet11_0.received.txt RuntimeApi.verified.txt
rm RuntimeApi.DotNet*_0.received.txt
cd -
dotnet test --solution StateAlchemist.slnx
```

Expected: PASS. The accepted difference is exactly this, after `DecisionAttribute`:

```text
    public readonly struct DecisionFailed : StateAlchemist.IEvent
    {
        public DecisionFailed(string decision, System.Exception exception) { }
        public string Decision { get; }
        public System.Exception Exception { get; }
        public override string ToString() { }
    }
```

Accept by renaming the received file, never by pasting text: PublicApiGenerator writes no trailing newline, and a
snapshot typed by hand with one does not match.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "DecisionFailed is a runtime event, so modules can recover from it"
```

---

### Task 3: Machines that decide and machines that run

**Files:**
- Modify: `Directory.Packages.props` (System.IO.Pipelines), `tests/StateAlchemist.Contracts/StateAlchemist.Contracts.csproj`
- Modify: `tests/StateAlchemist.Contracts/Machines/RecordingContext.cs` (`Answer`, `Deciding`), `Shapes.cs`
- Create: `tests/StateAlchemist.Contracts/Machines/UnionPolyfill.cs`, `Deciding.cs`, `DecidingModule.cs`, `Runs.cs`

**Interfaces:**
- Produces:
  - `RecordingContext.Answer` — a `TaskCompletionSource<object>` an async decision awaits. The test completes it
    with an outcome union or faults it. Its continuations run **inline**, so when `SetResult` returns, the machine
    has already processed the outcome — which is what makes "a result arriving late is dropped" checkable without a
    timing.
  - `RecordingContext.Deciding` — completed with the decision's `CancellationToken` when an async decision starts.
  - `Shapes.RecorderUnchecked`, `RecorderSerialized`, `Deciding`, `DecidingSerialized`, `Runs`.
- The machines:
  - **Deciding** (`Machines.Deciding`): `DecideRoot {Ticks}` → `Asking [Initial] {Question}`, `Account {Name}`,
    `Refused {Code}`; events `Hangup`, `Nudge`; outcomes `Accept(Name)`, `Reject(Code)`, union `Verdict`.
    Value 1 ticks from anywhere (the "rest of the input" a decision holds back); 7 asks another question; 4 returns
    to asking. Decisions: **2** `Ask` — async, answered by the test, handles `Hangup`; **3** `Quick` — synchronous;
    **5** `Loop` — fires its own machine; **6** `Enqueuer` — enqueues a `Nudge`; **8** `Recheck` — from `Account`,
    where nothing recovers a failure; **9** `HangUpOnItself` — enqueues the `Hangup` it handles. `Hangup` returns to
    asking from anywhere, `Nudge` is recorded anywhere, and `DecisionFailed` from `Asking` moves to `Refused`.
  - **Runs** (`Machines.Runs`): `RunRoot {Lines}` → `Text [Initial] {Length}`, `Escape`. `Text` has an `[OnAny]`
    run (whose transform also takes the context, to exercise that shape), a line feed, a guarded bell on 7, and IAC
    (255) to `Escape`; anything after IAC returns to `Text`.

- [ ] **Step 1: Add the pipe package and the context's decision hooks**

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
    <PackageVersion Include="PolySharp" Version="1.16.0" />
    <PackageVersion Include="TUnit" Version="1.66.27" />
    <PackageVersion Include="TUnit.Core" Version="1.66.27" />
    <PackageVersion Include="TUnit.Assertions" Version="1.66.27" />
    <PackageVersion Include="PublicApiGenerator" Version="11.5.4" />
    <PackageVersion Include="Verify.TUnit" Version="32.0.0" />
  </ItemGroup>
</Project>
```

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
    <PackageReference Include="System.IO.Pipelines" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\StateAlchemist\StateAlchemist.csproj" />
    <ProjectReference Include="..\..\samples\StateAlchemist.Samples\StateAlchemist.Samples.csproj" />
  </ItemGroup>
</Project>
```

`tests/StateAlchemist.Contracts/Machines/RecordingContext.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
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

    /// <summary>
    /// What an async decision awaits: a test completes it with an outcome union, or faults it. Its continuations run
    /// inline, so when <c>SetResult</c> returns, the machine has already dealt with the outcome.
    /// </summary>
    public TaskCompletionSource<object> Answer { get; } = new();

    /// <summary>Completed when an async decision starts, with the token it was given.</summary>
    public TaskCompletionSource<CancellationToken> Deciding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

- [ ] **Step 2: Write the machines**

`tests/StateAlchemist.Contracts/Machines/UnionPolyfill.cs`:

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

`tests/StateAlchemist.Contracts/Machines/Deciding.cs`:

```csharp
namespace StateAlchemist.Contracts.Machines.Deciding;

// DecideRoot ─┬─ Asking [Initial]
//             ├─ Account
//             └─ Refused

public struct DecideRoot : IRootState
{
    public int Ticks;
}

[Initial]
public struct Asking : IState<DecideRoot>
{
    public int Question;
}

public struct Account : IState<DecideRoot>
{
    public string? Name;
}

public struct Refused : IState<DecideRoot>
{
    public int Code;
}

/// <summary>The connection closed: the one event the pending decision handles at once.</summary>
public readonly struct Hangup : IEvent;

/// <summary>An event the pending decision does not handle: it waits for the decision.</summary>
public readonly struct Nudge : IEvent;

public readonly record struct Accept(string Name);

public readonly record struct Reject(int Code);

public union Verdict(Accept, Reject);
```

`public union Verdict(Accept, Reject);` is a C# 15 union declaration. It compiles to a struct marked
`[Union]` with one constructor per case and a `Value` property — the shape the reflection front-end reads
outcomes from (Plan 1, Task 11), and the shape the interpreter reads the chosen case from.

`tests/StateAlchemist.Contracts/Machines/DecidingModule.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Contracts.Machines.Deciding;

/// <summary>Decisions, synchronous and async, recording what they do; the test answers the async ones through <see cref="RecordingContext.Answer"/>.</summary>
[Module]
public static class DecidingModule
{
    /// <summary>1: counts, from anywhere — the "rest of the input" a pending decision holds back.</summary>
    [Transition(From = typeof(DecideRoot)), On(1)]
    public static void Tick(ref DecideRoot root, RecordingContext context)
    {
        root.Ticks++;
        context.Record("tick");
    }

    /// <summary>7: another question.</summary>
    [Transition(From = typeof(Asking)), On(7)]
    public static void More(ref Asking self) => self.Question++;

    /// <summary>4: back to asking, from anywhere.</summary>
    [Transition(From = typeof(DecideRoot), To = typeof(Asking)), On(4)]
    public static void Again()
    {
    }

    /// <summary>2: an async decision the test answers. A hang-up interrupts it.</summary>
    [Decision(From = typeof(Asking), Handle = new[] { typeof(Hangup) }), On(2)]
    public static class Ask
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context, Asking asking, CancellationToken cancellation)
        {
            context.Record($"deciding {asking.Question}");
            context.Deciding.TrySetResult(cancellation);
            return (Verdict)await context.Answer.Task;
        }

        [To(typeof(Account))]
        public static void Complete(in Asking from, ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(in Asking from, ref Refused to, Reject outcome) => to.Code = outcome.Code + from.Question;

        public static ValueTask CompletedAsync(RecordingContext context, Account account, Accept outcome)
        {
            context.Record($"welcome {outcome.Name} as {account.Name}");
            return default;
        }

        public static void Completed(RecordingContext context, Reject outcome) => context.Record($"refused {outcome.Code}");
    }

    /// <summary>3: a synchronous decision: it answers from the data it is given, at once.</summary>
    [Decision(From = typeof(Asking)), On(3)]
    public static class Quick
    {
        public static Verdict Decide(RecordingContext context, Asking asking)
        {
            context.Record($"quick {asking.Question}");
            return asking.Question > 0 ? new Accept("quick") : new Reject(3);
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>5: a decision that fires its own machine instead of enqueueing.</summary>
    [Decision(From = typeof(Asking)), On(5)]
    public static class Loop
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context)
        {
            await context.Machine!.FireAsync((byte)1);
            return new Accept("never");
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>6: a decision that enqueues, which is how code inside the machine asks for more.</summary>
    [Decision(From = typeof(Asking)), On(6)]
    public static class Enqueuer
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context)
        {
            await Task.Yield();
            context.Machine!.Enqueue(new Nudge());
            return new Accept("queued");
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;

        public static void Completed(RecordingContext context, Accept outcome) => context.Record($"welcome {outcome.Name}");
    }

    /// <summary>8: from Account, an async decision nothing recovers from when it fails.</summary>
    [Decision(From = typeof(Account)), On(8)]
    public static class Recheck
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context) => (Verdict)await context.Answer.Task;

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>9: a decision that enqueues the hang-up it handles, then waits for the test.</summary>
    [Decision(From = typeof(Asking), Handle = new[] { typeof(Hangup) }), On(9)]
    public static class HangUpOnItself
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context, CancellationToken cancellation)
        {
            context.Deciding.TrySetResult(cancellation);
            await Task.Yield();
            context.Machine!.Enqueue(new Hangup());
            return (Verdict)await context.Answer.Task;
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>A hang-up returns to asking from anywhere: from the pending state, that leaves it.</summary>
    [Transition(From = typeof(DecideRoot), To = typeof(Asking)), OnEvent(typeof(Hangup))]
    public static void HungUp(RecordingContext context) => context.Record("hung up");

    /// <summary>A nudge is noted wherever it arrives.</summary>
    [Transition(From = typeof(DecideRoot)), OnEvent(typeof(Nudge))]
    public static void Nudged(RecordingContext context) => context.Record("nudged");

    /// <summary>A failed decision from Asking is refused with the reason.</summary>
    [Transition(From = typeof(Asking), To = typeof(Refused)), OnEvent(typeof(DecisionFailed))]
    public static void Failed(in DecisionFailed failed, ref Refused to, RecordingContext context)
    {
        to.Code = -1;
        context.Record($"failed {failed.Decision}: {failed.Exception.Message}");
    }
}
```

`tests/StateAlchemist.Contracts/Machines/Runs.cs`:

```csharp
using System;

namespace StateAlchemist.Contracts.Machines.Runs;

// RunRoot ─┬─ Text [Initial]
//          └─ Escape

public struct RunRoot : IRootState
{
    public int Lines;
}

[Initial]
public struct Text : IState<RunRoot>
{
    public int Length;
}

public struct Escape : IState<RunRoot>;

/// <summary>Text arrives in runs: everything up to the next line feed, bell or IAC is one call.</summary>
[Module]
public static class RunModule
{
    /// <summary>Any value: appended to the line, a whole run at a time.</summary>
    [Transition(From = typeof(Text)), OnAny, Run]
    public static class Append
    {
        public static void Transform(ref Text self, ReadOnlySpan<byte> run, RecordingContext context)
        {
            self.Length += run.Length;
            context.Record($"run {run.Length}");
        }

        public static void Completed(RecordingContext context, ReadOnlyMemory<byte> run) => context.Record($"appended {run.Length}");
    }

    /// <summary>A line feed ends the line.</summary>
    [Transition(From = typeof(Text)), On(10)]
    public static void Line(ref Text self, ref RunRoot root)
    {
        self.Length = 0;
        root.Lines++;
    }

    /// <summary>A bell rings only when allowed; otherwise it falls through to the run, as a run of one.</summary>
    [Transition(From = typeof(Text)), On(7)]
    public static class Bell
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Bell");

        public static void Completed(RecordingContext context) => context.Record("bell");
    }

    /// <summary>IAC leaves the text.</summary>
    [Transition(From = typeof(Text), To = typeof(Escape)), On(255)]
    public static void Iac(in Text from)
    {
    }

    /// <summary>Anything after IAC returns to a fresh line.</summary>
    [Transition(From = typeof(Escape), To = typeof(Text)), OnAny]
    public static void Back()
    {
    }
}
```

`tests/StateAlchemist.Contracts/Machines/Shapes.cs`:

```csharp
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Contracts.Machines.Runs;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Contracts.Machines;

/// <summary>The contract machines. A generated test project declares one <c>[Machine]</c> class per shape, with the same modules.</summary>
public static class Shapes
{
    public static readonly MachineShape Recorder = new("Recorder", typeof(Root), [typeof(RecorderModule), typeof(RecorderExtras)], typeof(RecordingContext));

    public static readonly MachineShape Guards = new("Guards", typeof(GuardRoot), [typeof(GuardModule)], typeof(RecordingContext));

    public static readonly MachineShape GuardsThatThrow = Guards with { Name = "GuardsThatThrow", Unhandled = Unhandled.Throw };

    public static readonly MachineShape Failures = new("Failures", typeof(FailRoot), [typeof(FailureModule)], typeof(RecordingContext));

    public static readonly MachineShape RecorderUnchecked = Recorder with { Name = "RecorderUnchecked", Concurrency = Concurrency.Unchecked };

    public static readonly MachineShape RecorderSerialized = Recorder with { Name = "RecorderSerialized", Concurrency = Concurrency.Serialized };

    public static readonly MachineShape Deciding = new("Deciding", typeof(DecideRoot), [typeof(DecidingModule)], typeof(RecordingContext));

    public static readonly MachineShape DecidingSerialized = Deciding with { Name = "DecidingSerialized", Concurrency = Concurrency.Serialized };

    public static readonly MachineShape Runs = new("Runs", typeof(RunRoot), [typeof(RunModule)], typeof(RecordingContext));

    public static readonly MachineShape Telnet = new("SampleTelnet", typeof(Connected), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule)], typeof(TelnetContext));
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `0 Warning(s)`, `0 Error(s)` on all three frameworks.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Contract machines for decisions and runs"
```

---

### Task 4: The contracts

**Files:**
- Create: `tests/StateAlchemist.Contracts/Suite/DecisionContract.cs`, `BackpressureContract.cs`, `RunContract.cs`,
  `SerializedContract.cs`
- Modify: `tests/StateAlchemist.Contracts/Suite/ConcurrencyContract.cs` (one test added)

**Interfaces:**
- Consumes: Task 3's machines.
- Produces: four new abstract contracts; `ConcurrencyContract` extended. `Serialized` tests get their own contract
  because a generated `Serialized` machine arrives a plan later than generated `Checked` and `Unchecked` ones
  (Plans 5 and 4): each generated test project inherits whole contracts.

| Contract | Enforces | Notably |
|---|---|---|
| `DecisionContract` | `concepts/decisions.md` | sync decisions inline; `FireAsync` waits through an async one while the source keeps its data; each outcome's `Complete` and `Completed`; a handled event leaves the pending state and cancels, and a late result is dropped; other events wait, then run before the rest of the input; `DecisionFailed` from `Decide` and `DecideAsync`; an outcome that throws fails its input; a failed input abandons its decision; stopping cancels and fails the waiting caller; self-firing refused; `Enqueue` from a decision; `Checked` refuses and `Serialized` queues another caller's values |
| `BackpressureContract` | `concepts/decisions.md`, "Deferral and backpressure" | the docs' read loop over a real `Pipe`: the writer is paused while a decision is pending, then everything is processed |
| `RunContract` | `concepts/runs.md` | a run in one call; the stop set; a guarded stop value that falls through is a run of one; a single value is a run of one; batch and value-by-value agree |
| `ConcurrencyContract` (added) | `concepts/concurrency.md` | `Unchecked` with one caller behaves like any other |
| `SerializedContract` | `concepts/concurrency.md`, "Serialized" | overlapping callers run in turn; each gets its own exception; callers from many threads are all processed |

- [ ] **Step 1: Write the decision contract**

`tests/StateAlchemist.Contracts/Suite/DecisionContract.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/decisions.md: outcomes, the pending state, interruption, failure and stopping.</summary>
public abstract class DecisionContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(MachineShape? shape = null, ContractHooks? hooks = null, RecordingContext? context = null)
    {
        context ??= new RecordingContext();
        var machine = await StartAsync(shape ?? Shapes.Deciding, context, hooks);
        context.Log.Clear();
        return (machine, context);
    }

    private static T Data<T>(IMachine<byte> machine)
        where T : struct => machine.TryGetState(out T value) ? value : throw new InvalidOperationException($"{typeof(T).Name} is not active");

    private static Verdict Accepted(string name) => new Accept(name);

    /// <summary>
    /// Waits until the decision has started, and returns its token — or, if <paramref name="fire"/> finishes first,
    /// surfaces that instead of waiting for a decision that will never start.
    /// </summary>
    private static async Task<CancellationToken> DecisionStarted(RecordingContext context, Task fire)
    {
        if (await Task.WhenAny(context.Deciding.Task, fire) == fire)
        {
            await fire;
            throw new InvalidOperationException("FireAsync completed without starting a decision.");
        }

        return await context.Deciding.Task;
    }

    [Test]
    public async Task ASynchronousDecisionCompletesAsOneTransition()
    {
        var (machine, context) = await Started();
        await machine.FireAsync(new byte[] { 7, 3 });
        await Assert.That(context.Trace).IsEqualTo("quick 1");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("quick");

        await machine.FireAsync(new byte[] { 4, 3 });
        await Assert.That(Data<Refused>(machine).Code).IsEqualTo(3);
    }

    [Test]
    public async Task FireAsyncWaitsThroughAnAsyncDecisionWhileTheSourceKeepsItsData()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)7);
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        await Assert.That(fire.IsCompleted).IsFalse();
        await Assert.That(Data<Asking>(machine).Question).IsEqualTo(1);
        await Assert.That(Data<DecideRoot>(machine).Ticks).IsEqualTo(0);

        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await Assert.That(context.Trace).IsEqualTo("deciding 1 | welcome ann as ann | tick");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("ann");
        await Assert.That(Data<DecideRoot>(machine).Ticks).IsEqualTo(1);
    }

    [Test]
    public async Task EachOutcomeRunsItsOwnCompleteAndCompleted()
    {
        var (machine, context) = await Started();
        await machine.FireAsync(new byte[] { 7, 7 });
        var fire = machine.FireAsync((byte)2).AsTask();
        await DecisionStarted(context, fire);
        context.Answer.SetResult(new Verdict(new Reject(40)));
        await fire;
        await Assert.That(Data<Refused>(machine).Code).IsEqualTo(42);
        await Assert.That(context.Trace).IsEqualTo("deciding 2 | refused 40");
    }

    [Test]
    public async Task AHandledEventLeavesThePendingStateAndCancelsTheDecision()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        var cancellation = await DecisionStarted(context, fire);

        await machine.FireAsync(new Hangup());
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await fire;
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | hung up | tick");

        context.Answer.SetResult(Accepted("late"));
        await Assert.That(machine.IsIn<Asking>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | hung up | tick");
    }

    [Test]
    public async Task OtherEventsWaitForTheDecisionThenRunBeforeTheRestOfTheInput()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        var nudge = machine.FireAsync(new Nudge()).AsTask();
        await Assert.That(nudge.IsCompleted).IsFalse();
        await Assert.That(context.Log).DoesNotContain("nudged");

        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await nudge;
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | welcome ann as ann | nudged | tick");
    }

    [Test]
    public async Task AFailingDecisionFiresDecisionFailed()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync((byte)2).AsTask();
        await DecisionStarted(context, fire);
        context.Answer.SetException(new InvalidOperationException("no service"));
        await fire;
        await Assert.That(machine.IsIn<Refused>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | failed DecidingModule.Ask: no service");
    }

    [Test]
    public async Task ASynchronousDecisionThatThrowsFiresDecisionFailedToo()
    {
        var (machine, context) = await Started();
        context.Failing.Add("quick 0");
        await machine.FireAsync((byte)3);
        await Assert.That(machine.IsIn<Refused>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("failed DecidingModule.Quick: quick 0 failed");
    }

    [Test]
    public async Task AnOutcomeThatThrowsFailsTheInputThatStartedTheDecision()
    {
        var (machine, context) = await Started();
        context.Failing.Add("welcome ann as ann");
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);
        context.Answer.SetResult(Accepted("ann"));

        await Assert.That(async () => await fire).Throws<InvalidOperationException>().WithMessage("welcome ann as ann failed");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("ann");
        await Assert.That(context.Log).DoesNotContain("tick");
    }

    [Test]
    public async Task AnInputThatFailsWhileItsDecisionIsPendingAbandonsTheDecision()
    {
        var (machine, context) = await Started();
        context.Failing.Add("hung up");
        var fire = machine.FireAsync(new byte[] { 9, 1 }).AsTask();
        var cancellation = await DecisionStarted(context, fire);

        await Assert.That(async () => await fire).Throws<InvalidOperationException>().WithMessage("hung up failed");
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(machine.IsIn<Asking>()).IsTrue();
        await Assert.That(context.Log).DoesNotContain("tick");
    }

    [Test]
    public async Task AFailureNothingHandlesIsAnUnhandledTrigger()
    {
        var context = new RecordingContext();
        var (machine, _) = await Started(hooks: new ContractHooks(context.Log), context: context);
        await machine.FireAsync(new byte[] { 7, 3 });
        context.Log.Clear();

        var fire = machine.FireAsync((byte)8).AsTask();
        context.Answer.SetException(new InvalidOperationException("gone"));
        await fire;
        await Assert.That(context.Trace).IsEqualTo("unhandled DecisionFailed in Account");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("quick");
    }

    [Test]
    public async Task StoppingCancelsThePendingDecisionAndDiscardsTheRestOfTheInput()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        var cancellation = await DecisionStarted(context, fire);

        await machine.StopAsync();
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(async () => await fire).Throws<MachineNotRunningException>();
        await Assert.That(context.Log).DoesNotContain("tick");
    }

    [Test]
    public async Task ADecisionThatFiresItsOwnMachineFailsInsteadOfWaitingForItself()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)5);
        await Assert.That(machine.IsIn<Refused>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("failed DecidingModule.Loop: " + new ConcurrentUseException().Message);
    }

    [Test]
    public async Task ADecisionEnqueuesAndTheEventRunsAfterItsOutcome()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)6);
        await Assert.That(context.Trace).IsEqualTo("welcome queued | nudged");
    }

    [Test]
    public async Task ACheckedMachineRefusesAnotherCallersValuesWhileADecisionIsPending()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<ConcurrentUseException>();
        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await Assert.That(Data<DecideRoot>(machine).Ticks).IsEqualTo(1);
    }

    [Test]
    public async Task ASerializedMachineQueuesAnotherCallersValuesBehindTheWaitingInput()
    {
        var (machine, context) = await Started(Shapes.DecidingSerialized);
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        var other = machine.FireAsync((byte)1).AsTask();
        await Assert.That(other.IsCompleted).IsFalse();
        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await other;
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | welcome ann as ann | tick | tick");
    }

    [Test]
    public async Task APlanShowsADecisionWithoutStartingIt()
    {
        var (machine, context) = await Started();
        var plan = machine.Plan(2);
        await Assert.That(plan.IsDecision).IsTrue();
        await Assert.That(plan.Transition).IsEqualTo("DecidingModule.Ask");
        await Assert.That(context.Trace).IsEqualTo("");
    }
}
```

`DecisionStarted` is the global constraint in code: waiting for the decision also watches the `FireAsync` that
should start it.

- [ ] **Step 2: Write the backpressure and run contracts**

`tests/StateAlchemist.Contracts/Suite/BackpressureContract.cs`:

```csharp
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/decisions.md, "Deferral and backpressure": the ordinary read loop, and a real pipe.</summary>
public abstract class BackpressureContract : MachineContract
{
    /// <summary>The loop the docs show, with nothing added.</summary>
    private static async Task ReadLoopAsync(PipeReader reader, IMachine<byte> machine)
    {
        while (true)
        {
            var read = await reader.ReadAsync();
            foreach (var segment in read.Buffer)
            {
                await machine.FireAsync(segment);
            }

            reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted)
            {
                break;
            }
        }

        await reader.CompleteAsync();
    }

    [Test]
    public async Task WhileADecisionIsPendingThePipeStopsTheWriter()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Deciding, context);
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 8, resumeWriterThreshold: 4, useSynchronizationContext: false));
        var reading = ReadLoopAsync(pipe.Reader, machine);

        await pipe.Writer.WriteAsync(new byte[] { 2 });
        if (await Task.WhenAny(context.Deciding.Task, reading) == reading)
        {
            await reading; // the loop ended or failed without the decision starting: surface that, rather than wait
        }

        var writing = pipe.Writer.WriteAsync(Enumerable.Repeat((byte)1, 16).ToArray()).AsTask();
        await Task.Delay(100);
        await Assert.That(writing.IsCompleted).IsFalse();
        await Assert.That(machine.TryGetState(out DecideRoot waiting) ? waiting.Ticks : -1).IsEqualTo(0);

        context.Answer.SetResult(new Verdict(new Accept("ann")));
        await writing;
        await pipe.Writer.CompleteAsync();
        await reading;
        await Assert.That(machine.TryGetState(out DecideRoot done) ? done.Ticks : -1).IsEqualTo(16);
    }
}
```

The pipe pauses its writer at 8 unconsumed bytes. The reader has read one byte — the one that started the decision —
and has not advanced past it, so 16 more bytes cannot be flushed until the decision resolves. Nothing here is
timing-dependent: the `Task.Delay` only gives a broken implementation time to show itself.

`tests/StateAlchemist.Contracts/Suite/RunContract.cs`:

```csharp
using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Runs;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/runs.md: a run is taken in one call, ends at its stop set, and changes speed, never results.</summary>
public abstract class RunContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(params string[] allow)
    {
        var context = new RecordingContext();
        context.Allow.UnionWith(allow);
        var machine = await StartAsync(Shapes.Runs, context);
        context.Log.Clear();
        return (machine, context);
    }

    [Test]
    public async Task ABatchHandsARunOverInOneCall()
    {
        var (machine, context) = await Started();
        await machine.FireAsync("hello"u8.ToArray());
        await Assert.That(context.Trace).IsEqualTo("run 5 | appended 5");
        await Assert.That(machine.TryGetState(out Text text) ? text.Length : -1).IsEqualTo(5);
    }

    [Test]
    public async Task ARunEndsAtTheFirstValueAnotherTransitionTakes()
    {
        var (machine, context) = await Started();
        await machine.FireAsync("ab\ncd"u8.ToArray());
        await Assert.That(context.Trace).IsEqualTo("run 2 | appended 2 | run 2 | appended 2");
        await Assert.That(machine.TryGetState(out RunRoot root) ? root.Lines : -1).IsEqualTo(1);
    }

    [Test]
    public async Task AStopValueWhoseGuardFailsReachesTheRunAsARunOfOne()
    {
        var (refusing, refused) = await Started();
        await refusing.FireAsync("ab\acd"u8.ToArray());
        await Assert.That(refused.Trace).IsEqualTo("run 2 | appended 2 | run 1 | appended 1 | run 2 | appended 2");

        var (ringing, rung) = await Started("Bell");
        await ringing.FireAsync("ab\acd"u8.ToArray());
        await Assert.That(rung.Trace).IsEqualTo("run 2 | appended 2 | bell | run 2 | appended 2");
    }

    [Test]
    public async Task ASingleValueIsARunOfOne()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)'a');
        await Assert.That(context.Trace).IsEqualTo("run 1 | appended 1");
    }

    [Test]
    public async Task RunsChangeHowFastABatchIsConsumedNeverWhatItDoes()
    {
        byte[] input = [.. "ab\ncd"u8, 255, .. "xyz\a"u8];
        var (batched, _) = await Started();
        await batched.FireAsync(input);
        var (single, _) = await Started();
        foreach (var value in input)
        {
            await single.FireAsync(value);
        }

        foreach (var machine in new[] { batched, single })
        {
            await Assert.That(machine.StateType).IsEqualTo(typeof(Text));
            await Assert.That(machine.TryGetState(out Text text) ? text.Length : -1).IsEqualTo(3);
            await Assert.That(machine.TryGetState(out RunRoot root) ? root.Lines : -1).IsEqualTo(1);
        }
    }
}
```

`[.. "ab\ncd"u8, 255, .. "xyz\a"u8]` spells IAC as a byte: `"\xFF"` inside a UTF-8 literal is the character
U+00FF, which encodes as two bytes.

- [ ] **Step 3: Extend the concurrency contract, and add the serialized one**

`tests/StateAlchemist.Contracts/Suite/ConcurrencyContract.cs`:

```csharp
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/concurrency.md: a Checked machine refuses a second caller and never corrupts state; an Unchecked one trusts its single caller.</summary>
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

    [Test]
    public async Task AnUncheckedMachineWithOneCallerBehavesLikeAnyOther()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.RecorderUnchecked, context);
        context.Log.Clear();
        await machine.FireAsync(new byte[] { 3, 1 });
        await Assert.That(context.Trace).IsEqualTo("exited A1 (1) | exited A1 (extra) | entered A2 | completed Sibling");
    }
}
```

`tests/StateAlchemist.Contracts/Suite/SerializedContract.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/concurrency.md, "Serialized": any thread may fire; calls run one at a time, in turn.</summary>
public abstract class SerializedContract : MachineContract
{
    [Test]
    public async Task ASerializedMachineRunsOverlappingCallersInTurn()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.RecorderSerialized, context);
        var held = machine.FireAsync((byte)11).AsTask();
        var queued = machine.FireAsync((byte)3).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();

        context.Gate.SetResult();
        await held;
        await queued;
        await Assert.That(machine.TryGetState(out Machines.Recording.A1 a1) ? a1.Value : -1).IsEqualTo(1);
    }

    [Test]
    public async Task ASerializedMachineGivesEachCallerItsOwnException()
    {
        var context = new RecordingContext();
        context.Failing.Add("completed Sibling");
        var machine = await StartAsync(Shapes.RecorderSerialized, context);
        var held = machine.FireAsync((byte)11).AsTask();
        var failing = machine.FireAsync((byte)1).AsTask();

        context.Gate.SetResult();
        await held;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await failing);
        await Assert.That(thrown!.Message).IsEqualTo("completed Sibling failed");
    }

    [Test]
    public async Task ASerializedMachineTakesCallersFromManyThreads()
    {
        var machine = await StartAsync(Shapes.RecorderSerialized, new RecordingContext());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await machine.FireAsync((byte)3);
            }
        })));
        await Assert.That(machine.TryGetState(out Machines.Recording.A1 a1) ? a1.Value : -1).IsEqualTo(400);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Contracts for decisions, backpressure, runs and the concurrency modes"
```

---

### Task 5: The interpreter — inputs, the pump, decisions, runs

**Files:**
- Modify: `tests/StateAlchemist.Reference.Tests/Contracts/ReferenceContracts.cs` (four contracts)
- Create: `tests/StateAlchemist.Reference.Tests/Interpreter/RunShapes.cs`; modify `ReferenceMachineTests.cs` (one test)
- Modify: `src/StateAlchemist.Reference/Interpreter/ReferenceMachine.cs`, `ReferenceMachine.Execution.cs`
- Create: `src/StateAlchemist.Reference/Interpreter/ReferenceMachine.Inbox.cs`, `ReferenceMachine.Decisions.cs`,
  `ReferenceMachine.Runs.cs`
- Modify: `src/StateAlchemist/Runtime/IMachine.cs` (where `Enqueue` may be called from)

**Interfaces:**
- Consumes: everything above; `ReferenceMachine<TValue>`'s public surface is unchanged.
- Produces: an interpreter that passes every contract. The rules it implements, which Plan 6's generated code must
  match, are in the comment at the top of `ReferenceMachine.Inbox.cs` and `ReferenceMachine.Decisions.cs`.

- [ ] **Step 1: Inherit the new contracts, and test the run shapes no contract machine has**

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

[InheritsTests]
public sealed class ReferenceDecisions : DecisionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceBackpressure : BackpressureContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceRuns : RunContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceSerialized : SerializedContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}
```

The contract machines have no configuration type, so the run transforms that take one are checked on the
interpreter directly:

`tests/StateAlchemist.Reference.Tests/Interpreter/RunShapes.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace StateAlchemist.Reference.Tests.Interpreter;

// A run transform may take the configuration and the context after the run (spec §6.7). The contract machines have
// no configuration, so these shapes are checked here.

public readonly record struct RunConfig(int Weight);

public sealed class RunLog
{
    public List<string> Entries { get; } = [];
}

public struct Stream : IRootState
{
    public int Total;
}

[Module]
public static class ConfiguredRuns
{
    [Transition(From = typeof(Stream)), OnRange(0, 99), Run]
    public static void Low(ref Stream self, ReadOnlySpan<byte> run, in RunConfig config) => self.Total += run.Length * config.Weight;

    [Transition(From = typeof(Stream)), OnRange(100, 255), Run]
    public static void High(ref Stream self, ReadOnlySpan<byte> run, in RunConfig config, RunLog log)
    {
        self.Total += run.Length * config.Weight * 100;
        log.Entries.Add($"high {run.Length}");
    }
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
    public async Task ARunTransformMayTakeTheConfigurationAndTheContext()
    {
        var spec = new MachineSpec("Configured", typeof(Stream), typeof(byte), [typeof(ConfiguredRuns)], typeof(RunLog), typeof(RunConfig));
        var log = new RunLog();
        var machine = ReferenceMachine<byte>.Create(ReflectionModelBuilder.Build(spec), log, new RunConfig(2));
        await machine.StartAsync();
        await machine.FireAsync(new byte[] { 1, 2, 3, 200, 201 });
        await Assert.That(machine.TryGetState(out Stream stream) ? stream.Total : -1).IsEqualTo(3 * 2 + 2 * 2 * 100);
        await Assert.That(string.Join(",", log.Entries)).IsEqualTo("high 2");
    }

    [Test]
    public async Task AValidMachineStartsInItsInitialLeaf()
    {
        var machine = ReferenceMachine<byte>.Create(Telnet(), new TelnetContext());
        await Assert.That(machine.StateType).IsEqualTo(typeof(Idle));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --solution StateAlchemist.slnx`
Expected: FAIL — 23 tests per framework. Fifteen decision tests and the backpressure test fail with
`NotSupportedException: Decisions are interpreted from Plan 3.`, the five run contracts and
`ARunTransformMayTakeTheConfigurationAndTheContext` with
`NotSupportedException: Parameter kind Run is interpreted from Plan 3.`, and
`ASerializedMachineRunsOverlappingCallersInTurn` because Plan 2 runs `Serialized` callers at the same time.
`ASerializedMachineTakesCallersFromManyThreads` may pass or fail: without the pump, its outcome is a race. Nothing
hangs.

- [ ] **Step 3: Submit inputs; pump them**

The public surface. `FireAsync` now submits an input; `Enqueue` decides what to do by *where it is called from*,
which an `AsyncLocal` records: inside a transition or a lifecycle action it queues for step 9; inside a decision
that is still pending it queues and starts a pump (the machine is idle while a decision runs); anywhere else it
throws.

`src/StateAlchemist.Reference/Interpreter/ReferenceMachine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lazy<MachineDefinition> _definition;
    private int _leaf;

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

        await RunLifecycleAsync(ActionPhase.Entered, _hierarchy.PathFromRoot(_leaf));
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

        Abandon();
        _lifetime.Cancel();
        await RunLifecycleAsync(ActionPhase.Exited, _hierarchy.PathFromRoot(_leaf).Reverse().ToList());
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => StopAsync();

    /// <inheritdoc/>
    public ValueTask FireAsync(TValue value) => SubmitAsync(Work.ForValues(new[] { value }));

    /// <inheritdoc/>
    public ValueTask FireAsync(ReadOnlyMemory<TValue> values) => SubmitAsync(Work.ForValues(values));

    /// <inheritdoc/>
    public ValueTask FireAsync<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        SubmitAsync(Work.ForEvent(e));

    /// <inheritdoc/>
    public void Enqueue<TEvent>(TEvent e)
        where TEvent : struct, IEvent
    {
        switch (_inside.Value)
        {
            case null:
                throw new InvalidOperationException("Enqueue is for code running inside the machine, such as an action, a hook or a decision. From outside, use FireAsync.");
            case { Decision: { } decision }:
                lock (_sync)
                {
                    if (!ReferenceEquals(decision, _pending))
                    {
                        return; // from a decision that is no longer pending: it can no longer affect the machine
                    }

                    _queue.Enqueue(Work.Queued(e));
                }

                // The machine is idle while a decision runs, so nothing else will look at the queue. Start a pump —
                // off the decision's stack, since Enqueue returns at once.
                using (ExecutionContext.SuppressFlow())
                {
                    _ = Task.Run(PumpAsync);
                }

                return;
            default:
                lock (_sync)
                {
                    _queue.Enqueue(Work.Queued(e));
                }

                return;
        }
    }

    /// <inheritdoc/>
    public TransitionPlan Plan(TValue value) => PlanFor(_resolver.ForValue(_leaf, ToInt64(value)), Trigger.OfValue(value));

    /// <inheritdoc/>
    public TransitionPlan Plan<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        PlanFor(_resolver.ForEvent(_leaf, typeof(TEvent).FullName!), Trigger.OfEvent(e));

    private async ValueTask RunLifecycleAsync(ActionPhase phase, IReadOnlyList<int> states)
    {
        // Lifecycle actions run inside the machine, so they may Enqueue; what they queue runs before the first trigger.
        _inside.Value = Inside.Transition;
        foreach (var state in states)
        {
            var info = new TransitionInfo<TValue>(
                "(lifecycle)", _machine.StateTypes[_hierarchy.Root], StateType, StateType, TransitionKind.Stay,
                phase == ActionPhase.Entered ? Phase.Entered : Phase.Exited, default, false, null, _machine.StateTypes[state]);
            await RunStateActionsAsync(phase, state, info, snapshots: null);
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

    /// <summary>What fired: a value (with the run it starts, one value long unless a run transition takes more) or an event.</summary>
    private readonly record struct Trigger(TValue Value, bool HasValue, ReadOnlyMemory<TValue> Run, object? Event)
    {
        public static Trigger OfValue(TValue value) => new(value, true, new[] { value }, null);

        public static Trigger OfRun(ReadOnlyMemory<TValue> run) => new(run.Span[0], true, run, null);

        public static Trigger OfEvent(object e) => new(default, false, default, e);

        public override string ToString() => HasValue ? Value.ToString()! : "event " + Event!.GetType().Name;
    }
}
```

The inbox and the pump:

`src/StateAlchemist.Reference/Interpreter/ReferenceMachine.Inbox.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

// Who runs what, and when (spec §6.4, §6.6, §6.10).
//
// Every FireAsync is one caller's *input*: a batch of values, or one event. Inputs wait in the inbox in arrival order
// and are processed one at a time; the one being processed is `_current`. Whoever finds the machine idle becomes the
// pump and runs steps until nothing is runnable — there is no background task. A step is one trigger:
//
//   - while a decision is pending: its result, if it has arrived; else an event the decision handles (from the
//     queue, then the inbox). Nothing else runs: the input that started the decision is paused.
//   - otherwise: an event queued inside the machine (step 9), then the next trigger of `_current`, then the next input.
//
// A trigger is processed on behalf of an input — its own, or, for queued events and a decision's outcome, the input
// being processed when they were queued or started. If processing throws, that input's FireAsync throws and the rest
// of it is discarded; if it had started a decision, the decision is abandoned.
public sealed partial class ReferenceMachine<TValue>
{
    private readonly object _sync = new();
    private readonly List<Work> _inbox = [];
    private readonly Queue<Work> _queue = new();
    private readonly AsyncLocal<Inside?> _inside = new();
    private Work? _current;
    private Pending? _pending;
    private bool _pumping;
    private bool _busy;

    private async ValueTask SubmitAsync(Work work)
    {
        if (Status != MachineStatus.Running)
        {
            throw new MachineNotRunningException(Status);
        }

        // Code running inside the machine that fires it would wait for itself. Caught where it is free (spec §6.6):
        // in a decision, and in any transition while a decision is pending. A Checked machine's busy flag catches the rest.
        if (_inside.Value is { } inside && (inside.Decision is not null || _pending is not null))
        {
            throw new ConcurrentUseException();
        }

        var holdsBusy = false;
        lock (_sync)
        {
            work.ArrivedWhilePending = _pending is not null;
            var acceptedWhilePending = work.Event is not null && work.ArrivedWhilePending;
            if (_model.Options.Concurrency == ConcurrencyMode.Checked && !acceptedWhilePending)
            {
                if (_busy)
                {
                    throw new ConcurrentUseException();
                }

                _busy = holdsBusy = true;
            }

            _inbox.Add(work);
        }

        try
        {
            await PumpAsync();
            await work.Done!.Task;
        }
        finally
        {
            if (holdsBusy)
            {
                lock (_sync)
                {
                    _busy = false;
                }
            }
        }
    }

    /// <summary>Runs steps until nothing is runnable, unless another caller already is.</summary>
    private async Task PumpAsync()
    {
        lock (_sync)
        {
            if (_pumping)
            {
                return;
            }

            _pumping = true;
        }

        try
        {
            while (true)
            {
                Func<Task>? step;
                lock (_sync)
                {
                    step = NextStep();
                    if (step is null)
                    {
                        _pumping = false;
                        return;
                    }
                }

                await step();
            }
        }
        catch
        {
            // Steps catch what their transitions throw; reaching here is a bug in the interpreter. Do not leave the
            // machine believing someone is still pumping.
            lock (_sync)
            {
                _pumping = false;
            }

            throw;
        }
    }

    /// <summary>The next runnable step, or <see langword="null"/>. Called under the lock.</summary>
    private Func<Task>? NextStep()
    {
        if (Status != MachineStatus.Running)
        {
            return null;
        }

        if (_pending is { } pending)
        {
            if (pending.HasResult)
            {
                return () => ApplyResultAsync(pending);
            }

            if (Take(_queue, w => pending.Handles(w.Event)) is { } queued)
            {
                return () => EventStepAsync(queued, queued.Done is null ? pending.Owner : queued);
            }

            if (_inbox.FirstOrDefault(w => pending.Handles(w.Event)) is { } handled)
            {
                _inbox.Remove(handled);
                return () => EventStepAsync(handled, handled);
            }

            return null;
        }

        if (_queue.Count > 0 && _queue.Peek().Done is not null)
        {
            // An event that waited for a decision: its caller is waiting on it, so it needs no other input to run.
            var waited = _queue.Dequeue();
            return () => EventStepAsync(waited, waited);
        }

        if (_current is null && _inbox.Count > 0)
        {
            _current = _inbox[0];
            _inbox.RemoveAt(0);
        }

        if (_current is not { } current)
        {
            return null; // queued events wait for the next input: they run before its first trigger
        }

        if (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            return () => EventStepAsync(next, next.Done is null ? current : next);
        }

        return () => ContinueAsync(current);
    }

    /// <summary>The next trigger of the current input — a run, a value or its event — or, with none left, its completion.</summary>
    private async Task ContinueAsync(Work input)
    {
        _inside.Value = Inside.Transition;
        Trigger trigger;
        if (input.Event is { } e && input.Next == 0)
        {
            trigger = Trigger.OfEvent(e);
        }
        else if (input.Event is null && input.Next < input.Values.Length)
        {
            trigger = NextRun(input.Values, input.Next);
        }
        else
        {
            lock (_sync)
            {
                _current = null;
            }

            input.Done!.TrySetResult(true);
            return;
        }

        input.Next += trigger.HasValue ? trigger.Run.Length : 1;
        try
        {
            await RunTriggerAsync(trigger, input);
        }
        catch (Exception exception)
        {
            Fail(input, exception);
        }
    }

    /// <summary>One event that is not the current input's own: queued inside the machine, or handled while a decision is pending.</summary>
    private async Task EventStepAsync(Work item, Work owner)
    {
        _inside.Value = Inside.Transition;
        try
        {
            if (!await RunTriggerAsync(Trigger.OfEvent(item.Event!), owner))
            {
                item.Done?.TrySetResult(true);
            }
        }
        catch (Exception exception)
        {
            Fail(owner, exception);
        }
        finally
        {
            ReleaseEnded();
        }
    }

    /// <summary>An input failed: its caller's FireAsync throws, and the rest of it — and any decision it started — is abandoned.</summary>
    private void Fail(Work input, Exception exception)
    {
        Pending? abandoned = null;
        lock (_sync)
        {
            if (ReferenceEquals(input, _current))
            {
                _current = null;
            }

            if (_pending is { } pending && ReferenceEquals(pending.Owner, input))
            {
                EndPending(abandoned = pending);
            }
        }

        // Cancelled before the caller hears about the failure, so it never sees its own decision still running.
        abandoned?.Cancellation.Cancel();
        input.Done?.TrySetException(exception);
        ReleaseEnded();
    }

    /// <summary>Stopping: cancel any pending decision; every waiting caller's FireAsync throws.</summary>
    private void Abandon()
    {
        List<Work> waiting;
        Pending? abandoned;
        lock (_sync)
        {
            Status = MachineStatus.Stopped;
            abandoned = _pending;
            _pending = null;
            _ended.Clear();

            waiting = [.. _inbox, .. _queue];
            if (_current is not null)
            {
                waiting.Add(_current);
            }

            _inbox.Clear();
            _queue.Clear();
            _current = null;
        }

        abandoned?.Cancellation.Cancel();
        foreach (var work in waiting)
        {
            work.Done?.TrySetException(new MachineNotRunningException(MachineStatus.Stopped));
        }
    }

    private static Work? Take(Queue<Work> queue, Func<Work, bool> match)
    {
        var found = queue.FirstOrDefault(match);
        if (found is not null)
        {
            var rest = queue.Where(w => !ReferenceEquals(w, found)).ToList();
            queue.Clear();
            foreach (var work in rest)
            {
                queue.Enqueue(work);
            }
        }

        return found;
    }

    /// <summary>One caller's input, or an event queued inside the machine (which has no caller waiting on it).</summary>
    private sealed class Work
    {
        private Work(ReadOnlyMemory<TValue> values, object? e, bool hasCaller)
        {
            Values = values;
            Event = e;
            Done = hasCaller ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        }

        public ReadOnlyMemory<TValue> Values { get; }

        public object? Event { get; }

        /// <summary>Completes when the input has been processed; <see langword="null"/> for a queued event.</summary>
        public TaskCompletionSource<bool>? Done { get; }

        /// <summary>How far processing has got: an index into <see cref="Values"/>, or 1 once the event has run.</summary>
        public int Next { get; set; }

        /// <summary>An event that arrived while a decision was pending, and so waits for it to resolve.</summary>
        public bool ArrivedWhilePending { get; set; }

        public static Work ForValues(ReadOnlyMemory<TValue> values) => new(values, null, hasCaller: true);

        public static Work ForEvent(object e) => new(default, e, hasCaller: true);

        public static Work Queued(object e) => new(default, e, hasCaller: false);
    }

    /// <summary>What the current flow is doing inside the machine: running a transition, or deciding.</summary>
    private sealed class Inside
    {
        public static readonly Inside Transition = new(null);

        public Inside(Pending? decision) => Decision = decision;

        /// <summary>The pending decision this flow is running, if it is one.</summary>
        public Pending? Decision { get; }
    }
}
```

What makes it correct, in the order a reader should check it:

- **One pump at a time.** `_pumping` is taken and released under the lock, together with the decision that there
  is nothing runnable, so an input added concurrently is either seen by the running pump or starts its own.
- **The modes differ only in `SubmitAsync`.** `Checked` holds `_busy` for the whole of a caller's `FireAsync`,
  pending decision included, and refuses anyone else — except events while a decision is pending. `Serialized`
  and `Unchecked` refuse no one; for `Unchecked`, where overlap is undefined, queueing is as good as any behaviour.
- **Self-firing.** A call from inside a decision, or from any transition while a decision is pending, is refused
  by the `AsyncLocal`; the busy flag catches the rest in a `Checked` machine. `Serialized` actions that fire their
  own machine still hang, as documented.
- **Stopping** fails every waiting caller with `MachineNotRunningException` and cancels the pending decision.

- [ ] **Step 4: Decide**

`src/StateAlchemist.Reference/Interpreter/ReferenceMachine.Decisions.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

// Decisions (spec §5.6, §6.5). A synchronous Decide runs inline: decide, then the chosen Complete, as one transition.
// A DecideAsync puts the machine in a pending state below the active leaf — every state on the active path stays,
// with its data — and pauses the input that started it. The pending state ends when the decision does: with an
// outcome (its Complete runs as a move from the leaf), with a failure (DecisionFailed fires from the leaf), or when a
// transition leaves it, which cancels the decision and drops whatever it returns later.
public sealed partial class ReferenceMachine<TValue>
{
    private readonly List<Pending> _ended = [];

    /// <summary>A synchronous decision: nothing is pending, and a throwing <c>Decide</c> becomes <see cref="DecisionFailed"/>.</summary>
    private async ValueTask<bool> DecideInlineAsync(TransitionModel decision, Trigger trigger, Work owner)
    {
        object outcome;
        try
        {
            var decide = decision.Decision!.Decide!;
            outcome = CaseOf(decision, Invoke(decide, Bind(decide, Use.Decide, PathPlanner.Stay(_leaf), trigger, default, snapshots: null)));
        }
        catch (Exception exception)
        {
            return await RunTriggerAsync(Trigger.OfEvent(new DecisionFailed(decision.Name, exception)), owner);
        }

        await CompleteAsync(decision, trigger, outcome);
        return false;
    }

    /// <summary>An async decision: enter the pending state and start <c>DecideAsync</c> with copies of what it reads.</summary>
    private void StartDecision(TransitionModel decision, Trigger trigger, Work owner)
    {
        Pending pending;
        lock (_sync)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException($"'{decision.Name}' cannot start while decision '{_pending.Decision.Name}' is pending.");
            }

            _pending = pending = new Pending(decision, trigger, owner);
        }

        var decide = decision.Decision!.DecideAsync!;
        var arguments = Bind(decide, Use.Decide, PathPlanner.Stay(_leaf), trigger, default, snapshots: null, token: pending.Cancellation.Token);
        _ = RunDecisionAsync(pending, decide, arguments);
    }

    private async Task RunDecisionAsync(Pending pending, MethodModel decide, object?[] arguments)
    {
        // Marks this flow as the decision, so that it cannot fire its own machine (spec §6.6) and so that what it
        // enqueues is kept only while it is still pending.
        _inside.Value = new Inside(pending);
        object? outcome = null;
        Exception? failure = null;
        try
        {
            outcome = CaseOf(pending.Decision, await ResultOfAsync(Invoke(decide, arguments)));
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        _inside.Value = null;
        lock (_sync)
        {
            if (!ReferenceEquals(_pending, pending) || Status != MachineStatus.Running)
            {
                return; // the pending state was left: the result cannot apply to a state that is no longer active
            }

            pending.Outcome = outcome;
            pending.Failure = failure;
        }

        await PumpAsync();
    }

    /// <summary>The decision has finished: the pending state ends, and its outcome completes — or its failure fires.</summary>
    private async Task ApplyResultAsync(Pending pending)
    {
        _inside.Value = Inside.Transition;
        lock (_sync)
        {
            EndPending(pending);
        }

        try
        {
            if (pending.Failure is { } failure)
            {
                await RunTriggerAsync(Trigger.OfEvent(new DecisionFailed(pending.Decision.Name, failure)), pending.Owner);
            }
            else
            {
                await CompleteAsync(pending.Decision, pending.Trigger, pending.Outcome!);
            }
        }
        catch (Exception exception)
        {
            Fail(pending.Owner, exception);
        }
        finally
        {
            ReleaseEnded();
        }
    }

    /// <summary>An outcome's <c>Complete</c> runs as the transform of a move from the active leaf to its target.</summary>
    private async ValueTask<bool> CompleteAsync(TransitionModel decision, Trigger trigger, object outcome)
    {
        var outcomeType = outcome.GetType().FullName;
        var completion = decision.Decision!.Completions.First(c => c.OutcomeType == outcomeType);
        var completed = decision.Completed
            .Where(m => m.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Outcome) is not { } taken || taken.TypeName == outcomeType)
            .ToList();
        var kind = completion.Target == decision.Source ? TransitionKind.Reenter : TransitionKind.Move;
        await ExecuteAsync(decision, completion.Complete, completed, PathPlanner.Move(_hierarchy, _leaf, completion.Target), kind, trigger, outcome);
        return false;
    }

    /// <summary>Leaves the pending state. Called under the lock; <see cref="ReleaseEnded"/> finishes the job outside it.</summary>
    private void EndPending(Pending pending)
    {
        if (ReferenceEquals(_pending, pending))
        {
            _pending = null;
            _ended.Add(pending);
        }
    }

    /// <summary>
    /// After a step that ended a decision: cancel it, let the events that waited for it run next — before the input it
    /// paused continues — and complete an owner that was a waiting event rather than the current input.
    /// </summary>
    private void ReleaseEnded()
    {
        List<Pending> ended;
        List<Work> finished;
        lock (_sync)
        {
            if (_ended.Count == 0)
            {
                return;
            }

            ended = [.. _ended];
            _ended.Clear();
            foreach (var waited in _inbox.Where(w => w.Event is not null && w.ArrivedWhilePending).ToList())
            {
                _inbox.Remove(waited);
                waited.ArrivedWhilePending = false;
                _queue.Enqueue(waited);
            }

            finished = ended.Select(p => p.Owner)
                .Where(owner => !ReferenceEquals(owner, _current) && !ReferenceEquals(owner, _pending?.Owner))
                .ToList();
        }

        foreach (var pending in ended)
        {
            // Not disposed: a decision still running may yet look at its token.
            pending.Cancellation.Cancel();
        }

        foreach (var owner in finished)
        {
            owner.Done?.TrySetResult(true);
        }
    }

    /// <summary>The case a decision's union holds, read from the union's <c>Value</c>.</summary>
    private static object CaseOf(TransitionModel decision, object? union) =>
        union?.GetType().GetProperty("Value")?.GetValue(union)
        ?? throw new InvalidOperationException($"'{decision.Name}' returned no outcome.");

    /// <summary>Awaits a <c>ValueTask&lt;T&gt;</c> or <c>Task&lt;T&gt;</c> known only as an object, and returns its result.</summary>
    private static async Task<object?> ResultOfAsync(object? awaitable)
    {
        var task = awaitable as Task ?? (Task)awaitable!.GetType().GetMethod("AsTask")!.Invoke(awaitable, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    /// <summary>A decision in flight: the generated pending state's slot, holding what cancels it.</summary>
    private sealed class Pending(TransitionModel decision, Trigger trigger, Work owner)
    {
        public TransitionModel Decision { get; } = decision;

        public Trigger Trigger { get; } = trigger;

        /// <summary>The input that started it: paused until it resolves, and answerable for what its outcome throws.</summary>
        public Work Owner { get; } = owner;

        public CancellationTokenSource Cancellation { get; } = new();

        public bool HasResult => Outcome is not null || Failure is not null;

        public object? Outcome { get; set; }

        public Exception? Failure { get; set; }

        /// <summary>Whether <paramref name="e"/> is an event the decision lists in <c>Handle</c>: run at once, not deferred.</summary>
        public bool Handles(object? e) => e is not null && Decision.Decision!.Handle.Contains(e.GetType().FullName!);
    }
}
```

- A synchronous `Decide` runs inline, inside the trigger's step. A throw becomes `DecisionFailed`, processed at
  once.
- `StartDecision` enters the pending state (the `_pending` field — the generated machine's pending slot) and calls
  `DecideAsync` with copies of what it reads and the pending state's token. The input's step then ends, and the
  pump finds nothing runnable but a result or a handled event.
- `RunDecisionAsync` records the result only if the decision is still the pending one, then pumps: the result is
  applied on whichever thread finished the decision, as the spec's inline-drained inbox would.
- `ReleaseEnded` runs after any step that ended a decision. It cancels the decision and moves the events that
  waited to the front of the queue, so they run before the paused input continues.

- [ ] **Step 5: Run**

`src/StateAlchemist.Reference/Interpreter/ReferenceMachine.Runs.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

// Runs (spec §6.7). The stop set is not precomputed here: a value belongs to the run exactly when resolution from the
// active leaf would try the run first for it — which is the stop set's definition (StopSets), applied value by value.
public sealed partial class ReferenceMachine<TValue>
{
    private delegate void RunTransform<TState>(ref TState self, ReadOnlySpan<TValue> run);

    private delegate void RunTransformWithConfig<TState, TConfig>(ref TState self, ReadOnlySpan<TValue> run, in TConfig config);

    private delegate void RunTransformWithContext<TState, TContext>(ref TState self, ReadOnlySpan<TValue> run, TContext context);

    private delegate void RunTransformWithBoth<TState, TConfig, TContext>(ref TState self, ReadOnlySpan<TValue> run, in TConfig config, TContext context);

    /// <summary>
    /// The trigger at <paramref name="index"/>: if the leaf would give that value to a run transition, the run reaches
    /// up to the first value it would not give the same run; otherwise it is the one value.
    /// </summary>
    private Trigger NextRun(ReadOnlyMemory<TValue> values, int index)
    {
        var span = values.Span;
        var first = _resolver.ForValue(_leaf, ToInt64(span[index]));
        var end = index + 1;
        if (first.Count > 0 && first[0].IsRun)
        {
            while (end < span.Length && _resolver.ForValue(_leaf, ToInt64(span[end])) is { Count: > 0 } next && next[0].Index == first[0].Index)
            {
                end++;
            }
        }

        return Trigger.OfRun(values.Slice(index, end - index));
    }

    /// <summary>
    /// Calls a run transform. A <c>ReadOnlySpan&lt;TValue&gt;</c> cannot be boxed, so reflection cannot pass it: the
    /// method is bound to a delegate of its exact shape — <c>(ref TState, ReadOnlySpan&lt;TValue&gt;)</c>, optionally
    /// followed by <c>in TConfig</c> and the context — and the state written back into <paramref name="arguments"/>.
    /// </summary>
    private void InvokeRun(MethodModel method, object?[] arguments, ReadOnlyMemory<TValue> run)
    {
        var info = _machine.MethodOf(method);
        var types = info.GetParameters().Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType).ToArray();
        var extras = method.Parameters.Skip(2).Select(p => p.Kind).ToArray();
        var (helper, generic) = extras switch
        {
            [] => (nameof(CallRun), new[] { types[0] }),
            [ParameterKind.Config] => (nameof(CallRunWithConfig), new[] { types[0], types[2] }),
            [ParameterKind.Context] => (nameof(CallRunWithContext), new[] { types[0], types[2] }),
            [ParameterKind.Config, ParameterKind.Context] => (nameof(CallRunWithBoth), new[] { types[0], types[2], types[3] }),
            _ => throw new NotSupportedException($"'{method.FullName}' is not a run transform (SALCH0701)."),
        };
        arguments[0] = typeof(ReferenceMachine<TValue>).GetMethod(helper, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(generic)
            .Invoke(null, BindingFlags.DoNotWrapExceptions, null, [info, arguments[0], run, arguments.Skip(2).ToArray()], null);
    }

    private static object? CallRun<TState>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        ((RunTransform<TState>)method.CreateDelegate(typeof(RunTransform<TState>)))(ref state, run.Span);
        return state;
    }

    private static object? CallRunWithConfig<TState, TConfig>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        var config = (TConfig)extras[0]!;
        ((RunTransformWithConfig<TState, TConfig>)method.CreateDelegate(typeof(RunTransformWithConfig<TState, TConfig>)))(ref state, run.Span, in config);
        return state;
    }

    private static object? CallRunWithContext<TState, TContext>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        ((RunTransformWithContext<TState, TContext>)method.CreateDelegate(typeof(RunTransformWithContext<TState, TContext>)))(ref state, run.Span, (TContext)extras[0]!);
        return state;
    }

    private static object? CallRunWithBoth<TState, TConfig, TContext>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        var config = (TConfig)extras[0]!;
        ((RunTransformWithBoth<TState, TConfig, TContext>)method.CreateDelegate(typeof(RunTransformWithBoth<TState, TConfig, TContext>)))(ref state, run.Span, in config, (TContext)extras[1]!);
        return state;
    }
}
```

A `ReadOnlySpan<TValue>` cannot be boxed into a reflection argument array, so a run transform is bound to a
delegate of its exact shape — one of the four `SALCH0701` allows — and the updated state is written back into the
argument array, where `WriteBack` finds it as it does for any `ref` state.

- [ ] **Step 6: Execute, for transitions and outcomes alike**

`src/StateAlchemist.Reference/Interpreter/ReferenceMachine.Execution.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

public sealed partial class ReferenceMachine<TValue>
{
    private enum Use
    {
        Guard,
        Transform,
        Decide,
        Completed,
        Exited,
        Entered,
    }

    /// <summary>One trigger, resolved and run. True when it started an async decision, which pauses its input.</summary>
    private async ValueTask<bool> RunTriggerAsync(Trigger trigger, Work owner)
    {
        var chosen = Choose(Candidates(trigger), trigger, hooks: true);
        if (chosen is null)
        {
            Unhandled(trigger);
            return false;
        }

        if (chosen.Decision is { } decision)
        {
            if (decision.DecideAsync is null)
            {
                return await DecideInlineAsync(chosen, trigger, owner);
            }

            StartDecision(chosen, trigger, owner);
            return true;
        }

        await ExecuteAsync(chosen, chosen.Transform, chosen.Completed, PathPlanner.Plan(_hierarchy, chosen, _leaf), (TransitionKind)chosen.Kind, trigger, outcome: null);
        return false;
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
            var info = Info(candidate, path, (TransitionKind)candidate.Kind, trigger, Phase.Guard);
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

    /// <summary>
    /// Steps 2–8 of spec §6.2, for a transition or — with <paramref name="outcome"/> — a decision outcome, whose
    /// <c>Complete</c> is the transform.
    /// </summary>
    private async ValueTask ExecuteAsync(
        TransitionModel transition,
        MethodModel? transform,
        IReadOnlyList<MethodModel> completed,
        TransitionPath path,
        TransitionKind kind,
        Trigger trigger,
        object? outcome)
    {
        var startedOver = new HashSet<int>(path.Exiting.Intersect(path.Entering));
        var snapshots = startedOver.ToDictionary(state => state, state => RuntimeHelpers.GetObjectValue(_slots[state]));

        // 2. reset the entering states
        foreach (var state in path.Entering)
        {
            _slots[state] = Cleared(state);
        }

        // 3. transform
        var info = Info(transition, path, kind, trigger, outcome is null ? Phase.Transform : Phase.Complete);
        if (transform is not null)
        {
            var arguments = Bind(transform, Use.Transform, path, trigger, info, snapshots, outcome);
            try
            {
                if (transition.IsRun)
                {
                    InvokeRun(transform, arguments, trigger.Run);
                }
                else
                {
                    Invoke(transform, arguments);
                }
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

        // 4. commit. A move leaves the pending state below the leaf, if there is one, which ends its decision.
        _leaf = path.TargetLeaf;
        if (path.Exiting.Count > 0)
        {
            lock (_sync)
            {
                if (_pending is { } pending)
                {
                    EndPending(pending);
                }
            }
        }

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

            foreach (var method in completed)
            {
                go = go && await RunActionAsync(method, Use.Completed, path, trigger, info.With(Phase.Completed), snapshots, outcome);
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
            if (!await RunActionAsync(action.Method, use, PathPlanner.Stay(_leaf), default, info, snapshots, outcome: null))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Runs one action; false means "skip the rest" (<see cref="ExceptionResolution.Skip"/>).</summary>
    private async ValueTask<bool> RunActionAsync(MethodModel method, Use use, TransitionPath path, Trigger trigger, TransitionInfo<TValue> info, IReadOnlyDictionary<int, object?>? snapshots, object? outcome)
    {
        try
        {
            switch (Invoke(method, Bind(method, use, path, trigger, info, snapshots, outcome)))
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

    private object?[] Bind(
        MethodModel method,
        Use use,
        TransitionPath path,
        Trigger trigger,
        TransitionInfo<TValue> info,
        IReadOnlyDictionary<int, object?>? snapshots,
        object? outcome = null,
        CancellationToken token = default)
    {
        var arguments = new object?[method.Parameters.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = method.Parameters[i];
            arguments[i] = parameter.Kind switch
            {
                ParameterKind.State => StateArgument(parameter, use, snapshots),
                ParameterKind.Value => trigger.Value,
                ParameterKind.Run => null, // passed as a span by InvokeRun
                ParameterKind.RunMemory => trigger.Run,
                ParameterKind.Event => trigger.Event,
                ParameterKind.Outcome => outcome,
                ParameterKind.Config => _config,
                ParameterKind.Context => _context,
                ParameterKind.CancellationToken => use == Use.Decide ? token : _lifetime.Token,
                ParameterKind.TransitionInfo => info,
                _ => throw new NotSupportedException($"Parameter '{parameter.Name}' of '{method.FullName}' cannot be bound."),
            };
        }

        return arguments;
    }

    /// <summary>
    /// Which copy of a state a parameter sees: a state being started over is read, before the change, from its
    /// snapshot — by a transform's <c>in</c> parameter, and by that state's own <c>[Exited]</c> actions. A decision
    /// gets a copy of the live slot, since it may still be running after the machine has moved on. Everything else
    /// reads the live slot.
    /// </summary>
    private object? StateArgument(ParameterModel parameter, Use use, IReadOnlyDictionary<int, object?>? snapshots)
    {
        var oldData = (use == Use.Transform && parameter.Passing == Passing.In) || use == Use.Exited;
        if (oldData && snapshots is not null && snapshots.TryGetValue(parameter.State, out var snapshot))
        {
            return RuntimeHelpers.GetObjectValue(snapshot);
        }

        return use == Use.Decide ? RuntimeHelpers.GetObjectValue(_slots[parameter.State]) : _slots[parameter.State];
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

    private TransitionInfo<TValue> Info(TransitionModel transition, TransitionPath path, TransitionKind kind, Trigger trigger, Phase phase) =>
        new(transition.Name,
            _machine.StateTypes[transition.Source],
            _machine.StateTypes[path.Leaf],
            _machine.StateTypes[path.TargetLeaf],
            kind,
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

Against Plan 2: `RunTriggerAsync` replaces `RunOneAsync` and returns whether it started a decision; `ExecuteAsync`
takes the transform, the `Completed` methods and the kind explicitly, so a decision outcome runs through it with
`Complete` as the transform; a move ends the pending decision at commit (step 4), since it leaves the pending state
below the leaf; and a decision's state parameters are copies.

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
    /// Completes when every value has been processed, including waiting for any decision one of them started. A read
    /// loop that awaits it therefore stops reading while a decision is pending, which is the backpressure.
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
    /// Called from code not running inside the machine — an action, a hook or a decision. From outside the machine,
    /// use <see cref="FireAsync{TEvent}(TEvent)"/>.
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

- [ ] **Step 7: Run the tests to see them pass — repeatedly**

```bash
dotnet test --solution StateAlchemist.slnx
for i in $(seq 1 10); do dotnet test --project tests/StateAlchemist.Reference.Tests/StateAlchemist.Reference.Tests.csproj --no-build | grep -E "failed: [1-9]"; done
```

Expected: PASS — 597 tests: 71 model, 35 runtime and 93 reference per framework — and the loop prints nothing. The
repetition is the point: a race in the pump shows up as an occasional failure, not a consistent one.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Interpreter: inputs and a pump; decisions, deferral, runs and the concurrency modes"
```

---

## Findings from validating Plan 3

Each is fixed in the spec and docs in the commit that adds this plan.

- **`DecisionFailed` is a runtime type** (Task 2), not generated: modules must be able to name it.
- **The pending state is below the active leaf**, not "a child of the source": a decision plans as a stay, which
  is also what the model always did. The two only differ for a decision declared on a parent.
- **A decision's failure fires from the active leaf, after the pending state has ended**, not "on the pending
  state" — and a synchronous `Decide` that throws does the same, where the docs had said nothing.
- **Events that waited for a decision run as soon as it resolves, before the rest of the paused input.** The docs
  said only that they wait.
- **A batch that throws** stops there, discards the rest, and cancels a decision it started. Undocumented before.
- **Self-firing is detected in every transition while a decision is pending**, not only in the decision: the
  `AsyncLocal` costs nothing when no decision is pending. Without it, a handled event's action firing its own
  machine would hang.
- **`Enqueue` from a decision** is allowed, waits like any event unless the decision handles it, and is dropped once
  the decision is no longer pending; an implementation must start processing, because the machine is idle while a
  decision runs.
- **One decision at a time**: an event handled during a decision cannot start another.
- **`[Entered]` actions run by `StartAsync` may `Enqueue`**; the events run before the first trigger.
- **The spec named the wrong exception** for `Checked` misuse (`InvalidOperationException`); it is
  `ConcurrentUseException`, which derives from it.

## Plan 3 exit gate

Before Plan 4 starts — the last gate before generated code:

- `dotnet test --solution StateAlchemist.slnx` passes on net8.0, net10.0 and net11.0 (597 tests when this plan was
  validated), repeatedly, and CI is green.
- Read `ReferenceMachine.Inbox.cs` and `ReferenceMachine.Decisions.cs` against spec §6.5, §6.6 and §6.10. They are
  now the executable statement of those sections; Plan 6 generates code that must pass the same contracts, so any
  rule here that would be expensive to generate should be challenged now.
- Confirm the backpressure contract against TNC's real read loop: it is the loop TNC will run.

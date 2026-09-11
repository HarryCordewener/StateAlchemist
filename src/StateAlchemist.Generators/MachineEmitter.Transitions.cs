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

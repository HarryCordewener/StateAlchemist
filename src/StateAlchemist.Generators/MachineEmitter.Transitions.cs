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
            WriteTransition(_model.Transitions[index], leaf);
        }
    }

    private void WriteGuard(TransitionModel transition, int leaf)
    {
        var path = PathPlanner.Plan(_hierarchy, transition, leaf);
        var call = $"{Owner(transition.Guard!)}({Arguments(transition.Guard!, Use.Guard, transition, path, startedOver: [])})";
        _w.Line();
        using (_w.Block($"private bool {GuardName(transition.Index, leaf)}({TriggerParameter(transition)})"))
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
    /// transform, commit, run the actions, clear what was left.
    /// </summary>
    private void WriteTransition(TransitionModel transition, int leaf)
    {
        var path = PathPlanner.Plan(_hierarchy, transition, leaf);
        var startedOver = new HashSet<int>(path.Exiting.Intersect(path.Entering));
        var actions = Actions(transition, path);
        var isAsync = actions.Any(a => a.Method.IsAsync);
        var skip = actions.Any(a => Implements($"On{a.Phase}Exception"));
        var done = isAsync ? "return;" : $"return default({ValueTaskType});";

        _w.Line();
        _w.Line($"// {transition.Name}: {_model.States[leaf].Name} --[{transition.Trigger}]--> {_model.States[path.TargetLeaf].Name}");
        using (_w.Block($"private {(isAsync ? "async " : "")}{ValueTaskType} {TransitionName(transition.Index, leaf)}({TriggerParameter(transition)})"))
        {
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
            if (transition.Transform is { } transform)
            {
                var call = $"{Owner(transform)}({Arguments(transform, Use.Transform, transition, path, startedOver)});";
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
                            _w.Line($"OnTransformException(exception, {Info(transition, path, "Transform")}, ref resolution);");
                            _w.Line($"if (resolution != {Rt}ExceptionResolution.Rethrow) {done}");
                        }

                        _w.Line("throw;");
                    }
                }
            }

            // 4. commit
            _w.Line($"_leaf = StateId.{_stateIds[path.TargetLeaf]};");

            var cleared = path.Exiting.Where(s => !startedOver.Contains(s)).ToList();
            if (actions.Count > 0 || cleared.Count > 0)
            {
                // 5–7. exited, entered, completed; 8. clear, whatever happens
                using (_w.Block("try"))
                {
                    foreach (var action in actions)
                    {
                        WriteAction(transition, path, startedOver, action);
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

            _w.Line($"OnTransitioned({Info(transition, path, "Completed")});");
            _w.Line(done);
        }
    }

    /// <summary>One action of the transition, and — if the application implements its phase's hook — its recovery.</summary>
    private void WriteAction(TransitionModel transition, TransitionPath path, HashSet<int> startedOver, TransitionAction action)
    {
        var call = $"{Owner(action.Method)}({Arguments(action.Method, action.Use, transition, path, startedOver, action.State)})";
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
            _w.Line($"On{action.Phase}Exception(exception, {Info(transition, path, action.Phase, action.State)}, ref resolution);");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Skip) goto skipped;");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Rethrow) throw;");
        }
    }

    /// <summary>The transition's actions in order: each exited state's, leaf first; each entered state's, outermost first; its own <c>Completed</c>.</summary>
    private List<TransitionAction> Actions(TransitionModel transition, TransitionPath path)
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

        actions.AddRange(transition.Completed.Select(m => new TransitionAction(m, Use.Completed, "Completed", -1)));
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
    private string Arguments(MethodModel method, Use use, TransitionModel transition, TransitionPath path, HashSet<int> startedOver, int actionState = -1)
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
                ParameterKind.Context => "_context",
                ParameterKind.Config => modifier + "_config",
                ParameterKind.CancellationToken => "LifetimeToken",
                ParameterKind.TransitionInfo => Info(transition, path, Phase(use), actionState),
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
        Use.Exited => "Exited",
        Use.Entered => "Entered",
        _ => "Completed",
    };

    /// <summary>The <c>TransitionInfo</c> for one phase of a transition fired from <paramref name="path"/>'s leaf.</summary>
    private string Info(TransitionModel transition, TransitionPath path, string phase, int state = -1)
    {
        var isEvent = transition.Trigger.Kind == MatchKind.Event;
        return $"new {Rt}TransitionInfo<{V}>({Literal(transition.Name)}, typeof({S(transition.Source)}), typeof({S(path.Leaf)}), typeof({S(path.TargetLeaf)}), " +
               $"{Rt}TransitionKind.{transition.Kind}, {Rt}Phase.{phase}, {(isEvent ? $"default({V})" : "value")}, {Bool(!isEvent)}, " +
               $"{(isEvent ? $"typeof({Event(transition.Trigger.EventType!)})" : "null")}, {(state < 0 ? "null" : $"typeof({S(state)})")})";
    }

    private string TriggerParameter(TransitionModel transition) =>
        transition.Trigger.Kind == MatchKind.Event ? $"{Event(transition.Trigger.EventType!)} e" : $"{V} value";

    private sealed record TransitionAction(MethodModel Method, Use Use, string Phase, int State);
}

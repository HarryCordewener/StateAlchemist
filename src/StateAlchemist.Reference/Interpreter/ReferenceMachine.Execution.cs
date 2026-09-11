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

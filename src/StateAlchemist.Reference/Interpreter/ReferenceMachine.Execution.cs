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
        if (trigger.Event is Armed armed)
        {
            return await RunTimerAsync(armed, owner);
        }

        var chosen = Choose(Candidates(trigger), trigger, hooks: true);
        if (chosen is null)
        {
            Unhandled(trigger);
            return false;
        }

        if (chosen.Join is { } join)
        {
            var payloads = Arrivals(chosen);
            payloads[join.BitOf(chosen.Trigger.EventType!)] = trigger.Event;
            if (payloads.Any(p => p is null))
            {
                return false;
            }

            // Fired: the join forgets its arrivals, and the transition reads them from the trigger.
            _joins.Remove(chosen.Name);
            trigger = trigger with { Joined = payloads };
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

        await ExecuteAsync(chosen, chosen.Transform, chosen.Completed, PlanNow(chosen), (TransitionKind)chosen.Kind, trigger, outcome: null);
        return false;
    }

    /// <summary>Step 1: the first candidate whose guard passes. Each guard that refuses is added to <paramref name="refused"/>.</summary>
    private TransitionModel? Choose(IReadOnlyList<TransitionModel> candidates, Trigger trigger, bool hooks, List<string>? refused = null)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Guard is not { } guard)
            {
                return candidate;
            }

            var path = PlanNow(candidate);
            var info = Info(candidate, path, (TransitionKind)candidate.Kind, trigger, Phase.Guard);
            try
            {
                if ((bool)Invoke(guard, Bind(guard, Use.Guard, path, trigger, info, snapshots: null))!)
                {
                    return candidate;
                }

                refused?.Add(candidate.Name);
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
        var joinSnapshots = _joins.Where(j => startedOver.Contains(SourceOf(j.Key))).ToDictionary(j => j.Key, j => (object?[])j.Value.Clone());

        // 2. reset the entering states
        foreach (var state in path.Entering)
        {
            _slots[state] = Cleared(state);
            ClearJoins(state);
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

                foreach (var (name, arrivals) in joinSnapshots)
                {
                    _joins[name] = arrivals;
                }

                if (Resolve(exception, info) == ExceptionResolution.Rethrow)
                {
                    ExceptionDispatchInfo.Capture(exception).Throw();
                }

                return;
            }

            WriteBack(transform, arguments);
        }

        // The entered states' timers' delays, which a Delay method computes from the new data. A Delay that throws is
        // a transform that throws: nothing commits.
        List<(TimerModel Timer, TimeSpan Delay)> delays;
        try
        {
            delays = Delays(path.Entering);
        }
        catch (Exception exception)
        {
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

        // 4. commit. A move leaves the pending state below the leaf, if there is one, which ends its decision. The
        // exited states' timers are cancelled, and the entered states' armed.
        _leaf = path.TargetLeaf;
        foreach (var state in path.Exiting)
        {
            _recorded[state] = path.Leaf;
        }

        if (path.Exiting.Count > 0 || path.Entering.Count > 0)
        {
            lock (_sync)
            {
                if (path.Exiting.Count > 0 && _pending is { } pending)
                {
                    EndPending(pending);
                }

                Rearm(path.Exiting, delays);
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
                ClearJoins(state);
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
                ParameterKind.Event => trigger.Joined is { } joined ? joined[JoinOf(method).BitOf(parameter.TypeName)] : trigger.Event,
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
        if (trigger.Timer is not null)
        {
            return;
        }

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
        var refused = new List<string>();
        var chosen = Choose(candidates, trigger, hooks: false, refused);
        if (chosen is null)
        {
            return refused.Count == 0 ? TransitionPlan.None : new TransitionPlan(refused);
        }

        if (chosen.Join is { } join && !WouldComplete(chosen, join))
        {
            return TransitionPlan.ForJoinArrival(chosen.Name, _machine.StateTypes[_leaf], refused);
        }

        var path = PlanNow(chosen);
        return new TransitionPlan(
            chosen.Name,
            _machine.StateTypes[path.Leaf],
            _machine.StateTypes[path.TargetLeaf],
            (TransitionKind)chosen.Kind,
            path.Exiting.Select(s => _machine.StateTypes[s]).ToList(),
            path.Entering.Select(s => _machine.StateTypes[s]).ToList(),
            chosen.IsDecision,
            refused);
    }

    /// <summary>The path <paramref name="transition"/> takes from the active leaf now, recalling what was recorded.</summary>
    private TransitionPath PlanNow(TransitionModel transition) =>
        transition.Kind == MoveKind.Stay || transition.IsDecision
            ? PathPlanner.Stay(_leaf)
            : PathPlanner.Move(_hierarchy, _leaf, transition.Target, transition.History, _recorded[transition.Target]);

    /// <summary>The arrivals recorded for <paramref name="join"/>'s join, created empty on its first.</summary>
    private object?[] Arrivals(TransitionModel join)
    {
        if (!_joins.TryGetValue(join.Name, out var payloads))
        {
            payloads = new object?[join.Join!.Events.Count];
            _joins[join.Name] = payloads;
        }

        return payloads;
    }

    /// <summary>Whether <paramref name="arriving"/>'s event would complete its join now.</summary>
    private bool WouldComplete(TransitionModel arriving, JoinModel join)
    {
        var bit = join.BitOf(arriving.Trigger.EventType!);
        return _joins.TryGetValue(arriving.Name, out var payloads)
            ? payloads.Where((p, i) => i != bit).All(p => p is not null)
            : join.Events.Count == 1;
    }

    /// <summary>Forgets the arrivals of the joins whose source is <paramref name="state"/>: they live as long as its data.</summary>
    private void ClearJoins(int state)
    {
        foreach (var name in _joins.Keys.Where(name => SourceOf(name) == state).ToList())
        {
            _joins.Remove(name);
        }
    }

    private int SourceOf(string join) => _model.Transitions.First(t => t.Name == join && t.IsJoin).Source;

    /// <summary>The join a method belongs to.</summary>
    private JoinModel JoinOf(MethodModel method) => _model.Transitions.First(t => t.Join is not null && t.Methods.Contains(method)).Join!;

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

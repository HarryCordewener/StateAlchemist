using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

// Final states, done and eventless transitions, completion, pausing and the observer. After every transition, and
// after StartAsync, the active leaf's eventless transitions are tried, nearest first; when none fires and the leaf is a
// final state just entered, its parent's done transitions are tried — or, under the root, the machine completes. Each
// transition this fires settles again, before the trigger that started the chain returns. A chain stops with an
// exception after 64 steps.
public sealed partial class ReferenceMachine<TValue>
{
    private const int MaxSettleDepth = 64;

    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan?[] _pausedLeft;
    private int _settleDepth;

    /// <inheritdoc/>
    public Task Completion => _completion.Task;

    /// <inheritdoc/>
    public IMachineObserver<TValue>? Observer { get; set; }

    /// <inheritdoc/>
    public void Pause()
    {
        if (_inside.Value is not null)
        {
            throw new InvalidOperationException("Pause is for code outside the machine.");
        }

        lock (_sync)
        {
            if (Status != MachineStatus.Running)
            {
                throw new InvalidOperationException("Only a running machine can be paused.");
            }

            Status = MachineStatus.Paused;
            var now = _time.GetUtcNow();
            for (var i = 0; i < _armed.Length; i++)
            {
                if (_armed[i] is { } armed)
                {
                    _pausedLeft[i] = armed.Due - now;
                    Disarm(i);
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Resume()
    {
        lock (_sync)
        {
            if (Status != MachineStatus.Paused)
            {
                throw new InvalidOperationException("Only a paused machine can be resumed.");
            }

            Status = MachineStatus.Running;
            var delays = new List<(TimerModel Timer, TimeSpan Delay)>();
            for (var i = 0; i < _pausedLeft.Length; i++)
            {
                if (_pausedLeft[i] is { } left)
                {
                    delays.Add((_timers[i], left < TimeSpan.Zero ? TimeSpan.Zero : left));
                    _pausedLeft[i] = null;
                }
            }

            Rearm([], delays);
        }

        _ = PumpAsync();
    }

    /// <summary>Tries what the active leaf fires on its own, after a transition into it or the start.</summary>
    private async ValueTask SettleAsync(bool entered)
    {
        if (_settleDepth >= MaxSettleDepth)
        {
            throw new InvalidOperationException($"Eventless transitions fired {MaxSettleDepth} times in a row without the machine settling: a guarded loop never ends.");
        }

        _settleDepth++;
        try
        {
            var none = new Trigger(default, false, default, null);
            if (Choose(_resolver.ForAlways(_leaf), none, hooks: true) is { } always)
            {
                await ExecuteAsync(always, always.Transform, always.Completed, PlanNow(always), (TransitionKind)always.Kind, none, outcome: null);
                return;
            }

            if (!entered || !_model.States[_leaf].IsFinal)
            {
                return;
            }

            var parent = _hierarchy.ParentOf(_leaf);
            if (parent == _hierarchy.Root)
            {
                Complete();
                return;
            }

            if (Choose(_resolver.ForDone(parent), none, hooks: true) is { } done)
            {
                await ExecuteAsync(done, done.Transform, done.Completed, PlanNow(done), (TransitionKind)done.Kind, none, outcome: null);
            }
        }
        finally
        {
            _settleDepth--;
        }
    }

    /// <summary>A final child of the root was entered: the machine refuses triggers, and its timers stop.</summary>
    private void Complete()
    {
        lock (_sync)
        {
            Status = MachineStatus.Completed;
            for (var i = 0; i < _armed.Length; i++)
            {
                Disarm(i);
            }
        }

        _completion.TrySetResult(true);
    }

    private void Observe(in TransitionInfo<TValue> transition) => Observer?.Transitioned(transition);

    private void ObserveUnhandled(Trigger trigger)
    {
        if (Observer is not { } observer)
        {
            return;
        }

        if (trigger.HasValue)
        {
            observer.UnhandledValue(StateType, trigger.Value);
        }
        else
        {
            observer.UnhandledEvent(StateType, trigger.Event!.GetType());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

// Timers (spec: [After]). A timer belongs to its source state: armed when the state is entered, at the commit of the
// transition that enters it (or by StartAsync for the initial path), and cancelled when the state is exited, by
// StopAsync, or by firing. Firing submits an input to the inbox like a caller would, except that it never holds a
// Checked machine's busy flag, and a pending decision always handles it. A firing that arrives after its timer was
// cancelled or re-armed carries a stale generation and is dropped.
public sealed partial class ReferenceMachine<TValue>
{
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<TimerModel> _timers;
    private readonly Armed?[] _armed;

    /// <summary>
    /// Each timer's delay for the states <paramref name="entering"/>, evaluated now: a <c>Delay</c> method reads the
    /// state's new data. A negative delay is treated as zero.
    /// </summary>
    private List<(TimerModel Timer, TimeSpan Delay)> Delays(IEnumerable<int> entering)
    {
        var delays = new List<(TimerModel, TimeSpan)>();
        foreach (var timer in Timers.On(_timers, entering))
        {
            TimeSpan delay;
            if (timer.Delay is { } method)
            {
                var arguments = method.Parameters.Select(p => p.Kind switch
                {
                    ParameterKind.State => _slots[p.State],
                    ParameterKind.Config => _config,
                    ParameterKind.Context => _context,
                    _ => throw new NotSupportedException($"Parameter '{p.Name}' of '{method.FullName}' cannot be bound."),
                }).ToArray();
                delay = (TimeSpan)Invoke(method, arguments)!;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }
            }
            else
            {
                delay = TimeSpan.FromTicks(timer.Milliseconds * TimeSpan.TicksPerMillisecond);
            }

            delays.Add((timer, delay));
        }

        return delays;
    }

    /// <summary>Cancels the timers of the states <paramref name="exiting"/>, then arms <paramref name="delays"/>. Called under the lock.</summary>
    private void Rearm(IEnumerable<int> exiting, List<(TimerModel Timer, TimeSpan Delay)> delays)
    {
        foreach (var timer in Timers.On(_timers, exiting))
        {
            Disarm(timer.Index);
        }

        foreach (var (timer, delay) in delays)
        {
            Disarm(timer.Index);
            var armed = new Armed(this, timer.Index, _time.GetUtcNow() + delay);
            _armed[timer.Index] = armed;

            // The callback must not carry the flow that armed it: a decision's flow marks code inside the machine.
            if (ExecutionContext.IsFlowSuppressed())
            {
                armed.Timer = _time.CreateTimer(OnElapsed, armed, delay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    armed.Timer = _time.CreateTimer(OnElapsed, armed, delay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private static void OnElapsed(object? state) => ((Armed)state!).Machine.Elapsed((Armed)state!);

    private void Disarm(int timer)
    {
        if (_armed[timer] is { } armed)
        {
            _armed[timer] = null;
            armed.Timer?.Dispose();
        }
    }

    /// <summary>A timer fired: submit it, unless it has since been cancelled or the machine has stopped.</summary>
    private void Elapsed(Armed armed)
    {
        Work work;
        lock (_sync)
        {
            if (Status != MachineStatus.Running || !ReferenceEquals(_armed[armed.Index], armed))
            {
                return;
            }

            work = Work.ForTimer(armed);
            work.ArrivedWhilePending = _pending is not null;
            _inbox.Add(work);
        }

        _ = Task.Run(async () =>
        {
            await PumpAsync();
            try
            {
                await work.Done!.Task;
            }
            catch (Exception)
            {
                // A timer has no caller to throw to: OnTimerException has seen it, and the machine carries on.
            }
        });
    }

    /// <summary>
    /// A timer's firing: if it is still the armed one, its candidates are tried at its own state, and nothing happens
    /// when every guard refuses. What escapes goes to <c>OnTimerException</c> before it fails the timer's input.
    /// </summary>
    private async ValueTask<bool> RunTimerAsync(Armed armed, Work owner)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_armed[armed.Index], armed))
            {
                return false;
            }

            _armed[armed.Index] = null;
        }

        armed.Timer?.Dispose();
        var timer = _timers[armed.Index];
        var trigger = Trigger.OfTimer(timer);
        var running = timer.Name;
        try
        {
            foreach (var candidate in timer.Candidates)
            {
                running = candidate.Name;
                if (Choose([candidate], trigger, hooks: true) is { } chosen)
                {
                    await ExecuteAsync(chosen, chosen.Transform, chosen.Completed, PathPlanner.Plan(_hierarchy, chosen, _leaf), (TransitionKind)chosen.Kind, trigger, outcome: null);
                    return false;
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            _hooks.TimerException?.Invoke(exception, running);
            ExceptionDispatchInfo.Capture(exception).Throw();
            return false;
        }
    }

    /// <summary>One arming of one timer. Identity is the generation: a later arming is a new object.</summary>
    private sealed class Armed(ReferenceMachine<TValue> machine, int index, DateTimeOffset due)
    {
        public ReferenceMachine<TValue> Machine { get; } = machine;

        public int Index { get; } = index;

        /// <summary>When it fires, by the machine's clock.</summary>
        public DateTimeOffset Due { get; } = due;

        public ITimer? Timer { get; set; }
    }
}

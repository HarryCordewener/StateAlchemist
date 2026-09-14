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
    private bool _boundaryRequested;

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
        if (input.StopAtBoundary && _boundaryRequested)
        {
            lock (_sync)
            {
                _boundaryRequested = false;
                _current = null;
            }

            input.Done!.TrySetResult(true);
            return;
        }

        if (!input.StopAtBoundary)
        {
            _boundaryRequested = false;
        }

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
        private Work(ReadOnlyMemory<TValue> values, object? e, bool hasCaller, bool stopAtBoundary = false)
        {
            Values = values;
            Event = e;
            Done = hasCaller ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            StopAtBoundary = stopAtBoundary;
        }

        public ReadOnlyMemory<TValue> Values { get; }

        public object? Event { get; }

        /// <summary>Completes when the input has been processed; <see langword="null"/> for a queued event.</summary>
        public TaskCompletionSource<bool>? Done { get; }

        /// <summary>How far processing has got: an index into <see cref="Values"/>, or 1 once the event has run.</summary>
        public int Next { get; set; }

        /// <summary>An event that arrived while a decision was pending, and so waits for it to resolve.</summary>
        public bool ArrivedWhilePending { get; set; }

        public bool StopAtBoundary { get; }

        public static Work ForValues(ReadOnlyMemory<TValue> values, bool stopAtBoundary = false) => new(values, null, hasCaller: true, stopAtBoundary);

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

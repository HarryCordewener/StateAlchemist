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

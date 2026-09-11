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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StateAlchemist;

/// <summary>
/// What a generated machine offers beyond <see cref="IBoundaryMachine{TValue}"/>: the triggers it would accept now,
/// starting in a chosen state, completion, pausing, and an observer that sees every step. A separate interface so that adding it
/// changed nothing for code that implements <see cref="IMachine{TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public interface IStatechart<TValue> : IBoundaryMachine<TValue>
    where TValue : struct
{
    /// <summary>
    /// The triggers that would fire a transition now, evaluating guards without their exception hooks and doing nothing
    /// else: values first, in order, with neighbouring values joined into ranges, then events by full name. A guard that
    /// reads an event's payload cannot be evaluated without one, so that event is listed; ask
    /// <see cref="IMachine{TValue}.Plan{TEvent}(TEvent)"/> with the payload. Timers, eventless transitions and
    /// <c>[OnDone]</c> fire on their own, and <see cref="InternalAttribute"/> events cannot be fired from outside: none
    /// is listed. Allocates the list: for interfaces and tests, not for every
    /// trigger.
    /// </summary>
    IReadOnlyList<TriggerDefinition> PermittedTriggers();

    /// <summary>
    /// Starts the machine in <typeparamref name="TState"/> instead of the root's <c>[Initial]</c> path: it enters the
    /// state's own <c>[Initial]</c> path, with fresh data, and runs the <c>[Entered]</c> actions from the root down, as
    /// <see cref="IMachine{TValue}.StartAsync"/> does. For tests that begin part-way through a machine.
    /// </summary>
    /// <typeparam name="TState">A state of this machine.</typeparam>
    /// <exception cref="ArgumentException"><typeparamref name="TState"/> is not a state of this machine.</exception>
    /// <exception cref="InvalidOperationException">The machine has already been started.</exception>
    ValueTask StartAsync<TState>()
        where TState : struct;

    /// <summary>
    /// Completes when the machine completes — enters a <see cref="FinalAttribute"/> child of the root — or stops.
    /// <see cref="IMachine{TValue}.Status"/> tells which. It never faults.
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// Pauses a running machine: it refuses triggers with <see cref="MachineNotRunningException"/> and holds its timers,
    /// each keeping the time it had left. Inputs already waiting in a serialized machine's inbox wait until
    /// <see cref="Resume"/>. A machine with an inbox finishes the transition it is running first; it also pauses the
    /// machines it invokes (<see cref="InvokeAttribute"/>), and <see cref="Resume"/> resumes them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The machine is not running, or it runs each call inline and the call
    /// comes from inside it.</exception>
    void Pause();

    /// <summary>Resumes a paused machine: it accepts triggers again, and its timers run on for the time they had left.</summary>
    /// <exception cref="InvalidOperationException">The machine is not paused.</exception>
    void Resume();

    /// <summary>
    /// Sees every transition and every unhandled trigger, or <see langword="null"/>. Set it from outside the machine,
    /// before firing it or between triggers.
    /// </summary>
    IMachineObserver<TValue>? Observer { get; set; }
}

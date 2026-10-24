using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StateAlchemist;

/// <summary>
/// What a generated machine offers beyond <see cref="IBoundaryMachine{TValue}"/>: the triggers it would accept now,
/// starting in a chosen state, pausing, and an observer that sees every step. A separate interface so that adding it
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
    /// <c>[OnDone]</c> fire on their own and are not listed. Allocates the list: for interfaces and tests, not for every
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
}

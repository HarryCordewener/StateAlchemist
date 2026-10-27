using System;

namespace StateAlchemist;

/// <summary>
/// Watches a running machine from outside: set it as <see cref="IStatechart{TValue}.Observer"/>. A debugger, a log or
/// a live diagram can follow the machine without the machine declaring hooks for it.
/// </summary>
/// <remarks>
/// Its methods run on the machine's thread, inside the machine, after the step they report: keep them short, and do
/// not fire the machine from them. A machine with no observer pays one null check per step.
/// </remarks>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public interface IMachineObserver<TValue>
    where TValue : struct
{
    /// <summary>A transition completed.</summary>
    /// <param name="transition">What fired, from where to where; its <see cref="TransitionInfo{TValue}.Phase"/> is <see cref="Phase.Completed"/>.</param>
    void Transitioned(in TransitionInfo<TValue> transition);

    /// <summary>A value nothing handles.</summary>
    /// <param name="state">The active leaf.</param>
    /// <param name="value">The value.</param>
    void UnhandledValue(Type state, TValue value);

    /// <summary>An event nothing handles.</summary>
    /// <param name="state">The active leaf.</param>
    /// <param name="eventType">The event's type.</param>
    void UnhandledEvent(Type state, Type eventType);
}

using System;

namespace StateAlchemist;

/// <summary>
/// Keeps an event that arrives while the state is active, instead of dropping it, until the machine leaves the state.
/// </summary>
/// <remarks>
/// Resolution walks from the active leaf to the root, as it does for any event: the first state that either handles
/// the event or defers it decides. A deferred event's <c>FireAsync</c> completes at once. When a later transition
/// moves the machine, every deferred event is queued again, ahead of the events that transition's actions queue, and
/// is resolved afresh in the new state. A state may not both handle and defer the same event (diagnostic
/// <c>SALCH0112</c>).
/// </remarks>
/// <param name="eventType">The event type to defer.</param>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class DeferAttribute(Type eventType) : Attribute
{
    /// <summary>The event type deferred.</summary>
    public Type EventType { get; } = eventType;
}

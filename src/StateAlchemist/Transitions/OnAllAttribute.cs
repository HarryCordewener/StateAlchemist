using System;

namespace StateAlchemist;

/// <summary>
/// A join: fires the transition once an event of each of <paramref name="eventTypes"/> has arrived while its
/// <c>From</c> state is active, in any order.
/// </summary>
/// <remarks>
/// Each arrival is recorded with the <c>From</c> state's data: entering or leaving that state forgets them, and so
/// does the join firing. An event that arrives again before the join fires replaces the payload recorded for it.
/// The transition's <c>Transform</c> and <c>Completed</c> may take any of the listed events, and receive the payload
/// recorded for each.
/// </remarks>
/// <param name="eventTypes">Two to thirty-two distinct structs implementing <see cref="IEvent"/>.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OnAllAttribute(params Type[] eventTypes) : Attribute
{
    /// <summary>The event types, as declared.</summary>
    public Type[] EventTypes { get; } = eventTypes;
}

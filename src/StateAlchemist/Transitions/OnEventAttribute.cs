using System;

namespace StateAlchemist;

/// <summary>Fires the transition on an event of type <paramref name="eventType"/>.</summary>
/// <param name="eventType">A struct implementing <see cref="IEvent"/>.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OnEventAttribute(Type eventType) : Attribute
{
    /// <summary>The event type.</summary>
    public Type EventType { get; } = eventType;
}

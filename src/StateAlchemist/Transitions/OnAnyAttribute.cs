using System;

namespace StateAlchemist;

/// <summary>
/// Fires the transition on any value the state does not handle more specifically — the "or else" of a state.
/// </summary>
/// <remarks>
/// It is tried after the state's exact values and ranges and <em>before</em> any ancestor's transitions, so it
/// shadows everything the ancestors do with values. It never matches an event.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OnAnyAttribute : Attribute
{
}

using System;

namespace StateAlchemist;

/// <summary>
/// Runs the method after the machine has left <paramref name="state"/>, whichever transition left it. Runs after
/// the state change commits, innermost state first, before any <see cref="EnteredAttribute"/> action.
/// </summary>
/// <param name="state">The state being left.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ExitedAttribute(Type state) : Attribute
{
    /// <summary>The state being left.</summary>
    public Type State { get; } = state;

    /// <summary>Order among actions for the same state from different modules; lower first.</summary>
    public int Order { get; set; }
}

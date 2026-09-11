using System;

namespace StateAlchemist;

/// <summary>
/// Runs the method after the machine has entered <paramref name="state"/>, whichever transition entered it. Runs
/// after every <see cref="ExitedAttribute"/> action, outermost state first, before the transition's <c>Completed</c>.
/// </summary>
/// <param name="state">The state entered.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class EnteredAttribute(Type state) : Attribute
{
    /// <summary>The state entered.</summary>
    public Type State { get; } = state;

    /// <summary>Order among actions for the same state from different modules; lower first.</summary>
    public int Order { get; set; }
}

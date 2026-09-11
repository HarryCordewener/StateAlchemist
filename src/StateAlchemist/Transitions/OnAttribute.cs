using System;

namespace StateAlchemist;

/// <summary>Fires the transition on one value. Repeat the attribute for several values.</summary>
/// <param name="value">A constant of the machine's value type, or an integer that fits it.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class OnAttribute(object value) : Attribute
{
    /// <summary>The value.</summary>
    public object Value { get; } = value;
}

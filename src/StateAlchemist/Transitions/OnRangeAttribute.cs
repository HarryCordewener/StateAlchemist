using System;

namespace StateAlchemist;

/// <summary>Fires the transition on any value from <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
/// <param name="from">The first value.</param>
/// <param name="to">The last value.</param>
/// <remarks>Within one state, a transition on an exact value is tried before a range containing it.</remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class OnRangeAttribute(object from, object to) : Attribute
{
    /// <summary>The first value.</summary>
    public object From { get; } = from;

    /// <summary>The last value.</summary>
    public object To { get; } = to;
}

using System;

namespace StateAlchemist;

/// <summary>
/// Declares a transition. On a <c>public static void</c> method, the method is the transition's
/// <c>Transform</c>. On a <c>public static class</c>, the class's methods are the transition's phases, named for
/// when they run (see <see cref="PhaseNames"/>).
/// </summary>
/// <remarks>
/// Without <see cref="To"/> the transition is a <em>stay</em>: nothing exits or enters, and the source's data
/// persists. With <see cref="To"/> equal to <see cref="From"/> it is a <em>re-entry</em>, which clears the data.
/// The trigger comes from <see cref="OnAttribute"/>, <see cref="OnRangeAttribute"/>, <see cref="OnAnyAttribute"/>
/// or <see cref="OnEventAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TransitionAttribute : Attribute
{
    /// <summary>The source state. A transition declared on a parent applies to every leaf beneath it. Required.</summary>
    public Type? From { get; set; }

    /// <summary>The target state, or <see langword="null"/> for a stay.</summary>
    public Type? To { get; set; }

    /// <summary>Where this transition is tried among guarded transitions for the same source and trigger; lower first.</summary>
    public int Order { get; set; }
}

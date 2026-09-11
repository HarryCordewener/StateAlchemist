using System;

namespace StateAlchemist;

/// <summary>
/// Declares a decision: a class-form transition whose <c>Decide</c> or <c>DecideAsync</c> returns a union of
/// outcomes, each completed by a <c>Complete</c> overload marked with <see cref="ToAttribute"/>.
/// </summary>
/// <remarks>
/// A <c>DecideAsync</c> parks the machine in a generated pending state and defers further triggers until it
/// completes; leaving the pending state cancels it (spec §6.5).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecisionAttribute : Attribute
{
    /// <summary>The source state. Required.</summary>
    public Type? From { get; set; }

    /// <summary>Where this decision is tried among guarded transitions for the same source and trigger; lower first.</summary>
    public int Order { get; set; }

    /// <summary>Event types the pending state handles immediately instead of deferring, such as a disconnect.</summary>
    public Type[] Handle { get; set; } = [];
}

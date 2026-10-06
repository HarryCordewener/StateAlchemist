using System;
using System.Collections.Generic;

namespace StateAlchemist;

/// <summary>
/// What a trigger would do, from <c>Plan</c>: which transition, which states it exits and enters, and which guarded
/// transitions were tried before it and refused.
/// </summary>
public sealed class TransitionPlan
{
    private TransitionPlan()
    {
        Exiting = [];
        Entering = [];
        Refused = [];
    }

    /// <summary>Creates a plan for a trigger nothing handles, after the guards of <paramref name="refused"/> returned <see langword="false"/>.</summary>
    /// <param name="refused">The guarded transitions whose guard returned <see langword="false"/>, in the order they were tried.</param>
    public TransitionPlan(IReadOnlyList<string> refused)
    {
        Exiting = [];
        Entering = [];
        Refused = refused;
    }

    /// <summary>Creates a plan for a handled trigger.</summary>
    /// <param name="transition">The transition that would fire.</param>
    /// <param name="leaf">The active leaf.</param>
    /// <param name="target">The leaf it would end in.</param>
    /// <param name="kind">Stay, move or re-entry.</param>
    /// <param name="exiting">States it would exit, innermost first.</param>
    /// <param name="entering">States it would enter, outermost first.</param>
    /// <param name="isDecision">Whether it is a decision, whose outcome is not known until it runs.</param>
    public TransitionPlan(string transition, Type leaf, Type target, TransitionKind kind, IReadOnlyList<Type> exiting, IReadOnlyList<Type> entering, bool isDecision)
        : this(transition, leaf, target, kind, exiting, entering, isDecision, [])
    {
    }

    /// <summary>Creates a plan for a handled trigger, tried after the guards of <paramref name="refused"/> returned <see langword="false"/>.</summary>
    /// <param name="transition">The transition that would fire.</param>
    /// <param name="leaf">The active leaf.</param>
    /// <param name="target">The leaf it would end in.</param>
    /// <param name="kind">Stay, move or re-entry.</param>
    /// <param name="exiting">States it would exit, innermost first.</param>
    /// <param name="entering">States it would enter, outermost first.</param>
    /// <param name="isDecision">Whether it is a decision, whose outcome is not known until it runs.</param>
    /// <param name="refused">The guarded transitions tried before it whose guard returned <see langword="false"/>, in the order they were tried.</param>
    public TransitionPlan(string transition, Type leaf, Type target, TransitionKind kind, IReadOnlyList<Type> exiting, IReadOnlyList<Type> entering, bool isDecision, IReadOnlyList<string> refused)
    {
        Handled = true;
        Transition = transition;
        Leaf = leaf;
        Target = target;
        Kind = kind;
        Exiting = exiting;
        Entering = entering;
        IsDecision = isDecision;
        Refused = refused;
    }

    /// <summary>The plan for a trigger nothing handles, where no guard was tried.</summary>
    public static TransitionPlan None { get; } = new();

    /// <summary>Whether any transition would fire.</summary>
    public bool Handled { get; }

    /// <summary>The transition that would fire.</summary>
    public string? Transition { get; }

    /// <summary>The active leaf.</summary>
    public Type? Leaf { get; }

    /// <summary>The leaf it would end in. For a decision, the pending state.</summary>
    public Type? Target { get; }

    /// <summary>Stay, move or re-entry.</summary>
    public TransitionKind Kind { get; }

    /// <summary>States it would exit, innermost first.</summary>
    public IReadOnlyList<Type> Exiting { get; }

    /// <summary>States it would enter, outermost first.</summary>
    public IReadOnlyList<Type> Entering { get; }

    /// <summary>Whether it is a decision, whose outcome is not known until it runs.</summary>
    public bool IsDecision { get; }

    /// <summary>
    /// The guarded transitions whose guard returned <see langword="false"/>, in the order they were tried; empty when
    /// none was. For a handled trigger these are the ones tried before <see cref="Transition"/>.
    /// </summary>
    public IReadOnlyList<string> Refused { get; }
}

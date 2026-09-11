using System;
using System.Collections.Generic;

namespace StateAlchemist;

/// <summary>What a trigger would do, from <c>Plan</c>: which transition, and which states it exits and enters.</summary>
public sealed class TransitionPlan
{
    private TransitionPlan()
    {
        Exiting = [];
        Entering = [];
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
    {
        Handled = true;
        Transition = transition;
        Leaf = leaf;
        Target = target;
        Kind = kind;
        Exiting = exiting;
        Entering = entering;
        IsDecision = isDecision;
    }

    /// <summary>The plan for a trigger nothing handles.</summary>
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
}

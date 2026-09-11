using System;

namespace StateAlchemist;

/// <summary>
/// Describes a transition while it runs: passed to hooks and to actions that take it.
/// </summary>
/// <typeparam name="TValue">The machine's value-trigger type.</typeparam>
public readonly struct TransitionInfo<TValue>
    where TValue : struct
{
    /// <summary>Creates a description.</summary>
    /// <param name="transition">The declaring member, such as <c>NawsModule.Capture</c>.</param>
    /// <param name="source">The declared source state.</param>
    /// <param name="leaf">The active leaf when the trigger fired.</param>
    /// <param name="target">The leaf after the transition; <paramref name="leaf"/> for a stay.</param>
    /// <param name="kind">Stay, move or re-entry.</param>
    /// <param name="phase">The phase now running.</param>
    /// <param name="value">The value that fired, when <paramref name="hasValue"/>.</param>
    /// <param name="hasValue">Whether a value fired, rather than an event.</param>
    /// <param name="eventType">The event type that fired, if an event did.</param>
    /// <param name="state">During <see cref="Phase.Exited"/> or <see cref="Phase.Entered"/>: the state whose action is running.</param>
    public TransitionInfo(string transition, Type source, Type leaf, Type target, TransitionKind kind, Phase phase, TValue value, bool hasValue, Type? eventType, Type? state)
    {
        Transition = transition;
        Source = source;
        Leaf = leaf;
        Target = target;
        Kind = kind;
        Phase = phase;
        Value = value;
        HasValue = hasValue;
        EventType = eventType;
        State = state;
    }

    /// <summary>The declaring member, such as <c>NawsModule.Capture</c>.</summary>
    public string Transition { get; }

    /// <summary>The declared source state.</summary>
    public Type Source { get; }

    /// <summary>The active leaf when the trigger fired.</summary>
    public Type Leaf { get; }

    /// <summary>The leaf after the transition; <see cref="Leaf"/> for a stay.</summary>
    public Type Target { get; }

    /// <summary>Stay, move or re-entry.</summary>
    public TransitionKind Kind { get; }

    /// <summary>The phase now running.</summary>
    public Phase Phase { get; }

    /// <summary>The value that fired, when <see cref="HasValue"/>.</summary>
    public TValue Value { get; }

    /// <summary>Whether a value fired, rather than an event.</summary>
    public bool HasValue { get; }

    /// <summary>The event type that fired, if an event did.</summary>
    public Type? EventType { get; }

    /// <summary>During <see cref="Phase.Exited"/> or <see cref="Phase.Entered"/>: the state whose action is running.</summary>
    public Type? State { get; }

    /// <summary>The same transition at another phase.</summary>
    /// <param name="phase">The phase.</param>
    /// <param name="state">The state whose action is running, for <see cref="Phase.Exited"/> and <see cref="Phase.Entered"/>.</param>
    public TransitionInfo<TValue> With(Phase phase, Type? state = null) =>
        new(Transition, Source, Leaf, Target, Kind, phase, Value, HasValue, EventType, state);

    /// <summary>For logs: <c>NawsModule.Escape: Naws --[255]--&gt; NawsEscaping (Transform)</c>.</summary>
    public override string ToString() =>
        $"{Transition}: {Leaf.Name} --[{(HasValue ? Value.ToString() : EventType?.Name)}]--> {Target.Name} ({Phase})";
}

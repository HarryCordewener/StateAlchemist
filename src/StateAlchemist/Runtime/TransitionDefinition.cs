using System;
using System.Collections.Generic;

namespace StateAlchemist;

/// <summary>A transition in a <see cref="MachineDefinition"/>.</summary>
/// <param name="index">The transition's position in <see cref="MachineDefinition.Transitions"/>.</param>
/// <param name="name">The declaring member, such as <c>NawsModule.Capture</c>.</param>
/// <param name="source">The source state's index.</param>
/// <param name="target">The target state's index, or −1 for a stay.</param>
/// <param name="kind">Stay, move or re-entry.</param>
/// <param name="trigger">What it fires on.</param>
/// <param name="order">Its order among guarded transitions for the same trigger.</param>
/// <param name="hasGuard">Whether it declares a <c>Guard</c>.</param>
/// <param name="isRun">Whether it is a run transition.</param>
/// <param name="usesContext">Whether its guard or transform takes the context.</param>
/// <param name="isDecision">Whether it is a decision.</param>
/// <param name="outcomes">A decision's outcomes and where each goes; empty for anything else.</param>
public sealed class TransitionDefinition(
    int index,
    string name,
    int source,
    int target,
    TransitionKind kind,
    TriggerDefinition trigger,
    int order,
    bool hasGuard,
    bool isRun,
    bool usesContext,
    bool isDecision,
    IReadOnlyList<OutcomeDefinition> outcomes)
{
    /// <summary>Creates a transition with no outcomes.</summary>
    /// <param name="index">The transition's position in <see cref="MachineDefinition.Transitions"/>.</param>
    /// <param name="name">The declaring member, such as <c>NawsModule.Capture</c>.</param>
    /// <param name="source">The source state's index.</param>
    /// <param name="target">The target state's index, or −1 for a stay.</param>
    /// <param name="kind">Stay, move or re-entry.</param>
    /// <param name="trigger">What it fires on.</param>
    /// <param name="order">Its order among guarded transitions for the same trigger.</param>
    /// <param name="hasGuard">Whether it declares a <c>Guard</c>.</param>
    /// <param name="isRun">Whether it is a run transition.</param>
    /// <param name="usesContext">Whether its guard or transform takes the context.</param>
    /// <param name="isDecision">Whether it is a decision.</param>
    public TransitionDefinition(
        int index,
        string name,
        int source,
        int target,
        TransitionKind kind,
        TriggerDefinition trigger,
        int order,
        bool hasGuard,
        bool isRun,
        bool usesContext,
        bool isDecision)
        : this(index, name, source, target, kind, trigger, order, hasGuard, isRun, usesContext, isDecision, new OutcomeDefinition[0])
    {
    }

    /// <summary>Creates one event's transition of a join (<c>[OnAll]</c>).</summary>
    /// <param name="index">The transition's position in <see cref="MachineDefinition.Transitions"/>.</param>
    /// <param name="name">The declaring member, such as <c>Checkout.Ship</c>.</param>
    /// <param name="source">The source state's index.</param>
    /// <param name="target">The target state's index, or −1 for a stay.</param>
    /// <param name="kind">Stay, move or re-entry.</param>
    /// <param name="trigger">The one event of the join this transition is for.</param>
    /// <param name="order">Its order among guarded transitions for the same trigger.</param>
    /// <param name="usesContext">Whether its transform takes the context.</param>
    /// <param name="joins">Every event the join waits for, as declared.</param>
    public TransitionDefinition(
        int index,
        string name,
        int source,
        int target,
        TransitionKind kind,
        TriggerDefinition trigger,
        int order,
        bool usesContext,
        IReadOnlyList<Type> joins)
        : this(index, name, source, target, kind, trigger, order, false, false, usesContext, false, new OutcomeDefinition[0])
    {
        Joins = joins ?? throw new ArgumentNullException(nameof(joins));
    }

    /// <summary>The transition's position in <see cref="MachineDefinition.Transitions"/>.</summary>
    public int Index { get; } = index;

    /// <summary>The declaring member, such as <c>NawsModule.Capture</c>.</summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>The source state's index.</summary>
    public int Source { get; } = source;

    /// <summary>The target state's index, or −1 for a stay.</summary>
    public int Target { get; } = target;

    /// <summary>Stay, move or re-entry.</summary>
    public TransitionKind Kind { get; } = kind;

    /// <summary>What it fires on.</summary>
    public TriggerDefinition Trigger { get; } = trigger;

    /// <summary>Its order among guarded transitions for the same trigger.</summary>
    public int Order { get; } = order;

    /// <summary>Whether it declares a <c>Guard</c>.</summary>
    public bool HasGuard { get; } = hasGuard;

    /// <summary>Whether it is a run transition.</summary>
    public bool IsRun { get; } = isRun;

    /// <summary>Whether its guard or transform takes the context (decision D13).</summary>
    public bool UsesContext { get; } = usesContext;

    /// <summary>Whether it is a decision.</summary>
    public bool IsDecision { get; } = isDecision;

    /// <summary>
    /// A decision's outcomes and where each one goes. Empty for every other transition, and for a decision whose
    /// outcomes the front-end could not resolve.
    /// </summary>
    public IReadOnlyList<OutcomeDefinition> Outcomes { get; } = outcomes ?? throw new ArgumentNullException(nameof(outcomes));

    /// <summary>
    /// For a join (<c>[OnAll]</c>), every event it waits for, as declared; empty for every other transition. A join
    /// is one transition per event, each with that event as its <see cref="Trigger"/> and the same
    /// <see cref="Name"/>.
    /// </summary>
    public IReadOnlyList<Type> Joins { get; } = new Type[0];

    /// <summary>Whether it is one event's transition of a join.</summary>
    public bool IsJoin => Joins.Count > 0;
}

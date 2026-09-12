using System;

namespace StateAlchemist;

/// <summary>
/// One outcome of a decision: a case its reader can answer with, and the state that answer moves to. A decision
/// itself is one <see cref="TransitionDefinition"/> whose target is not known when the trigger arrives; these are
/// where it can end up, so a diagram drawn from a <see cref="MachineDefinition"/> can show states that only a
/// decision reaches.
/// </summary>
/// <param name="type">The outcome case's type, such as the <c>Allowed</c> of an <c>Answer</c> union.</param>
/// <param name="target">The target state's index.</param>
public sealed class OutcomeDefinition(Type type, int target)
{
    /// <summary>The outcome case's type.</summary>
    public Type Type { get; } = type ?? throw new ArgumentNullException(nameof(type));

    /// <summary>The outcome case's short name, such as <c>Allowed</c>.</summary>
    public string Name => Type.Name;

    /// <summary>The target state's index: where the decision goes when its reader answers with this case.</summary>
    public int Target { get; } = target;
}

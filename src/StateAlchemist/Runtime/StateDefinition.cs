using System;

namespace StateAlchemist;

/// <summary>A state in a <see cref="MachineDefinition"/>.</summary>
/// <param name="index">The state's position in <see cref="MachineDefinition.States"/>.</param>
/// <param name="type">The state struct.</param>
/// <param name="parent">The parent's index, or −1 for the root.</param>
/// <param name="isInitial">Whether the state is its parent's <c>[Initial]</c> child.</param>
public sealed class StateDefinition(int index, Type type, int parent, bool isInitial)
{
    /// <summary>The state's position in <see cref="MachineDefinition.States"/>.</summary>
    public int Index { get; } = index;

    /// <summary>The state struct.</summary>
    public Type Type { get; } = type ?? throw new ArgumentNullException(nameof(type));

    /// <summary>The state's short name.</summary>
    public string Name => Type.Name;

    /// <summary>The parent's index, or −1 for the root.</summary>
    public int Parent { get; } = parent;

    /// <summary>Whether the state is its parent's <c>[Initial]</c> child.</summary>
    public bool IsInitial { get; } = isInitial;

    /// <summary>Whether this is the root.</summary>
    public bool IsRoot => Parent < 0;
}

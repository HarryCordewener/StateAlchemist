using System;

namespace StateAlchemist;

/// <summary>A state in a <see cref="MachineDefinition"/>.</summary>
public sealed class StateDefinition
{
    /// <summary>A state that is not final.</summary>
    /// <param name="index">The state's position in <see cref="MachineDefinition.States"/>.</param>
    /// <param name="type">The state struct.</param>
    /// <param name="parent">The parent's index, or −1 for the root.</param>
    /// <param name="isInitial">Whether the state is its parent's <c>[Initial]</c> child.</param>
    public StateDefinition(int index, Type type, int parent, bool isInitial)
        : this(index, type, parent, isInitial, isFinal: false)
    {
    }

    /// <summary>A state.</summary>
    /// <param name="index">The state's position in <see cref="MachineDefinition.States"/>.</param>
    /// <param name="type">The state struct.</param>
    /// <param name="parent">The parent's index, or −1 for the root.</param>
    /// <param name="isInitial">Whether the state is its parent's <c>[Initial]</c> child.</param>
    /// <param name="isFinal">Whether the state is <c>[Final]</c>.</param>
    public StateDefinition(int index, Type type, int parent, bool isInitial, bool isFinal)
    {
        Index = index;
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Parent = parent;
        IsInitial = isInitial;
        IsFinal = isFinal;
    }

    /// <summary>The state's position in <see cref="MachineDefinition.States"/>.</summary>
    public int Index { get; }

    /// <summary>The state struct.</summary>
    public Type Type { get; }

    /// <summary>The state's short name.</summary>
    public string Name => Type.Name;

    /// <summary>The parent's index, or −1 for the root.</summary>
    public int Parent { get; }

    /// <summary>Whether the state is its parent's <c>[Initial]</c> child.</summary>
    public bool IsInitial { get; }

    /// <summary>Whether the state is <c>[Final]</c>: entering it completes its parent.</summary>
    public bool IsFinal { get; }

    /// <summary>Whether this is the root.</summary>
    public bool IsRoot => Parent < 0;
}

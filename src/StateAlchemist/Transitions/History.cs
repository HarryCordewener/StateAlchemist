namespace StateAlchemist;

/// <summary>
/// Which child a move into a state with children enters: its <c>[Initial]</c> path, or what was active when the
/// state was last exited. Set with <see cref="TransitionAttribute.History"/> or <see cref="ToAttribute.History"/>.
/// </summary>
/// <remarks>
/// History restores which state is active, not its data: every state the move enters starts with reset data. Before
/// the target has been exited once, a move with history enters its <c>[Initial]</c> path.
/// </remarks>
public enum History
{
    /// <summary>Enter the target's <c>[Initial]</c> path.</summary>
    None,

    /// <summary>Enter the child of the target that was active when the target was last exited, then that child's <c>[Initial]</c> path.</summary>
    Shallow,

    /// <summary>Enter the leaf under the target that was active when the target was last exited.</summary>
    Deep,
}

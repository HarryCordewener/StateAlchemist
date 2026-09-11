namespace StateAlchemist;

/// <summary>A point in a transition, in the order they run (spec §6.2).</summary>
public enum Phase
{
    /// <summary>Deciding whether the transition may fire; read-only.</summary>
    Guard,

    /// <summary>Turning the source into the target, before the state changes.</summary>
    Transform,

    /// <summary>A decision choosing its outcome.</summary>
    Decide,

    /// <summary>A decision outcome's transform, before the state changes.</summary>
    Complete,

    /// <summary>A state's <see cref="ExitedAttribute"/> action, after the state has changed.</summary>
    Exited,

    /// <summary>A state's <see cref="EnteredAttribute"/> action, after every exit action.</summary>
    Entered,

    /// <summary>The transition's own <c>Completed</c>, last.</summary>
    Completed,
}

namespace StateAlchemist;

/// <summary>What a transition does to the active path.</summary>
public enum TransitionKind
{
    /// <summary>Nothing exits or enters; the source's data persists.</summary>
    Stay,

    /// <summary>Exits to the lowest common ancestor and enters the target.</summary>
    Move,

    /// <summary>Exits the source and enters it again, clearing its data.</summary>
    Reenter,
}

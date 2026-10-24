namespace StateAlchemist;

/// <summary>How a transition's trigger matches.</summary>
public enum TriggerKind
{
    /// <summary>One value.</summary>
    Value,

    /// <summary>An inclusive range of values.</summary>
    Range,

    /// <summary>Any value the state does not handle more specifically.</summary>
    Any,

    /// <summary>An event type.</summary>
    Event,

    /// <summary>A timer that belongs to the source state: see <see cref="AfterAttribute"/>.</summary>
    Timer,

    /// <summary>No trigger: the transition fires as soon as its guard passes. See <see cref="AlwaysAttribute"/>.</summary>
    Always,

    /// <summary>Its source state is done. See <see cref="OnDoneAttribute"/>.</summary>
    Done,
}

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
}

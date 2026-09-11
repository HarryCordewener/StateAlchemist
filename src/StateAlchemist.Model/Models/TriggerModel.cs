using System.Globalization;

namespace StateAlchemist.Model;

/// <summary>What a transition fires on.</summary>
/// <param name="Kind">How it matches.</param>
/// <param name="Low">The value, or a range's first value.</param>
/// <param name="High">The value, or a range's last value.</param>
/// <param name="EventType">An event trigger's event type's full name.</param>
public readonly record struct TriggerModel(MatchKind Kind, long Low, long High, string? EventType)
{
    /// <summary>Any value the state does not handle more specifically.</summary>
    public static TriggerModel Any { get; } = new(MatchKind.Any, 0, 0, null);

    /// <summary>One value.</summary>
    public static TriggerModel Value(long value) => new(MatchKind.Value, value, value, null);

    /// <summary>An inclusive range.</summary>
    public static TriggerModel Range(long low, long high) => new(MatchKind.Range, low, high, null);

    /// <summary>An event.</summary>
    public static TriggerModel Event(string eventType) => new(MatchKind.Event, 0, 0, eventType);

    /// <summary>Whether it matches values rather than an event.</summary>
    public bool IsValue => Kind != MatchKind.Event;

    /// <summary>Whether it matches <paramref name="value"/>.</summary>
    public bool Matches(long value) => Kind switch
    {
        MatchKind.Value => value == Low,
        MatchKind.Range => value >= Low && value <= High,
        MatchKind.Any => true,
        _ => false,
    };

    /// <summary>Whether some trigger matches both.</summary>
    public bool Overlaps(TriggerModel other) => (Kind, other.Kind) switch
    {
        (MatchKind.Event, MatchKind.Event) => EventType == other.EventType,
        (MatchKind.Event, _) or (_, MatchKind.Event) => false,
        (MatchKind.Any, _) or (_, MatchKind.Any) => true,
        _ => Low <= other.High && other.Low <= High,
    };

    /// <summary><c>31</c>, <c>32..126</c>, <c>any</c>, or <c>event Error</c>.</summary>
    public override string ToString() => Kind switch
    {
        MatchKind.Value => Low.ToString(CultureInfo.InvariantCulture),
        MatchKind.Range => string.Format(CultureInfo.InvariantCulture, "{0}..{1}", Low, High),
        MatchKind.Any => "any",
        _ => "event " + ShortName(EventType ?? string.Empty),
    };

    private static string ShortName(string typeName)
    {
        var at = typeName.LastIndexOfAny(['.', '+']);
        return at < 0 ? typeName : typeName.Substring(at + 1);
    }
}

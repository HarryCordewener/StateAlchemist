using System.Globalization;

namespace StateAlchemist.Model;

/// <summary>What a transition fires on.</summary>
/// <param name="Kind">How it matches.</param>
/// <param name="Low">The value, or a range's first value.</param>
/// <param name="High">The value, or a range's last value.</param>
/// <param name="EventType">An event trigger's event type's full name.</param>
/// <param name="DelayedBy">
/// A timer whose <c>Delay</c> method computes its delay: that transition's name, since each such timer is its own.
/// A timer with a fixed delay has its milliseconds in <paramref name="Low"/> and <paramref name="High"/> instead.
/// </param>
public readonly record struct TriggerModel(MatchKind Kind, long Low, long High, string? EventType, string? DelayedBy = null)
{
    /// <summary>The longest delay a timer accepts, in milliseconds: what <c>ITimer.Change</c> takes.</summary>
    public const long MaxDelayMilliseconds = 4294967294;

    /// <summary>Any value the state does not handle more specifically.</summary>
    public static TriggerModel Any { get; } = new(MatchKind.Any, 0, 0, null);

    /// <summary>One value.</summary>
    public static TriggerModel Value(long value) => new(MatchKind.Value, value, value, null);

    /// <summary>An inclusive range.</summary>
    public static TriggerModel Range(long low, long high) => new(MatchKind.Range, low, high, null);

    /// <summary>An event.</summary>
    public static TriggerModel Event(string eventType) => new(MatchKind.Event, 0, 0, eventType);

    /// <summary>A timer with a fixed delay.</summary>
    public static TriggerModel Timer(long milliseconds) => new(MatchKind.Timer, milliseconds, milliseconds, null);

    /// <summary>A timer whose delay <paramref name="transition"/>'s <c>Delay</c> method computes.</summary>
    public static TriggerModel TimerByDelay(string transition) => new(MatchKind.Timer, 0, 0, null, transition);

    /// <summary>Whether it matches values rather than an event or a timer.</summary>
    public bool IsValue => Kind is MatchKind.Value or MatchKind.Range or MatchKind.Any;

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
        (MatchKind.Timer, MatchKind.Timer) => Low == other.Low && DelayedBy == other.DelayedBy,
        (MatchKind.Event, _) or (_, MatchKind.Event) or (MatchKind.Timer, _) or (_, MatchKind.Timer) => false,
        (MatchKind.Any, _) or (_, MatchKind.Any) => true,
        _ => Low <= other.High && other.Low <= High,
    };

    /// <summary><c>31</c>, <c>32..126</c>, <c>any</c>, <c>event Error</c>, <c>after 60s</c>, <c>after 250ms</c> or <c>after Delay</c>.</summary>
    public override string ToString() => Kind switch
    {
        MatchKind.Value => Low.ToString(CultureInfo.InvariantCulture),
        MatchKind.Range => string.Format(CultureInfo.InvariantCulture, "{0}..{1}", Low, High),
        MatchKind.Any => "any",
        MatchKind.Timer => "after " + (DelayedBy is null ? DelayText(Low) : "Delay"),
        _ => "event " + ShortName(EventType ?? string.Empty),
    };

    /// <summary><c>60s</c> for whole seconds, otherwise <c>250ms</c>: what <c>TriggerDefinition.ToString</c> writes too.</summary>
    public static string DelayText(long milliseconds) =>
        milliseconds % 1000 == 0
            ? (milliseconds / 1000).ToString(CultureInfo.InvariantCulture) + "s"
            : milliseconds.ToString(CultureInfo.InvariantCulture) + "ms";

    private static string ShortName(string typeName)
    {
        var at = typeName.LastIndexOfAny(['.', '+']);
        return at < 0 ? typeName : typeName.Substring(at + 1);
    }
}

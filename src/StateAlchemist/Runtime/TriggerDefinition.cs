using System;

namespace StateAlchemist;

/// <summary>What a transition fires on.</summary>
public readonly struct TriggerDefinition : IEquatable<TriggerDefinition>
{
    private TriggerDefinition(TriggerKind kind, long low, long high, Type? eventType)
    {
        Kind = kind;
        Low = low;
        High = high;
        EventType = eventType;
    }

    /// <summary>How the trigger matches.</summary>
    public TriggerKind Kind { get; }

    /// <summary>The value, or the first value of a range. Zero otherwise.</summary>
    public long Low { get; }

    /// <summary>The value, or the last value of a range. Zero otherwise.</summary>
    public long High { get; }

    /// <summary>The event type of an event trigger; otherwise <see langword="null"/>.</summary>
    public Type? EventType { get; }

    /// <summary>A trigger on one value.</summary>
    /// <param name="value">The value.</param>
    public static TriggerDefinition ForValue(long value) => new(TriggerKind.Value, value, value, null);

    /// <summary>A trigger on an inclusive range.</summary>
    /// <param name="low">The first value.</param>
    /// <param name="high">The last value; not less than <paramref name="low"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="high"/> is less than <paramref name="low"/>.</exception>
    public static TriggerDefinition ForRange(long low, long high) =>
        high < low
            ? throw new ArgumentException($"The range {low}..{high} is empty.", nameof(high))
            : new(TriggerKind.Range, low, high, null);

    /// <summary>A trigger on any value the state does not handle more specifically.</summary>
    public static TriggerDefinition ForAny() => new(TriggerKind.Any, 0, 0, null);

    /// <summary>A trigger on an event.</summary>
    /// <param name="eventType">A struct implementing <see cref="IEvent"/>.</param>
    public static TriggerDefinition ForEvent(Type eventType) =>
        new(TriggerKind.Event, 0, 0, eventType ?? throw new ArgumentNullException(nameof(eventType)));

    /// <summary>Whether this trigger matches <paramref name="value"/>. Event triggers match no value.</summary>
    /// <param name="value">The value.</param>
    public bool Matches(long value) => Kind switch
    {
        TriggerKind.Value => value == Low,
        TriggerKind.Range => value >= Low && value <= High,
        TriggerKind.Any => true,
        _ => false,
    };

    /// <inheritdoc/>
    public bool Equals(TriggerDefinition other) =>
        Kind == other.Kind && Low == other.Low && High == other.High && EventType == other.EventType;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TriggerDefinition other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => ((int)Kind * 397) ^ Low.GetHashCode() ^ (High.GetHashCode() * 31) ^ (EventType?.GetHashCode() ?? 0);

    /// <summary>The trigger as the docs and diagrams show it: <c>31</c>, <c>32..126</c>, <c>any</c>, <c>event Error</c>.</summary>
    public override string ToString() => Kind switch
    {
        TriggerKind.Value => Low.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TriggerKind.Range => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}..{1}", Low, High),
        TriggerKind.Any => "any",
        _ => "event " + EventType!.Name,
    };

    /// <summary>Equality.</summary>
    public static bool operator ==(TriggerDefinition left, TriggerDefinition right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(TriggerDefinition left, TriggerDefinition right) => !left.Equals(right);
}

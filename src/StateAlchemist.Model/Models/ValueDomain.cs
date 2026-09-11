using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>The values a machine's value type can take.</summary>
/// <param name="Low">The smallest.</param>
/// <param name="High">The largest.</param>
/// <param name="Members">For an enum, its declared values; otherwise every value from <paramref name="Low"/> to <paramref name="High"/>.</param>
public sealed record ValueDomain(long Low, long High, IReadOnlyList<long>? Members = null)
{
    /// <summary><see cref="byte"/>: 0 to 255.</summary>
    public static ValueDomain Byte { get; } = new(0, 255);

    /// <summary>How many values there are.</summary>
    public long Count => Members?.Count ?? High - Low + 1;

    /// <summary>Whether <paramref name="value"/> fits the value type.</summary>
    public bool Contains(long value) => value >= Low && value <= High;

    /// <summary>Every value, in order.</summary>
    public IEnumerable<long> Values
    {
        get
        {
            if (Members is not null)
            {
                foreach (var member in Members)
                {
                    yield return member;
                }

                yield break;
            }

            for (var value = Low; value <= High; value++)
            {
                yield return value;
            }
        }
    }
}

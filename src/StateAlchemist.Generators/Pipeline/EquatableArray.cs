using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Generators;

/// <summary>
/// An array compared by its elements, so a pipeline step's result can be compared with the last run's and the
/// generator skip work when nothing changed.
/// </summary>
internal readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items = items;

    public int Count => _items?.Length ?? 0;

    public bool Equals(EquatableArray<T> other) => (_items ?? []).SequenceEqual(other._items ?? []);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in _items ?? [])
        {
            hash = (hash * 31) + item.GetHashCode();
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

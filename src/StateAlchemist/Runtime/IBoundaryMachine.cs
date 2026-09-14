using System;
using System.Threading.Tasks;

namespace StateAlchemist;

/// <summary>A machine whose actions can cooperatively return an input batch to its caller.</summary>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public interface IBoundaryMachine<TValue> : IMachine<TValue>
    where TValue : struct
{
    /// <summary>Fires values until all are consumed or an action requests a cooperative batch boundary.</summary>
    /// <param name="values">The values. The machine holds them until the returned task completes; nothing is copied.</param>
    /// <returns>The number of values consumed.</returns>
    ValueTask<int> FireUntilBoundaryAsync(ReadOnlyMemory<TValue> values);

    /// <summary>Returns the current batch after the current transition and its queued events.</summary>
    /// <exception cref="InvalidOperationException">Called from code not running inside the machine.</exception>
    void RequestBatchBoundary();
}

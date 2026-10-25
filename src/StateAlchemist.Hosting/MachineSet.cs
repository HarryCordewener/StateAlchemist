using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Hosting;

/// <summary>
/// Machines by key, such as one per connection, order or player: each is created and started the first time its key
/// is asked for, and stopped when it is removed or the set is stopped.
/// </summary>
/// <remarks>
/// <para>
/// Callers asking for the same key at once get the same machine, created and started once. If its start throws,
/// the key is left empty, so the next call tries again.
/// </para>
/// <para>
/// A machine that implements <see cref="IStatechart{TValue}"/> and completes (<see cref="MachineStatus.Completed"/>)
/// is removed from the set and stopped, which runs its <c>[Exited]</c> actions.
/// </para>
/// </remarks>
/// <typeparam name="TKey">What identifies a machine.</typeparam>
/// <typeparam name="TMachine">The machine.</typeparam>
/// <typeparam name="TValue">The machine's value type.</typeparam>
public sealed class MachineSet<TKey, TMachine, TValue> : IAsyncDisposable
    where TKey : notnull
    where TMachine : class, IMachine<TValue>
    where TValue : struct
{
    private readonly Func<TKey, TMachine> _factory;
    private readonly ConcurrentDictionary<TKey, Entry> _machines;
    private int _stopped;

    /// <summary>An empty set.</summary>
    /// <param name="factory">Creates the machine for a key, without starting it. The set starts it.</param>
    /// <param name="comparer">Compares keys; the default comparer when <see langword="null"/>.</param>
    public MachineSet(Func<TKey, TMachine> factory, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _machines = new ConcurrentDictionary<TKey, Entry>(comparer);
    }

    /// <summary>How many keys have a machine, counting one still starting.</summary>
    public int Count => _machines.Count;

    /// <summary>The keys that have a machine, counting one still starting.</summary>
    public IReadOnlyCollection<TKey> Keys => _machines.Keys.ToArray();

    /// <summary>The started machine for <paramref name="key"/>, if there is one.</summary>
    /// <param name="key">The key.</param>
    /// <param name="machine">The machine, when this returns <see langword="true"/>.</param>
    /// <returns>Whether the key has a machine that has finished starting.</returns>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TMachine machine)
    {
        if (_machines.TryGetValue(key, out var entry) && entry.Started.IsCompletedSuccessfully)
        {
            machine = entry.Started.Result;
            return true;
        }

        machine = null;
        return false;
    }

    /// <summary>The machine for <paramref name="key"/>, created and started if the key has none.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The started machine.</returns>
    /// <exception cref="ObjectDisposedException">The set has been stopped.</exception>
    public async ValueTask<TMachine> GetOrStartAsync(TKey key)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopped) != 0, this);
        var entry = _machines.GetOrAdd(key, static (k, set) => new Entry(set, k), this);
        try
        {
            return await entry.Started.ConfigureAwait(false);
        }
        catch
        {
            _machines.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
            throw;
        }
    }

    /// <summary>Fires <paramref name="value"/> at the machine for <paramref name="key"/>, starting it first if the key has none.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>Completes when the machine has processed the value.</returns>
    public async ValueTask FireAsync(TKey key, TValue value) =>
        await (await GetOrStartAsync(key).ConfigureAwait(false)).FireAsync(value).ConfigureAwait(false);

    /// <summary>Fires <paramref name="e"/> at the machine for <paramref name="key"/>, starting it first if the key has none.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="e">The event.</param>
    /// <returns>Completes when the machine has processed the event.</returns>
    public async ValueTask FireAsync<TEvent>(TKey key, TEvent e)
        where TEvent : struct, IEvent =>
        await (await GetOrStartAsync(key).ConfigureAwait(false)).FireAsync(e).ConfigureAwait(false);

    /// <summary>Removes the machine for <paramref name="key"/> and stops it.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether the key had a machine.</returns>
    public async ValueTask<bool> RemoveAsync(TKey key)
    {
        if (!_machines.TryRemove(key, out var entry))
        {
            return false;
        }

        await StopAsync(entry).ConfigureAwait(false);
        return true;
    }

    /// <summary>Stops every machine and refuses new ones. A host calls it on shutdown.</summary>
    /// <returns>Completes when every machine has stopped.</returns>
    public async ValueTask StopAllAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        var stopping = new List<Task>();
        foreach (var key in _machines.Keys)
        {
            if (_machines.TryRemove(key, out var entry))
            {
                stopping.Add(StopAsync(entry).AsTask());
            }
        }

        await Task.WhenAll(stopping).ConfigureAwait(false);
    }

    /// <inheritdoc cref="StopAllAsync"/>
    public ValueTask DisposeAsync() => StopAllAsync();

    private static async ValueTask StopAsync(Entry entry)
    {
        TMachine machine;
        try
        {
            machine = await entry.Started.ConfigureAwait(false);
        }
        catch
        {
            return; // its start failed: GetOrStartAsync reported that
        }

        await machine.StopAsync().ConfigureAwait(false);
    }

    /// <summary>A machine completed: it leaves the set, if it is still the key's machine, and is stopped.</summary>
    private async Task Completed(TKey key, Entry entry, TMachine machine)
    {
        if (machine.Status == MachineStatus.Completed && _machines.TryRemove(new KeyValuePair<TKey, Entry>(key, entry)))
        {
            await machine.StopAsync().ConfigureAwait(false);
        }
    }

    private sealed class Entry
    {
        private readonly Lazy<Task<TMachine>> _started;

        public Entry(MachineSet<TKey, TMachine, TValue> set, TKey key) =>
            _started = new Lazy<Task<TMachine>>(() => StartAsync(set, key), LazyThreadSafetyMode.ExecutionAndPublication);

        public Task<TMachine> Started => _started.Value;

        private async Task<TMachine> StartAsync(MachineSet<TKey, TMachine, TValue> set, TKey key)
        {
            var machine = set._factory(key) ?? throw new InvalidOperationException($"The factory returned null for '{key}'.");
            await machine.StartAsync().ConfigureAwait(false);
            if (machine is IStatechart<TValue> statechart)
            {
                _ = statechart.Completion.ContinueWith(
                    static (_, state) =>
                    {
                        var (set, key, entry, machine) = ((MachineSet<TKey, TMachine, TValue>, TKey, Entry, TMachine))state!;
                        return set.Completed(key, entry, machine);
                    },
                    (set, key, this, machine),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Unwrap();
            }

            return machine;
        }
    }
}

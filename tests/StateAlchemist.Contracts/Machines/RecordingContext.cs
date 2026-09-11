using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Contracts.Machines;

/// <summary>
/// The context of every contract machine except the telnet sample. Actions and guards record into <see cref="Log"/>;
/// a test makes any recorded step throw by adding its entry to <see cref="Failing"/>.
/// </summary>
public sealed class RecordingContext
{
    /// <summary>What ran, in order.</summary>
    public List<string> Log { get; } = [];

    /// <summary>Entries that throw instead of being recorded.</summary>
    public HashSet<string> Failing { get; } = [];

    /// <summary>Guards named here pass.</summary>
    public HashSet<string> Allow { get; } = [];

    /// <summary>What an action awaits, when a test needs a transition held open.</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// What an async decision awaits: a test completes it with an outcome union, or faults it. Its continuations run
    /// inline, so when <c>SetResult</c> returns, the machine has already dealt with the outcome.
    /// </summary>
    public TaskCompletionSource<object> Answer { get; } = new();

    /// <summary>Completed when an async decision starts, with the token it was given.</summary>
    public TaskCompletionSource<CancellationToken> Deciding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The machine, for actions that enqueue or fire.</summary>
    public IMachine<byte>? Machine { get; set; }

    /// <summary>Records <paramref name="entry"/>, or throws if a test listed it in <see cref="Failing"/>.</summary>
    public void Record(string entry)
    {
        if (Failing.Contains(entry))
        {
            throw new InvalidOperationException($"{entry} failed");
        }

        Log.Add(entry);
    }

    /// <summary>The log, joined: the form order-of-operations assertions compare.</summary>
    public string Trace => string.Join(" | ", Log);
}

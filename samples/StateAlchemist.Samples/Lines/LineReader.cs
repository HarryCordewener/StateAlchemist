using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Samples.Lines;

// Reading a stream: the example for runs, batches and Serialized. Text arrives in whatever chunks the socket
// gives, a whole stretch of it is one call, and something else — a timer, a supervisor — fires events meanwhile.

/// <summary>What the machine reports to.</summary>
public sealed class Lines
{
    /// <summary>Each completed line's length, in order.</summary>
    public List<int> Lengths { get; } = [];

    /// <summary>How many times the reader was asked to flush.</summary>
    public int Flushes { get; set; }
}

/// <summary>Someone wants what has been read so far.</summary>
public readonly struct Flush : IEvent;

// begin-snippet: sample-lines-states
/// <summary>The stream. Its data lasts as long as the machine.</summary>
public struct Stream : IRootState
{
    /// <summary>Completed lines.</summary>
    public int Count;

    /// <summary>The length of the line that just ended, for the action that reports it.</summary>
    public int LastLength;
}

/// <summary>Reading a line. What it has read so far belongs to the line, and goes when the line ends.</summary>
[Initial]
public struct Line : IState<Stream>
{
    /// <summary>Bytes in the line so far.</summary>
    public int Length;
}
// end-snippet

// begin-snippet: sample-lines-module
[Module]
public static class LineModule
{
    /// <summary>
    /// A run: every byte that is not a newline is handled the same way, so the machine takes the whole stretch
    /// in one call. The stop set — here, just the newline — is worked out at compile time from the other
    /// transitions in this state.
    /// </summary>
    [Transition(From = typeof(Line)), OnAny, Run]
    public static void Text(ref Line line, ReadOnlySpan<byte> run) => line.Length += run.Length;

    /// <summary>
    /// The newline ends a line. This is a stay, not a re-entry: a stay keeps the state's data, so the transform
    /// resets the length itself and the machine never leaves <see cref="Line"/>. A re-entry would clear the data
    /// for you, and warn that it had ([`SALCH0301`](../../docs/reference/diagnostics.md#salch0301)).
    /// </summary>
    [Transition(From = typeof(Line)), On((byte)'\n')]
    public static class EndOfLine
    {
        public static void Transform(ref Line line, ref Stream stream)
        {
            stream.Count++;
            stream.LastLength = line.Length;
            line.Length = 0;
        }

        public static void Completed(Lines lines, Stream stream) => lines.Lengths.Add(stream.LastLength);
    }

    /// <summary>An event from elsewhere. It waits its turn like any other input.</summary>
    [Transition(From = typeof(Stream)), OnEvent(typeof(Flush))]
    public static void Flushed(Lines lines) => lines.Flushes++;
}
// end-snippet

// begin-snippet: sample-lines-machine
/// <summary>
/// <c>Serialized</c>: the read loop and whatever fires events are different threads, and the machine runs one
/// transition at a time whichever of them arrives first.
/// </summary>
[Machine(Root = typeof(Stream), Value = typeof(byte), Context = typeof(Lines), Concurrency = Concurrency.Serialized)]
[Include(typeof(LineModule))]
public sealed partial class LineReader;
// end-snippet

/// <summary>The read loop itself, which is the ordinary one.</summary>
public static class Reading
{
    // begin-snippet: sample-lines-loop
    /// <summary>
    /// Reads until the pipe completes. Awaiting <c>FireAsync</c> is the backpressure: while the machine is busy —
    /// including while a decision of its own is pending — this loop is not reading, so the pipe fills, and once it
    /// passes its pause threshold the writer waits.
    /// </summary>
    public static async Task ReadAsync(PipeReader reader, LineReader machine, CancellationToken cancellation = default)
    {
        while (true)
        {
            var read = await reader.ReadAsync(cancellation).ConfigureAwait(false);
            foreach (var segment in read.Buffer)
            {
                await machine.FireAsync(segment).ConfigureAwait(false);
            }

            reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted)
            {
                break;
            }
        }

        await reader.CompleteAsync().ConfigureAwait(false);
    }
    // end-snippet
}

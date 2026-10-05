using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Samples.Illustrations;

/// <summary>Turns bytes a mode change has made undecodable as they stand into bytes the machine can take.</summary>
public interface IDecoder
{
    /// <summary>Decodes what is left of a batch.</summary>
    ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> remaining);
}

/// <summary>The loops the concept pages quote, compiled against the interfaces every machine implements.</summary>
public static class Loops
{
    /// <summary>The ordinary pipe loop: awaiting each batch is the backpressure.</summary>
    public static async Task ReadAsync(PipeReader reader, IMachine<byte> machine, CancellationToken ct)
    {
        // begin-snippet: sample-backpressure-loop
        while (true)
        {
            var read = await reader.ReadAsync(ct);
            foreach (var segment in read.Buffer)
            {
                await machine.FireAsync(segment);        // waits through any decision
            }

            reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted) break;
        }
        // end-snippet
    }

    /// <summary>Feeds a batch through a machine whose actions can change how the rest of it must be decoded.</summary>
    public static async Task FeedAsync(IBoundaryMachine<byte> machine, ReadOnlyMemory<byte> input, IDecoder decoder)
    {
        // begin-snippet: sample-boundary-loop
        ReadOnlyMemory<byte> remaining = input;

        while (!remaining.IsEmpty)
        {
            var consumed = await machine.FireUntilBoundaryAsync(remaining);
            remaining = remaining[consumed..];

            if (!remaining.IsEmpty)
            {
                // An action requested the boundary after changing the decoder's mode.
                remaining = decoder.Decode(remaining);
            }
        }
        // end-snippet
    }

    /// <summary>Writes the telnet machine's diagrams.</summary>
    public static void WriteDiagrams()
    {
        // begin-snippet: sample-diagrams
        Console.WriteLine(MudTelnet.Mermaid);   // a Mermaid stateDiagram-v2
        File.WriteAllText("telnet.dot", MudTelnet.Dot);   // a Graphviz digraph
        // end-snippet
    }
}

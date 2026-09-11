using System.Collections.Generic;
using System.Threading.Tasks;

namespace StateAlchemist.Samples.Telnet;

/// <summary>What the sample machine's actions talk to. It records everything, so tests and docs can show what happened.</summary>
public sealed class TelnetContext
{
    /// <summary>Lines the actions logged, in order.</summary>
    public List<string> Log { get; } = [];

    /// <summary>Byte sequences the actions sent, in order.</summary>
    public List<byte[]> Sent { get; } = [];

    /// <summary>Records a send; completes synchronously.</summary>
    public ValueTask SendAsync(params byte[] bytes)
    {
        Sent.Add(bytes);
        return default;
    }
}

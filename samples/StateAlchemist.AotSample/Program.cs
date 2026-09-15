using System;
using System.Threading.Tasks;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.AotSample;

// A machine in a native binary: no reflection, no dictionaries, nothing the trimmer has to guess at. Publishing this
// with PublishAot must produce no warnings — that is what CI checks — and running it must negotiate the same way the
// sample does on the runtime.
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class AotTelnet;

public static class Program
{
    public static async Task<int> Main()
    {
        var context = new TelnetContext();
        await using var machine = new AotTelnet(context);
        await machine.StartAsync();

        // IAC SB NAWS 0 80 0 24 IAC SE: a window of 80×24, read byte by byte.
        await machine.FireAsync(new byte[] { 255, 250, 31, 0, 80, 0, 24, 255, 240 });
        await machine.StopAsync();

        machine.TryGetState<Connected>(out var root);
        Console.WriteLine($"window {root.Width}x{root.Height}");
        foreach (var line in context.Log)
        {
            Console.WriteLine(line);
        }

        return root.Width == 80 && root.Height == 24 ? 0 : 1;
    }
}

using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/guides/getting-started.md, end to end, on the documentation's own machine.</summary>
public abstract class TelnetContract : MachineContract
{
    private static string Sent(TelnetContext context) => string.Join(" | ", context.Sent.Select(s => string.Join(",", s)));

    [Test]
    public async Task GmcpIsAcceptedAndOtherOptionsRefused()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 251, 201, 255, 251, 42 });
        await Assert.That(Sent(context)).IsEqualTo("255,253,201 | 255,254,42");
        await Assert.That(telnet.TryGetState(out Connected root) && root.GmcpEnabled).IsTrue();
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
    }

    [Test]
    public async Task NawsReadsTheWindowSize()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 250, 31, 0, 80, 0, 24, 255, 240 });
        await Assert.That(telnet.TryGetState(out Connected root) ? (root.Width, root.Height) : (0, 0)).IsEqualTo((80, 24));
        await Assert.That(string.Join(" | ", context.Log)).IsEqualTo("ready | ready | window 80x24");
    }

    [Test]
    public async Task TextCountsTheLineAndLineFeedEndsIt()
    {
        var telnet = await StartAsync(Shapes.Telnet, new TelnetContext());
        await telnet.FireAsync("hi"u8.ToArray());
        await Assert.That(telnet.TryGetState(out Idle idle) ? idle.LineLength : -1).IsEqualTo(2);
        await telnet.FireAsync((byte)'\n');
        await Assert.That(telnet.TryGetState(out idle) ? idle.LineLength : -1).IsEqualTo(0);
    }

    [Test]
    public async Task AnErrorRecoversFromAnywhere()
    {
        var telnet = await StartAsync(Shapes.Telnet, new TelnetContext());
        await telnet.FireAsync(new byte[] { 255, 250, 31, 1 });
        await Assert.That(telnet.IsIn<Naws>()).IsTrue();
        await telnet.FireAsync(new Error());
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
    }

    [Test]
    public async Task AnUnknownSubnegotiationIsAbandoned()
    {
        var telnet = await StartAsync(Shapes.Telnet, new TelnetContext());
        await telnet.FireAsync(new byte[] { 255, 250, 99 });
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
    }
}

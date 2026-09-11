using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/triggers.md: which transition wins.</summary>
public abstract class ResolutionContract : MachineContract
{
    private async Task<string?> ChosenBy(byte value, params string[] allow)
    {
        var context = new RecordingContext();
        context.Allow.UnionWith(allow);
        var machine = await StartAsync(Shapes.Guards, context);
        await machine.FireAsync(value);
        return machine.TryGetState(out Chosen chosen) ? chosen.By : "(not chosen)";
    }

    [Test]
    public async Task WhenNoGuardPassesTheUnguardedTransitionRuns() => await Assert.That(await ChosenBy(1)).IsEqualTo("Fallback");

    [Test]
    public async Task GuardedTransitionsAreTriedInOrder()
    {
        await Assert.That(await ChosenBy(1, "Second")).IsEqualTo("Second");
        await Assert.That(await ChosenBy(1, "First", "Second")).IsEqualTo("First");
    }

    [Test]
    public async Task ExactBeatsRangeBeatsAny()
    {
        await Assert.That(await ChosenBy(15)).IsEqualTo("Range");
        await Assert.That(await ChosenBy(200)).IsEqualTo("Any");
    }

    [Test]
    public async Task AStatesOrElseShadowsItsParent()
    {
        await Assert.That(await ChosenBy(42)).IsEqualTo("Any");
    }

    [Test]
    public async Task WithNothingAtALevelResolutionReachesTheParent()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Guards, context);
        await machine.FireAsync(new byte[] { 1, 42 });
        await Assert.That(machine.TryGetState(out GuardRoot root) ? root.Hits : -1).IsEqualTo(1);
        await Assert.That(machine.IsIn<Chosen>()).IsTrue();
    }

    [Test]
    public async Task AFailingGuardFallsThroughToTheParentsOrElse()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 250, 31, 0, 80, 255, 240 });
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
        await Assert.That(telnet.TryGetState(out Connected root) ? root.Width : -1).IsEqualTo(0);
    }

    [Test]
    public async Task OrElseNeverSwallowsAnEvent()
    {
        var context = new TelnetContext();
        var telnet = await StartAsync(Shapes.Telnet, context);
        await telnet.FireAsync(new byte[] { 255, 251 });
        await telnet.FireAsync(new Error());
        await Assert.That(telnet.IsIn<Idle>()).IsTrue();
        await Assert.That(context.Sent.Count).IsEqualTo(0);
    }
}

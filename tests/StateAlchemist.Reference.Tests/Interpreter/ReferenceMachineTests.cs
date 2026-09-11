using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Reference.Tests.FrontEnd;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Reference.Tests.Interpreter;

/// <summary>What only the interpreter does: refuse what a generator would refuse to compile.</summary>
public class ReferenceMachineTests
{
    private static ReflectedMachine Telnet(params Type[] extra) =>
        ReflectionModelBuilder.Build(new MachineSpec(
            "Telnet", typeof(Connected), typeof(byte), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule), .. extra], typeof(TelnetContext)));

    [Test]
    public async Task AMachineWithErrorsIsRefusedWithThem()
    {
        var refused = Assert.Throws<InvalidMachineException>(() => ReferenceMachine<byte>.Create(Telnet(typeof(RivalGmcpModule)), new TelnetContext()));
        await Assert.That(string.Join(",", refused!.Errors.Select(e => e.Id))).IsEqualTo("SALCH0101");
        await Assert.That(refused.Message).StartsWith("Machine 'Telnet' has errors:\n  SALCH0101: ");
    }

    [Test]
    public async Task TheValueTypeMustBeTheMachines()
    {
        await Assert.That(() => ReferenceMachine<ushort>.Create(Telnet(), new TelnetContext())).Throws<ArgumentException>();
    }

    [Test]
    public async Task ARunTransformMayTakeTheConfigurationAndTheContext()
    {
        var spec = new MachineSpec("Configured", typeof(Stream), typeof(byte), [typeof(ConfiguredRuns)], typeof(RunLog), typeof(RunConfig));
        var log = new RunLog();
        var machine = ReferenceMachine<byte>.Create(ReflectionModelBuilder.Build(spec), log, new RunConfig(2));
        await machine.StartAsync();
        await machine.FireAsync(new byte[] { 1, 2, 3, 200, 201 });
        await Assert.That(machine.TryGetState(out Stream stream) ? stream.Total : -1).IsEqualTo(3 * 2 + 2 * 2 * 100);
        await Assert.That(string.Join(",", log.Entries)).IsEqualTo("high 2");
    }

    [Test]
    public async Task AValidMachineStartsInItsInitialLeaf()
    {
        var machine = ReferenceMachine<byte>.Create(Telnet(), new TelnetContext());
        await Assert.That(machine.StateType).IsEqualTo(typeof(Idle));
    }
}

using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/concurrency.md: a Checked machine refuses a second caller and never corrupts state.</summary>
public abstract class ConcurrencyContract : MachineContract
{
    [Test]
    public async Task ACheckedMachineRefusesASecondCallerWhileATransitionRuns()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        var held = machine.FireAsync((byte)11).AsTask();

        await Assert.That(async () => await machine.FireAsync((byte)3)).Throws<ConcurrentUseException>();

        context.Gate.SetResult();
        await held;
        await Assert.That(machine.TryGetState(out Machines.Recording.A1 a1) ? a1.Value : -1).IsEqualTo(0);
    }

    [Test]
    public async Task ACheckedMachineCatchesAnActionFiringItsOwnMachine()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(async () => await machine.FireAsync((byte)10)).Throws<ConcurrentUseException>();
    }
}

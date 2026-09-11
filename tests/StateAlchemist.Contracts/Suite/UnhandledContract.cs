using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Guards;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/triggers.md: unhandled triggers.</summary>
public abstract class UnhandledContract : MachineContract
{
    [Test]
    public async Task AnUnhandledValueCallsTheHookAndIsIgnored()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Guards, context, new ContractHooks(context.Log));
        await machine.FireAsync(new byte[] { 1, 99 });
        await Assert.That(context.Trace).IsEqualTo("transitioned GuardModule.Fallback | unhandled 99 in Chosen");
        await Assert.That(machine.IsIn<Chosen>()).IsTrue();
    }

    [Test]
    public async Task AMachineDeclaredUnhandledThrowThrows()
    {
        var machine = await StartAsync(Shapes.GuardsThatThrow, new RecordingContext());
        await machine.FireAsync((byte)1);
        var refused = await Assert.ThrowsAsync<UnhandledTriggerException>(async () => await machine.FireAsync((byte)99));
        await Assert.That(refused!.Message).IsEqualTo("No transition handles 99 in state 'Chosen'.");
    }
}

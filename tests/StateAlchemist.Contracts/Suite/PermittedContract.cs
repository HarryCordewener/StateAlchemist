using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Permits;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>Which triggers would fire now, and starting a machine part-way through.</summary>
public abstract class PermittedContract : MachineContract
{
    private static string Text(IStatechart<byte> machine) => string.Join(", ", machine.PermittedTriggers().Select(t => t.ToString()));

    [Test]
    public async Task PermittedTriggersEvaluateGuardsAndJoinNeighbours()
    {
        var context = new RecordingContext();
        var machine = (IStatechart<byte>)await StartAsync(Shapes.Permits, context);

        await Assert.That(Text(machine)).IsEqualTo("1, 20, 22, 24, 100..101, event Knock, event Wave");

        context.Allow.Add("Ranged");
        context.Allow.Add("Poked");
        await Assert.That(Text(machine)).IsEqualTo("1, 5..9, 20, 22, 24, 100..101, event Knock, event Poke, event Wave");
    }

    [Test]
    public async Task PermittedTriggersFollowTheActiveLeaf()
    {
        var machine = (IStatechart<byte>)await StartAsync(Shapes.Permits, new RecordingContext());
        await machine.FireAsync(1);
        await Assert.That(Text(machine)).IsEqualTo("2, 100..101, event Wave");
    }

    [Test]
    public async Task PermittedTriggersRunNothing()
    {
        var context = new RecordingContext();
        var machine = (IStatechart<byte>)await StartAsync(Shapes.Permits, context);
        context.Log.Clear();
        _ = machine.PermittedTriggers();
        await Assert.That(context.Trace).IsEqualTo("");
        await Assert.That(machine.IsIn<Open>()).IsTrue();
    }

    [Test]
    public async Task StartingInAStateEntersItsInitialPathAndRunsItsEnteredActions()
    {
        var context = new RecordingContext();
        var machine = (IStatechart<byte>)Create(Shapes.Permits, context, null);
        await machine.StartAsync<Shut>();

        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Running);
        await Assert.That(machine.StateType).IsEqualTo(typeof(Latched));
        await Assert.That(context.Trace).IsEqualTo("entered PermitRoot | entered Shut | entered Latched");

        await machine.FireAsync(2);
        await Assert.That(machine.IsIn<Open>()).IsTrue();
    }

    [Test]
    public async Task StartingInAStateThatIsNotInTheMachineThrows()
    {
        var machine = (IStatechart<byte>)Create(Shapes.Permits, new RecordingContext(), null);
        await Assert.That(async () => await machine.StartAsync<Knock>()).Throws<ArgumentException>();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.NotStarted);
    }

    [Test]
    public async Task StartingInAStateTwiceThrows()
    {
        var machine = (IStatechart<byte>)await StartAsync(Shapes.Permits, new RecordingContext());
        await Assert.That(async () => await machine.StartAsync<Shut>()).Throws<InvalidOperationException>();
    }
}

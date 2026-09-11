using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/triggers.md (events) and docs/concepts/actions.md (run to completion).</summary>
public abstract class EventContract : MachineContract
{
    [Test]
    public async Task AnEventDeclaredOnTheRootReachesEveryState()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await machine.FireAsync(new Ping { Amount = 5 });
        await machine.FireAsync((byte)1);
        await machine.FireAsync(new Ping { Amount = 2 });
        await Assert.That(machine.TryGetState(out Root root) ? root.Counter : 0).IsEqualTo(7);
    }

    [Test]
    public async Task AnEventEnqueuedByAnActionRunsAfterItsTransitionCompletes()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await machine.FireAsync((byte)1);
        context.Log.Clear();
        await machine.FireAsync((byte)9);
        await Assert.That(context.Trace).IsEqualTo("completed Echo | pinged 100");
        await Assert.That(machine.TryGetState(out Root root) ? root.Counter : 0).IsEqualTo(100);
    }

    [Test]
    public async Task EnqueueOutsideATransitionIsRefused()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(() => machine.Enqueue(new Ping())).Throws<InvalidOperationException>();
    }
}

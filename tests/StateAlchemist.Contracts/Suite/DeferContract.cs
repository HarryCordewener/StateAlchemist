using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Defers;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>Events a state defers: kept until the machine moves, then resolved afresh.</summary>
public abstract class DeferContract : MachineContract
{
    [Test]
    public async Task ADeferredEventWaitsForTheMachineToMove()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Defers, context);
        await machine.FireAsync(new Nudge { N = 1 });
        await machine.FireAsync(new Nudge { N = 2 });
        await machine.FireAsync(3);
        await Assert.That(context.Trace).IsEqualTo(string.Empty);

        await machine.FireAsync(1);
        await Assert.That(context.Trace).IsEqualTo("nudge 1 | nudge 2");
        await Assert.That(machine.IsIn<Free>()).IsTrue();
    }

    [Test]
    public async Task AStateBelowTheDeferringOneIsTriedFirst()
    {
        var context = new RecordingContext();
        context.Allow.Add("Inner");
        var machine = await StartAsync(Shapes.Defers, context);
        await machine.FireAsync(new Nudge { N = 5 });
        await machine.FireAsync(1);
        await Assert.That(context.Trace).IsEqualTo("inner nudge 5");
    }

    [Test]
    public async Task ADeferredEventIsDeferredAgainIfTheNewStateDefersIt()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Defers, context);
        await machine.FireAsync(1);
        await machine.FireAsync(2);
        await machine.FireAsync(new Nudge { N = 7 });
        await Assert.That(context.Trace).IsEqualTo(string.Empty);
        await machine.FireAsync(1);
        await Assert.That(context.Trace).IsEqualTo("nudge 7");
    }

    [Test]
    public async Task AnInternalEventIsRaisedOnlyFromInside()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Defers, context);
        await machine.FireAsync(1);
#pragma warning disable SALCH0602 // the contract checks what the call does at run time
        await Assert.That(async () => await machine.FireAsync(new Tick())).Throws<System.InvalidOperationException>();
#pragma warning restore SALCH0602
        await machine.FireAsync(5);
        await Assert.That(context.Trace).IsEqualTo("tick");
    }

    [Test]
    public async Task ThePlanOfADeferredEventSaysSo()
    {
        var machine = await StartAsync(Shapes.Defers, new RecordingContext());
        var plan = machine.Plan(new Nudge { N = 1 });
        await Assert.That(plan.IsDeferred).IsTrue();
        await Assert.That(plan.Handled).IsFalse();
        await Assert.That(plan.Leaf).IsEqualTo(typeof(Inner));
    }
}

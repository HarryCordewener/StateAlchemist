using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Invokes;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>Invoked machines: a state's children start with it and stop with it, complete it, and take what it forwards.</summary>
public abstract class InvokeContract : MachineContract
{
    private RecordingContext Context()
    {
        var context = new RecordingContext();
        context.CreateChild = () => (IStatechart<byte>)Create(Shapes.Worker, context, null);
        return context;
    }

    private async Task<IStatechart<byte>> Started(RecordingContext context) => (IStatechart<byte>)await StartAsync(Shapes.Invokes, context);

    /// <summary>A child's completion reaches its parent through the parent's inbox, after the call that caused it.</summary>
    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold within 10 seconds.");
            }

            await Task.Delay(5);
        }
    }

    [Test]
    public async Task EnteringAStateStartsItsChildAndLeavingItStopsIt()
    {
        var context = Context();
        var machine = await Started(context);
        await machine.FireAsync(1);

        await Assert.That(context.Children.Count).IsEqualTo(1);
        var child = context.Children[0];
        await Assert.That(child.Status).IsEqualTo(MachineStatus.Running);

        await machine.FireAsync(9);
        await Assert.That(child.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("entered running | worker started | reset | worker stopped | exited running");
    }

    [Test]
    public async Task AForwardedValueCompletesTheChildAndItsStateIsDone()
    {
        var context = Context();
        var machine = await Started(context);
        await machine.FireAsync(1);
        await machine.FireAsync(1);

        await Eventually(() => context.Trace.EndsWith("running done"));
        await Assert.That(machine.IsIn<InvJoined>()).IsTrue();
        await Assert.That(context.Children[0].Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("entered running | worker started | worker done | worker stopped | exited running | running done");
    }

    [Test]
    public async Task AnEventTheParentLeavesUnhandledIsForwarded()
    {
        var context = Context();
        var machine = await Started(context);
        await machine.FireAsync(new Shout());
        await machine.FireAsync(1);
        await machine.FireAsync(new Shout());
        await machine.FireAsync(new Whisper());

        await Assert.That(context.Trace).IsEqualTo("parent heard | entered running | worker started | worker heard | worker whispered");
    }

    [Test]
    public async Task AStateWithSeveralChildrenIsDoneWhenAllHaveCompleted()
    {
        var context = Context();
        var machine = await Started(context);
        await machine.FireAsync(2);
        await Assert.That(context.Children.Count).IsEqualTo(2);

        // Not forwarded: the children do not see it.
        await machine.FireAsync(1);
        await Assert.That(context.Children[0].Status).IsEqualTo(MachineStatus.Running);

        await context.Children[0].FireAsync(1);
        await Assert.That(context.Children[0].Status).IsEqualTo(MachineStatus.Completed);
        await Task.Delay(50);
        await Assert.That(machine.IsIn<InvParallel>()).IsTrue();

        await context.Children[1].FireAsync(1);
        await Eventually(() => context.Trace.EndsWith("parallel done"));
        await Assert.That(machine.IsIn<InvJoined>()).IsTrue();
    }

    [Test]
    public async Task StoppingTheParentStopsItsChildrenFirst()
    {
        var context = Context();
        var machine = await Started(context);
        await machine.FireAsync(1);
        await machine.StopAsync();

        await Assert.That(context.Children[0].Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("entered running | worker started | worker stopped | exited running");
    }

    [Test]
    public async Task PausingTheParentPausesItsChildren()
    {
        var context = Context();
        var machine = await Started(context);
        await machine.FireAsync(1);
        machine.Pause();
        await Assert.That(context.Children[0].Status).IsEqualTo(MachineStatus.Paused);

        machine.Resume();
        await Assert.That(context.Children[0].Status).IsEqualTo(MachineStatus.Running);
    }

    [Test]
    public async Task StartingInAStateStartsItsChild()
    {
        var context = Context();
        var machine = (IStatechart<byte>)Create(Shapes.Invokes, context, null);
        context.Machine = machine;
        await machine.StartAsync<InvRunning>();

        await Assert.That(context.Children.Count).IsEqualTo(1);
        await Assert.That(context.Children[0].Status).IsEqualTo(MachineStatus.Running);
        await Assert.That(context.Trace).IsEqualTo("entered running | worker started");
    }
}

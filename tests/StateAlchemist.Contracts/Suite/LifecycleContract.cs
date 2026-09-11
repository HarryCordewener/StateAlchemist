using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/lifecycle.md.</summary>
public abstract class LifecycleContract : MachineContract
{
    [Test]
    public async Task AMachineIsNotRunningUntilStarted()
    {
        var context = new RecordingContext();
        var machine = Create(Shapes.Recorder, context, null);
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.NotStarted);
        await Assert.That(machine.StateType).IsEqualTo(typeof(A1));
        var refused = await Assert.ThrowsAsync<MachineNotRunningException>(async () => await machine.FireAsync((byte)3));
        await Assert.That(refused!.Status).IsEqualTo(MachineStatus.NotStarted);
        await Assert.That(context.Trace).IsEqualTo("");
    }

    [Test]
    public async Task StartingRunsTheInitialPathsEnteredActionsRootFirst()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Running);
        await Assert.That(context.Trace).IsEqualTo("entered Root | entered A | entered A1 (0)");
    }

    [Test]
    public async Task StartingTwiceIsRefused()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(async () => await machine.StartAsync()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StoppingRunsExitedActionsFromTheLeafToTheRoot()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        context.Log.Clear();
        await machine.StopAsync();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | exited A (0) | exited Root");
    }

    [Test]
    public async Task AStoppedMachineCannotBeFiredAndStoppingAgainDoesNothing()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        await machine.StopAsync();
        context.Log.Clear();
        await machine.StopAsync();
        await Assert.That(context.Trace).IsEqualTo("");
        var refused = await Assert.ThrowsAsync<MachineNotRunningException>(async () => await machine.FireAsync((byte)3));
        await Assert.That(refused!.Status).IsEqualTo(MachineStatus.Stopped);
    }

    [Test]
    public async Task DisposingAnUnstartedMachineRunsNothing()
    {
        var context = new RecordingContext();
        var machine = Create(Shapes.Recorder, context, null);
        await machine.DisposeAsync();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("");
    }
}

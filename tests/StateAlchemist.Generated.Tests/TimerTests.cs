using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Timing;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>docs/concepts/timers.md: timers in the generated shapes the contracts do not cover.</summary>
public class TimerTests
{
    private static async Task TimesOut(IMachine<byte> machine, RecordingContext context)
    {
        await machine.StartAsync();
        await machine.FireAsync((byte)1);
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        await machine.FireAsync((byte)200);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("gave up");
    }

    [Test]
    public async Task ACheckedMachineWithoutDecisionsRunsItsTimers()
    {
        var context = new RecordingContext();
        await TimesOut(new KettleMachine(context, context.Clock), context);
    }

    [Test]
    public async Task AnUncheckedMachineRunsItsTimers()
    {
        var context = new RecordingContext();
        await TimesOut(new KettleUncheckedMachine(context, context.Clock), context);
    }

    [Test]
    public async Task AMachineWithTelemetryRunsItsTimers()
    {
        var context = new RecordingContext();
        await TimesOut(new KettleTelemetryMachine(context, context.Clock), context);
    }

    [Test]
    public async Task WithoutATimeProviderTheSystemClockIsUsed()
    {
        var context = new RecordingContext();
        var machine = new KettleMachine(context);
        await machine.StartAsync();
        await machine.FireAsync((byte)3);
        await context.Gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await machine.FireAsync((byte)200);
        await Assert.That(machine.IsIn<Left>()).IsTrue();
    }

    [Test]
    public async Task ATimerFiringWhileACallerIsInsideACheckedMachineWaitsItsTurn()
    {
        var context = new RecordingContext();
        var machine = new SlowKettleMachine(context, context.Clock);
        await machine.StartAsync();
        var waiting = machine.FireAsync((byte)2).AsTask();
        await Assert.That(machine.IsIn<Waiting>()).IsTrue();

        context.Clock.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(machine.IsIn<Waiting>()).IsTrue();

        context.Gate.SetResult();
        await waiting;
        await machine.FireAsync((byte)200);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("timed out | ignored");
    }

    [Test]
    public async Task ATimerFiringTakesNoRoomInABoundedInbox()
    {
        var context = new RecordingContext();
        var machine = new SlowKettleBoundedMachine(context, context.Clock);
        await machine.StartAsync();
        var waiting = machine.FireAsync((byte)2).AsTask();
        var queued = machine.FireAsync((byte)200).AsTask();

        // The one place in the inbox is taken: a firing that needed it would block the clock here.
        context.Clock.Advance(TimeSpan.FromSeconds(1));

        context.Gate.SetResult();
        await waiting;
        await queued;
        await machine.FireAsync((byte)201);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("ignored | timed out | ignored");
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Timing;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/timers.md: a timer is armed on entry, cancelled on exit, and fires through the inbox.</summary>
public abstract class TimerContract : MachineContract
{
    protected virtual MachineShape Shape => Shapes.Timing;

    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(ContractHooks? hooks = null, params string[] allow)
    {
        var context = new RecordingContext();
        context.Allow.UnionWith(allow);
        var machine = await StartAsync(Shape, context, hooks);
        context.Log.Clear();
        return (machine, context);
    }

    /// <summary>
    /// Moves the clock, then fires a value nothing handles and waits for it: a timer's firing joins the inbox when
    /// its callback runs, ahead of that value, so once it has been processed so has every firing.
    /// </summary>
    private static async Task Advance(IMachine<byte> machine, RecordingContext context, int seconds)
    {
        context.Clock.Advance(TimeSpan.FromSeconds(seconds));
        await machine.FireAsync((byte)200);
    }

    [Test]
    public async Task ATimerFiresWhenItsStateHasBeenActiveForItsDelay()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)1);
        await Advance(machine, context, 59);
        await Assert.That(machine.IsIn<Recording>()).IsTrue();

        await Advance(machine, context, 1);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("record | gave up 1");
    }

    [Test]
    public async Task StartingArmsTheInitialPathsTimers()
    {
        var (machine, context) = await Started();
        await Advance(machine, context, 3600);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("dozed");
    }

    [Test]
    public async Task LeavingTheStateCancelsItsTimer()
    {
        var (machine, context) = await Started();
        await machine.FireAsync(new byte[] { 1, 0 });
        await Advance(machine, context, 120);
        await Assert.That(machine.IsIn<Idle>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("record | reset");
    }

    [Test]
    public async Task AReentryRestartsTheTimer()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)1);
        await Advance(machine, context, 40);
        await machine.FireAsync((byte)3);
        await Advance(machine, context, 40);
        await Assert.That(machine.IsIn<Recording>()).IsTrue();

        await Advance(machine, context, 20);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
    }

    [Test]
    public async Task AStayLeavesTheTimerRunning()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)1);
        await Advance(machine, context, 40);
        await machine.FireAsync((byte)4);
        await Advance(machine, context, 20);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
    }

    [Test]
    public async Task AMoveBelowTheStateLeavesItsTimerRunning()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)1);
        await Advance(machine, context, 30);
        await machine.FireAsync((byte)5);
        await Assert.That(machine.IsIn<Paused>()).IsTrue();

        await Advance(machine, context, 30);
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
    }

    [Test]
    public async Task ADelayMethodReadsTheStatesNewData()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)6);
        await Advance(machine, context, 4);
        await Assert.That(machine.IsIn<Waiting>()).IsTrue();

        await Advance(machine, context, 1);
        await Assert.That(machine.IsIn<Idle>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("waited");
    }

    [Test]
    public async Task GuardsChooseAmongTimersWithTheSameDelay()
    {
        var (machine, context) = await Started(allow: "left");
        await machine.FireAsync((byte)7);
        await Advance(machine, context, 10);
        await Assert.That(machine.IsIn<Left>()).IsTrue();

        await machine.FireAsync(new byte[] { 0, 7 });
        context.Allow.Clear();
        await Advance(machine, context, 10);
        await Assert.That(machine.IsIn<Right>()).IsTrue();
    }

    [Test]
    public async Task ATimerWhoseGuardsAllRefuseDoesNothing()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)7);
        await Advance(machine, context, 5);
        await Assert.That(machine.IsIn<Choosing>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ATimerFiringWhileADecisionIsPendingIsHandledAndCancelsIt()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)9);
        var asking = machine.FireAsync((byte)2).AsTask();
        var token = await context.Deciding.Task;

        context.Clock.Advance(TimeSpan.FromSeconds(30));
        await asking;
        await Assert.That(machine.IsIn<TimedOut>()).IsTrue();
        await Assert.That(token.IsCancellationRequested).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("stopped asking");
    }

    [Test]
    public async Task OnTimerExceptionSeesWhatATimersTransitionThrowsAndTheMachineCarriesOn()
    {
        var context = new RecordingContext();
        var hooks = new ContractHooks(context.Log) { HandleTimerExceptions = true };
        var machine = await StartAsync(Shape, context, hooks);
        context.Failing.Add("explode");
        await machine.FireAsync((byte)8);
        await Advance(machine, context, 1);

        await Assert.That(context.Trace).IsEqualTo("transitioned TimingModule.Arm | timer hook TimingModule.Explode: explode failed | transitioned TimingModule.Ignore");
        await Assert.That(machine.IsIn<Exploding>()).IsTrue();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Running);

        await machine.FireAsync((byte)0);
        await Assert.That(machine.IsIn<Idle>()).IsTrue();
    }

    [Test]
    public async Task WithoutTheHookATimersExceptionIsDropped()
    {
        var (machine, context) = await Started();
        context.Failing.Add("explode");
        await machine.FireAsync((byte)8);
        await Advance(machine, context, 1);

        await Assert.That(machine.IsIn<Exploding>()).IsTrue();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Running);
    }

    [Test]
    public async Task StoppingCancelsEveryTimer()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)1);
        await machine.StopAsync();
        context.Log.Clear();
        context.Clock.Advance(TimeSpan.FromSeconds(3600));
        await Assert.That(machine.IsIn<Recording>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TheDefinitionNamesEachTimersDelay()
    {
        var (machine, _) = await Started();
        var triggers = machine.Definition.Transitions.ToDictionary(t => t.Name, t => t.Trigger);
        await Assert.That(triggers["TimingModule.GiveUp"]).IsEqualTo(TriggerDefinition.ForTimer(TimeSpan.FromSeconds(60)));
        await Assert.That(triggers["TimingModule.Explode"].ToString()).IsEqualTo("after 1s");
        await Assert.That(triggers["TimingModule.Waited"]).IsEqualTo(TriggerDefinition.ForTimer(null));
        await Assert.That(triggers["TimingModule.Waited"].ToString()).IsEqualTo("after Delay");
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Finals;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>Final states, done and eventless transitions, completion, the observer, and pausing.</summary>
public abstract class StatechartContract : MachineContract
{
    private async Task<IStatechart<byte>> Started(RecordingContext context) => (IStatechart<byte>)await StartAsync(Shapes.Finals, context);

    [Test]
    public async Task EnteringAFinalChildFiresTheParentsDoneTransition()
    {
        var context = new RecordingContext();
        var machine = await Started(context);
        await machine.FireAsync(1);
        await machine.FireAsync(2);

        await Assert.That(machine.StateType).IsEqualTo(typeof(Review));
        await Assert.That(context.Trace).IsEqualTo("job done");
    }

    [Test]
    public async Task AnEventlessTransitionFiresAsSoonAsItsGuardPasses()
    {
        var context = new RecordingContext();
        context.Allow.Add("Gate");
        var machine = await Started(context);
        await machine.FireAsync(1);
        await machine.FireAsync(2);
        await machine.FireAsync(3);

        await Assert.That(machine.StateType).IsEqualTo(typeof(Closed));
        await Assert.That(context.Trace).IsEqualTo("job done | entered Closed | through");
    }

    [Test]
    public async Task AnEventlessTransitionIsTriedAgainAfterAStay()
    {
        var context = new RecordingContext();
        var machine = await Started(context);
        await machine.FireAsync(1);
        await machine.FireAsync(2);
        await machine.FireAsync(3);
        await Assert.That(machine.StateType).IsEqualTo(typeof(Gate));

        context.Allow.Add("Gate");
        await machine.FireAsync(4);
        await Assert.That(machine.StateType).IsEqualTo(typeof(Closed));
    }

    [Test]
    public async Task EnteringAFinalChildOfTheRootCompletesTheMachine()
    {
        var context = new RecordingContext();
        context.Allow.Add("Gate");
        var machine = await Started(context);
        var completion = machine.Completion;
        await Assert.That(completion.IsCompleted).IsFalse();

        await machine.FireAsync(1);
        await machine.FireAsync(2);
        await machine.FireAsync(3);

        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Completed);
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        var refused = await Assert.That(async () => await machine.FireAsync(7)).Throws<MachineNotRunningException>();
        await Assert.That(refused!.Status).IsEqualTo(MachineStatus.Completed);
        await Assert.That(context.Trace).DoesNotContain("ping");
    }

    [Test]
    public async Task StoppingACompletedMachineRunsItsExitedActions()
    {
        var context = new RecordingContext();
        context.Allow.Add("Gate");
        var machine = (IStatechart<byte>)Create(Shapes.Finals, context, null);
        await machine.StartAsync<Gate>();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Completed);

        context.Log.Clear();
        await machine.StopAsync();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Trace).IsEqualTo("exited FinRoot");
    }

    [Test]
    public async Task AnEventlessLoopIsStopped()
    {
        var context = new RecordingContext();
        context.Allow.Add("Loop");
        var machine = await Started(context);
        await Assert.That(async () => await machine.FireAsync(9)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StoppingCompletesCompletion()
    {
        var machine = await Started(new RecordingContext());
        var completion = machine.Completion;
        await machine.StopAsync();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
    }

    [Test]
    public async Task TheObserverSeesTransitionsAndUnhandledTriggers()
    {
        var machine = await Started(new RecordingContext());
        var observer = new RecordingObserver();
        machine.Observer = observer;
        await machine.FireAsync(1);
        await machine.FireAsync(2);
        await machine.FireAsync(200);

        await Assert.That(string.Join(" | ", observer.Seen))
            .IsEqualTo("FinalModule.Begin Idle->Working | FinalModule.Finish Working->Finished | FinalModule.JobDone Finished->Review | unhandled 200 in Review");
    }

    [Test]
    public async Task APausedMachineRefusesTriggersUntilResumed()
    {
        var context = new RecordingContext();
        var machine = await Started(context);
        machine.Pause();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Paused);
        var refused = await Assert.That(async () => await machine.FireAsync(7)).Throws<MachineNotRunningException>();
        await Assert.That(refused!.Status).IsEqualTo(MachineStatus.Paused);
        await Assert.That(() => machine.Pause()).Throws<InvalidOperationException>();

        machine.Resume();
        await machine.FireAsync(7);
        await Assert.That(context.Trace).IsEqualTo("ping");
        await Assert.That(() => machine.Resume()).Throws<InvalidOperationException>();
    }

    private sealed class RecordingObserver : IMachineObserver<byte>
    {
        public List<string> Seen { get; } = [];

        public void Transitioned(in TransitionInfo<byte> transition) => Seen.Add($"{transition.Transition} {transition.Leaf.Name}->{transition.Target.Name}");

        public void UnhandledValue(Type state, byte value) => Seen.Add($"unhandled {value} in {state.Name}");

        public void UnhandledEvent(Type state, Type eventType) => Seen.Add($"unhandled {eventType.Name} in {state.Name}");
    }
}

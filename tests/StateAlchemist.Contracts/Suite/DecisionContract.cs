using System;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/decisions.md: outcomes, the pending state, interruption, failure and stopping.</summary>
public abstract class DecisionContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(MachineShape? shape = null, ContractHooks? hooks = null, RecordingContext? context = null)
    {
        context ??= new RecordingContext();
        var machine = await StartAsync(shape ?? Shapes.Deciding, context, hooks);
        context.Log.Clear();
        return (machine, context);
    }

    private static T Data<T>(IMachine<byte> machine)
        where T : struct => machine.TryGetState(out T value) ? value : throw new InvalidOperationException($"{typeof(T).Name} is not active");

    private static Verdict Accepted(string name) => new Accept(name);

    /// <summary>
    /// Waits until the decision has started, and returns its token — or, if <paramref name="fire"/> finishes first,
    /// surfaces that instead of waiting for a decision that will never start.
    /// </summary>
    private static async Task<CancellationToken> DecisionStarted(RecordingContext context, Task fire)
    {
        if (await Task.WhenAny(context.Deciding.Task, fire) == fire)
        {
            await fire;
            throw new InvalidOperationException("FireAsync completed without starting a decision.");
        }

        return await context.Deciding.Task;
    }

    [Test]
    public async Task ASynchronousDecisionCompletesAsOneTransition()
    {
        var (machine, context) = await Started();
        await machine.FireAsync(new byte[] { 7, 3 });
        await Assert.That(context.Trace).IsEqualTo("quick 1");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("quick");

        await machine.FireAsync(new byte[] { 4, 3 });
        await Assert.That(Data<Refused>(machine).Code).IsEqualTo(3);
    }

    [Test]
    public async Task FireAsyncWaitsThroughAnAsyncDecisionWhileTheSourceKeepsItsData()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)7);
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        await Assert.That(fire.IsCompleted).IsFalse();
        await Assert.That(Data<Asking>(machine).Question).IsEqualTo(1);
        await Assert.That(Data<DecideRoot>(machine).Ticks).IsEqualTo(0);

        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await Assert.That(context.Trace).IsEqualTo("deciding 1 | welcome ann as ann | tick");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("ann");
        await Assert.That(Data<DecideRoot>(machine).Ticks).IsEqualTo(1);
    }

    [Test]
    public async Task EachOutcomeRunsItsOwnCompleteAndCompleted()
    {
        var (machine, context) = await Started();
        await machine.FireAsync(new byte[] { 7, 7 });
        var fire = machine.FireAsync((byte)2).AsTask();
        await DecisionStarted(context, fire);
        context.Answer.SetResult(new Verdict(new Reject(40)));
        await fire;
        await Assert.That(Data<Refused>(machine).Code).IsEqualTo(42);
        await Assert.That(context.Trace).IsEqualTo("deciding 2 | refused 40");
    }

    [Test]
    public async Task AHandledEventLeavesThePendingStateAndCancelsTheDecision()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        var cancellation = await DecisionStarted(context, fire);

        await machine.FireAsync(new Hangup());
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await fire;
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | hung up | tick");

        context.Answer.SetResult(Accepted("late"));
        await Assert.That(machine.IsIn<Asking>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | hung up | tick");
    }

    [Test]
    public async Task OtherEventsWaitForTheDecisionThenRunBeforeTheRestOfTheInput()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        var nudge = machine.FireAsync(new Nudge()).AsTask();
        await Assert.That(nudge.IsCompleted).IsFalse();
        await Assert.That(context.Log).DoesNotContain("nudged");

        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await nudge;
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | welcome ann as ann | nudged | tick");
    }

    [Test]
    public async Task AFailingDecisionFiresDecisionFailed()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync((byte)2).AsTask();
        await DecisionStarted(context, fire);
        context.Answer.SetException(new InvalidOperationException("no service"));
        await fire;
        await Assert.That(machine.IsIn<Refused>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | failed DecidingModule.Ask: no service");
    }

    [Test]
    public async Task ASynchronousDecisionThatThrowsFiresDecisionFailedToo()
    {
        var (machine, context) = await Started();
        context.Failing.Add("quick 0");
        await machine.FireAsync((byte)3);
        await Assert.That(machine.IsIn<Refused>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("failed DecidingModule.Quick: quick 0 failed");
    }

    [Test]
    public async Task AnOutcomeThatThrowsFailsTheInputThatStartedTheDecision()
    {
        var (machine, context) = await Started();
        context.Failing.Add("welcome ann as ann");
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);
        context.Answer.SetResult(Accepted("ann"));

        await Assert.That(async () => await fire).Throws<InvalidOperationException>().WithMessage("welcome ann as ann failed");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("ann");
        await Assert.That(context.Log).DoesNotContain("tick");
    }

    [Test]
    public async Task AnInputThatFailsWhileItsDecisionIsPendingAbandonsTheDecision()
    {
        var (machine, context) = await Started();
        context.Failing.Add("hung up");
        var fire = machine.FireAsync(new byte[] { 9, 1 }).AsTask();
        var cancellation = await DecisionStarted(context, fire);

        await Assert.That(async () => await fire).Throws<InvalidOperationException>().WithMessage("hung up failed");
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(machine.IsIn<Asking>()).IsTrue();
        await Assert.That(context.Log).DoesNotContain("tick");
    }

    [Test]
    public async Task AFailureNothingHandlesIsAnUnhandledTrigger()
    {
        var context = new RecordingContext();
        var (machine, _) = await Started(hooks: new ContractHooks(context.Log), context: context);
        await machine.FireAsync(new byte[] { 7, 3 });
        context.Log.Clear();

        var fire = machine.FireAsync((byte)8).AsTask();
        context.Answer.SetException(new InvalidOperationException("gone"));
        await fire;
        await Assert.That(context.Trace).IsEqualTo("unhandled DecisionFailed in Account");
        await Assert.That(Data<Account>(machine).Name).IsEqualTo("quick");
    }

    [Test]
    public async Task StoppingCancelsThePendingDecisionAndDiscardsTheRestOfTheInput()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        var cancellation = await DecisionStarted(context, fire);

        await machine.StopAsync();
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(async () => await fire).Throws<MachineNotRunningException>();
        await Assert.That(context.Log).DoesNotContain("tick");
    }

    [Test]
    public async Task ADecisionThatFiresItsOwnMachineFailsInsteadOfWaitingForItself()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)5);
        await Assert.That(machine.IsIn<Refused>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("failed DecidingModule.Loop: " + new ConcurrentUseException().Message);
    }

    [Test]
    public async Task ADecisionEnqueuesAndTheEventRunsAfterItsOutcome()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)6);
        await Assert.That(context.Trace).IsEqualTo("welcome queued | nudged");
    }

    [Test]
    public async Task ACheckedMachineRefusesAnotherCallersValuesWhileADecisionIsPending()
    {
        var (machine, context) = await Started();
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<ConcurrentUseException>();
        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await Assert.That(Data<DecideRoot>(machine).Ticks).IsEqualTo(1);
    }

    [Test]
    public async Task ASerializedMachineQueuesAnotherCallersValuesBehindTheWaitingInput()
    {
        var (machine, context) = await Started(Shapes.DecidingSerialized);
        var fire = machine.FireAsync(new byte[] { 2, 1 }).AsTask();
        await DecisionStarted(context, fire);

        var other = machine.FireAsync((byte)1).AsTask();
        await Assert.That(other.IsCompleted).IsFalse();
        context.Answer.SetResult(Accepted("ann"));
        await fire;
        await other;
        await Assert.That(context.Trace).IsEqualTo("deciding 0 | welcome ann as ann | tick | tick");
    }

    [Test]
    public async Task APlanShowsADecisionWithoutStartingIt()
    {
        var (machine, context) = await Started();
        var plan = machine.Plan(2);
        await Assert.That(plan.IsDecision).IsTrue();
        await Assert.That(plan.Transition).IsEqualTo("DecidingModule.Ask");
        await Assert.That(context.Trace).IsEqualTo("");
    }
}

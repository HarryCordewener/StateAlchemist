using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Failures;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/exceptions.md: the defaults, and every resolution a hook can choose.</summary>
public abstract class ExceptionContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Failing(string entry, ContractHooks? hooks = null, RecordingContext? context = null)
    {
        context ??= new RecordingContext();
        context.Failing.Add(entry);
        return (await StartAsync(Shapes.Failures, context, hooks), context);
    }

    private static ContractHooks Resolving(RecordingContext context, ExceptionResolution resolution, Action<IMachine<byte>>? after = null) =>
        new(context.Log) { HandleExceptions = true, Resolution = _ => resolution, AfterException = after };

    [Test]
    public async Task AGuardThatThrowsCommitsNothing()
    {
        var (machine, context) = await Failing("guard Go");
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("");
    }

    [Test]
    public async Task ATransformThatThrowsCommitsNothingAndRunsNoAction()
    {
        var (machine, context) = await Failing("transform Go");
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go");
    }

    [Test]
    public async Task AnActionThatThrowsHasCommittedAndSkipsTheRest()
    {
        var (machine, context) = await Failing("entered Moved");
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Moved>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go | transform Go | exited Calm");
    }

    [Test]
    public async Task SkipOnAGuardCountsItAsFalse()
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("guard Go", Resolving(context, ExceptionResolution.Skip), context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("hook Guard: guard Go failed | unhandled 1 in Calm");
    }

    [Test]
    [Arguments(ExceptionResolution.Skip)]
    [Arguments(ExceptionResolution.Continue)]
    public async Task SkipOrContinueOnATransformDropsTheTrigger(ExceptionResolution resolution)
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("transform Go", Resolving(context, resolution), context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go | hook Transform: transform Go failed");
    }

    [Test]
    public async Task SkipOnAnActionSkipsTheRestAndReturnsNormally()
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("exited Calm", Resolving(context, ExceptionResolution.Skip), context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Moved>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("guard Go | transform Go | hook Exited Calm: exited Calm failed | transitioned FailureModule.Go");
    }

    [Test]
    public async Task ContinueOnAnActionRunsTheRest()
    {
        var context = new RecordingContext();
        var (machine, _) = await Failing("entered Moved", Resolving(context, ExceptionResolution.Continue), context);
        await machine.FireAsync((byte)1);
        await Assert.That(context.Trace).IsEqualTo(
            "guard Go | transform Go | exited Calm | hook Entered Moved: entered Moved failed | entered Moved again | completed Go | transitioned FailureModule.Go");
    }

    [Test]
    public async Task AHookCanQueueRecoveryThatRunsWhenTheTransitionEnds()
    {
        var context = new RecordingContext();
        var hooks = Resolving(context, ExceptionResolution.Skip, machine => machine.Enqueue(new Recover()));
        var (machine, _) = await Failing("completed Go", hooks, context);
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo(
            "guard Go | transform Go | exited Calm | entered Moved | entered Moved again | hook Completed: completed Go failed | transitioned FailureModule.Go | recovered | transitioned FailureModule.Recovered");
    }

    [Test]
    public async Task EventsQueuedBeforeARethrowRunBeforeTheNextTrigger()
    {
        var context = new RecordingContext();
        var hooks = Resolving(context, ExceptionResolution.Rethrow, machine => machine.Enqueue(new Recover()));
        var (machine, _) = await Failing("completed Go", hooks, context);
        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();
        await Assert.That(machine.IsIn<Moved>()).IsTrue();

        await machine.FireAsync((byte)2);
        await Assert.That(machine.IsIn<Calm>()).IsTrue();
        await Assert.That(context.Log.Contains("recovered")).IsTrue();
    }
}

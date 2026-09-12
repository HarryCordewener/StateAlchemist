using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Samples.Performance;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// The inbox under contention. Each of these failed against 1.0.0, so each names what it is defending: a
/// synchronous call that cannot wait on a pooled source, a stop that arrives between a caller's check and its
/// join, a handled event that leaves the inbox without freeing its room, and two disposals at once.
/// </summary>
[NotInParallel]
public class InboxRaceTests
{
    /// <summary>Fails a test rather than hanging the run when a call never completes.</summary>
    private static async Task Within(Task work, string what)
    {
        var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10)));
        await Assert.That(finished == work).IsTrue().Because(what);
        await work;
    }

    /// <summary>
    /// <c>Fire</c> on a Serialized machine: whoever does not get the pump has to block until the pump has run
    /// their input. Calling <c>GetResult</c> on a pooled source that is not finished throws instead of waiting,
    /// and returns a queued input to the pool, which then serves two callers at once.
    /// </summary>
    [Test]
    public async Task FiringSynchronouslyFromManyThreadsRunsEveryTrigger()
    {
        const int threads = 4;
        const int each = 2_000;

        var machine = new SyncSerializedMachine(new Counters());
        await machine.StartAsync();
        var thrown = new ConcurrentBag<Exception>();

        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < each; i++)
            {
                try
                {
                    machine.Fire((byte)1);
                }
                catch (Exception exception)
                {
                    thrown.Add(exception);
                }
            }
        })));

        await Assert.That(thrown.Select(e => e.GetType().Name).Distinct()).IsEmpty();
        machine.TryGetText(out var text);
        await Assert.That(text.Count).IsEqualTo(threads * each);
        await machine.StopAsync();
    }

    /// <summary>
    /// A caller that reads the status, is preempted, and joins the inbox after <c>Abandon</c> has drained it used
    /// to wait forever. Every call must end, one way or the other.
    /// </summary>
    [Test]
    public async Task StoppingWhileOthersFireStrandsNobody()
    {
        for (var round = 0; round < 40; round++)
        {
            var machine = new SyncSerializedMachine(new Counters());
            await machine.StartAsync();

            var firing = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                for (var i = 0; i < 50; i++)
                {
                    try
                    {
                        await machine.FireAsync((byte)1);
                    }
                    catch (MachineNotRunningException)
                    {
                        return;
                    }
                }
            })).ToArray();

            await Task.Yield();
            await machine.StopAsync();
            await Within(Task.WhenAll(firing), "a stop must not leave a caller waiting forever");
        }
    }

    /// <summary>
    /// A bounded inbox with a decision: an event the decision handles is taken out of the inbox by the pump, and
    /// the room it held has to go back. Two rounds used to exhaust a capacity of two, and the third parked.
    /// </summary>
    [Test]
    public async Task HandledEventsGiveBackTheirRoom()
    {
        const int rounds = 6;
        var gate = new Gatekeeper();
        var machine = new BoundedDecidingMachine(gate);
        await machine.StartAsync();

        for (var round = 0; round < rounds; round++)
        {
            gate.Answer = new TaskCompletionSource<Verdict>(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.Deciding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var asked = machine.FireAsync((byte)1).AsTask();
            await Within(gate.Deciding.Task, "the decision must start");
            await Within(machine.FireAsync(new Nudge()).AsTask(), $"a handled event must be taken in round {round}");

            gate.Answer.SetResult(new Accept("yes"));
            await Within(asked, $"the decision must finish in round {round}");
            await Within(machine.FireAsync((byte)2).AsTask(), $"the machine must accept more after round {round}");
        }

        await Assert.That(gate.Nudges).IsEqualTo(rounds);
        await machine.StopAsync();
    }

    /// <summary>
    /// <c>await using</c> around an explicit <c>StopAsync</c> puts two threads in the stop path. The exit actions
    /// belong to whichever claims it; the other returns.
    /// </summary>
    [Test]
    public async Task StoppingAndDisposingAtOnceRunsTheExitActionsOnce()
    {
        for (var round = 0; round < 500; round++)
        {
            var gate = new Gatekeeper();
            var machine = new ExitCountMachine(gate);
            await machine.StartAsync();

            await Task.WhenAll(
                Task.Run(async () => await machine.StopAsync()),
                Task.Run(async () => await machine.DisposeAsync()));

            await Assert.That(gate.Exits).IsEqualTo(1).Because($"round {round} ran the exit actions {gate.Exits} times");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Samples.Performance;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests.Performance;

/// <summary>
/// Spec §9: every synchronous path allocates nothing. Measured per call after warming up, on the test thread. The
/// suite runs in Debug, where an async method allocates even when it completes synchronously, so a zero also proves
/// the synchronous paths call none; the release workflow runs the same tests in Release.
/// </summary>
[NotInParallel]
public class AllocationTests
{
    private static readonly byte[] Kilobyte = Enumerable.Repeat((byte)'a', 1024).ToArray();

    private static long PerCall(Func<ValueTask> fire)
    {
        for (var i = 0; i < 100; i++)
        {
            Complete(fire());
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            Complete(fire());
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
    }

    private static void Complete(ValueTask fired)
    {
        if (!fired.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The call did not complete synchronously.");
        }

        fired.GetAwaiter().GetResult();
    }

    private static async Task<T> Started<T>(T machine)
        where T : IMachine<byte>
    {
        await machine.StartAsync();
        return machine;
    }

    [Test]
    public async Task AStayAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync((byte)1))).IsEqualTo(0);
    }

    [Test]
    public async Task AMoveAcrossTwoLevelsWithSynchronousActionsAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() =>
        {
            Complete(machine.FireAsync((byte)2));
            return machine.FireAsync((byte)3);
        })).IsEqualTo(0);
    }

    [Test]
    public async Task AKilobyteRunAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync(Kilobyte))).IsEqualTo(0);
    }

    [Test]
    public async Task AnEventAllocatesNothing()
    {
        var machine = await Started(new InlineMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync(new Tick()))).IsEqualTo(0);
    }

    [Test]
    public async Task ASerializedMachineAllocatesNothingPerCall()
    {
        var machine = await Started(new SerializedMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync((byte)1))).IsEqualTo(0);
        await Assert.That(PerCall(() => machine.FireAsync(Kilobyte))).IsEqualTo(0);
    }

    /// <summary>
    /// What the machine may add to a suspending action, above the cost of awaiting that action directly: one
    /// continuation and the state machine the compiler writes for it.
    /// </summary>
    /// <remarks>
    /// A ceiling with room in it, not a measurement. The exact size is the compiler's business and it moves
    /// between builds and runtimes — measured here, per call:
    /// <code>
    ///            alone   through   machine adds
    /// Debug   net8.0    48 B   304 B   256 B
    /// Debug   net10.0   48 B   304 B   256 B
    /// Debug   net11.0   48 B   288 B   240 B
    /// Release net8.0     0 B   128 B   128 B
    /// Release net10.0    0 B   128 B   128 B
    /// Release net11.0    0 B   112 B   112 B
    /// </code>
    /// At 256 this passed by nothing at all on two of those six, which is a test that fails on somebody else's
    /// machine for a reason that is not a regression. The assertion that carries the weight is the equality above
    /// it: a suspending call costs the same every time, so nothing accumulates per call. This one is here to
    /// catch a change of kind — a Task allocated per call, a state machine that grows with the machine.
    /// </remarks>
    private const long Suspension = 512;

    /// <summary>A suspending action costs a fixed amount every time (spec §9): the call finishes in one async method.</summary>
    [Test]
    public async Task ASuspendingActionCostsTheSameEveryTime()
    {
        var counters = new Counters();
        var machine = await Started(new InlineMachine(counters));

        // On one thread: every continuation comes back to this pump, so the per-thread counter sees all of it.
        var pump = new OneThread();
        var alone = pump.Measure(() => PerformanceModule.Suspend.CompletedAsync(counters));
        var first = pump.Measure(() => machine.FireAsync((byte)4));
        var again = pump.Measure(() => machine.FireAsync((byte)4));
        await Assert.That(again).IsEqualTo(first).Because("a suspending call costs the same every time");
        await Assert.That(first).IsLessThanOrEqualTo(alone + Suspension).Because($"firing a suspending action adds a bounded amount to it: {alone} B alone, {first} B through the machine");
    }

    /// <summary>Runs suspending work on the calling thread: <c>await</c> posts here, and this pump runs it.</summary>
    private sealed class OneThread : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_work)
            {
                _work.Enqueue((callback, state));
            }
        }

        /// <summary>Bytes one call of <paramref name="fire"/> allocates, awaited to completion, after warming up.</summary>
        public long Measure(Func<ValueTask> fire)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                Run(fire, 100);
                var before = GC.GetAllocatedBytesForCurrentThread();
                Run(fire, 1000);
                return (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        private void Run(Func<ValueTask> fire, int times)
        {
            for (var i = 0; i < times; i++)
            {
                var fired = fire();
                while (!fired.IsCompleted)
                {
                    (SendOrPostCallback Callback, object? State) next;
                    lock (_work)
                    {
                        if (_work.Count == 0)
                        {
                            continue;
                        }

                        next = _work.Dequeue();
                    }

                    next.Callback(next.State);
                }

                fired.GetAwaiter().GetResult();
            }
        }
    }

    [Test]
    public async Task AMachineWithAnInboxAllocatesNothingForOrdinaryInput()
    {
        var machine = await Started(new DecidingMachine(new Counters()));
        await Assert.That(PerCall(() => machine.FireAsync((byte)1))).IsEqualTo(0);
        await Assert.That(PerCall(() => machine.FireAsync(Kilobyte))).IsEqualTo(0);
    }
}

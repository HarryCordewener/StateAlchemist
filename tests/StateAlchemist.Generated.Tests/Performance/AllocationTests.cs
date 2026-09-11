using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Samples.Performance;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests.Performance;

/// <summary>
/// Spec §9: every synchronous path allocates nothing. Measured per call after warming up, on the test thread, in the
/// Debug build the suite runs — where an async method allocates even when it completes synchronously, so these also
/// prove the synchronous paths call none.
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
    /// A suspending action costs a fixed amount every time (spec §9): the call finishes in one async method, so
    /// firing the action adds a bounded amount to awaiting it directly, and never more as the calls go on. The
    /// Debug build the suite runs compiles async state machines as classes, which defeats the pooled builder; what
    /// a Release build costs is in the benchmarks.
    /// </summary>
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
        await Assert.That(first).IsLessThanOrEqualTo(8 * alone).Because($"firing a suspending action adds a bounded amount to it: {alone} B alone, {first} B through the machine");
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

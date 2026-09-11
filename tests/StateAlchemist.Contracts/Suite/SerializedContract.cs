using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/concurrency.md, "Serialized": any thread may fire; calls run one at a time, in turn.</summary>
public abstract class SerializedContract : MachineContract
{
    [Test]
    public async Task ASerializedMachineRunsOverlappingCallersInTurn()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.RecorderSerialized, context);
        var held = machine.FireAsync((byte)11).AsTask();
        var queued = machine.FireAsync((byte)3).AsTask();
        await Assert.That(queued.IsCompleted).IsFalse();

        context.Gate.SetResult();
        await held;
        await queued;
        await Assert.That(machine.TryGetState(out Machines.Recording.A1 a1) ? a1.Value : -1).IsEqualTo(1);
    }

    [Test]
    public async Task ASerializedMachineGivesEachCallerItsOwnException()
    {
        var context = new RecordingContext();
        context.Failing.Add("completed Sibling");
        var machine = await StartAsync(Shapes.RecorderSerialized, context);
        var held = machine.FireAsync((byte)11).AsTask();
        var failing = machine.FireAsync((byte)1).AsTask();

        context.Gate.SetResult();
        await held;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await failing);
        await Assert.That(thrown!.Message).IsEqualTo("completed Sibling failed");
    }

    [Test]
    public async Task ASerializedMachineTakesCallersFromManyThreads()
    {
        var machine = await StartAsync(Shapes.RecorderSerialized, new RecordingContext());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await machine.FireAsync((byte)3);
            }
        })));
        await Assert.That(machine.TryGetState(out Machines.Recording.A1 a1) ? a1.Value : -1).IsEqualTo(400);
    }
}

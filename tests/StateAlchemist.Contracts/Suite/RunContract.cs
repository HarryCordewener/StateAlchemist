using System;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Runs;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/runs.md: a run is taken in one call, ends at its stop set, and changes speed, never results.</summary>
public abstract class RunContract : MachineContract
{
    protected virtual MachineShape Shape => Shapes.Runs;

    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(params string[] allow)
    {
        var context = new RecordingContext();
        context.Allow.UnionWith(allow);
        var machine = await StartAsync(Shape, context);
        context.Log.Clear();
        return (machine, context);
    }

    [Test]
    public async Task ABatchHandsARunOverInOneCall()
    {
        var (machine, context) = await Started();
        await machine.FireAsync("hello"u8.ToArray());
        await Assert.That(context.Trace).IsEqualTo("run 5 | appended 5");
        await Assert.That(machine.TryGetState(out Text text) ? text.Length : -1).IsEqualTo(5);
    }

    [Test]
    public async Task ARunEndsAtTheFirstValueAnotherTransitionTakes()
    {
        var (machine, context) = await Started();
        await machine.FireAsync("ab\ncd"u8.ToArray());
        await Assert.That(context.Trace).IsEqualTo("run 2 | appended 2 | run 2 | appended 2");
        await Assert.That(machine.TryGetState(out RunRoot root) ? root.Lines : -1).IsEqualTo(1);
    }

    [Test]
    public async Task AStopValueWhoseGuardFailsReachesTheRunAsARunOfOne()
    {
        var (refusing, refused) = await Started();
        await refusing.FireAsync("ab\acd"u8.ToArray());
        await Assert.That(refused.Trace).IsEqualTo("run 2 | appended 2 | run 1 | appended 1 | run 2 | appended 2");

        var (ringing, rung) = await Started("Bell");
        await ringing.FireAsync("ab\acd"u8.ToArray());
        await Assert.That(rung.Trace).IsEqualTo("run 2 | appended 2 | bell | run 2 | appended 2");
    }

    [Test]
    public async Task ASingleValueIsARunOfOne()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)'a');
        await Assert.That(context.Trace).IsEqualTo("run 1 | appended 1");
    }

    [Test]
    public async Task RunsChangeHowFastABatchIsConsumedNeverWhatItDoes()
    {
        byte[] input = [.. "ab\ncd"u8, 255, .. "xyz\a"u8];
        var (batched, _) = await Started();
        await batched.FireAsync(input);
        var (single, _) = await Started();
        foreach (var value in input)
        {
            await single.FireAsync(value);
        }

        foreach (var machine in new[] { batched, single })
        {
            await Assert.That(machine.StateType).IsEqualTo(typeof(Text));
            await Assert.That(machine.TryGetState(out Text text) ? text.Length : -1).IsEqualTo(3);
            await Assert.That(machine.TryGetState(out RunRoot root) ? root.Lines : -1).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ACompletedActionCanReturnTheUnconsumedSuffixToTheCaller()
    {
        var (machine, context) = await Started();
        var input = "ab|cd"u8.ToArray();

        var boundaryMachine = (IBoundaryMachine<byte>)machine;
        var consumed = await boundaryMachine.FireUntilBoundaryAsync(input);

        await Assert.That(consumed).IsEqualTo(3);
        await Assert.That(context.Trace).IsEqualTo("run 2 | appended 2 | boundary | queued after boundary");
        await Assert.That(machine.TryGetState(out Text text) ? text.Length : -1).IsEqualTo(2);

        await Assert.That(await boundaryMachine.FireUntilBoundaryAsync(input.AsMemory(consumed))).IsEqualTo(2);
        await Assert.That(machine.TryGetState(out text) ? text.Length : -1).IsEqualTo(4);
    }

    [Test]
    public async Task TheExistingBatchApiContinuesAcrossACooperativeBoundary()
    {
        var (machine, context) = await Started();

        await machine.FireAsync("ab|cd"u8.ToArray());

        await Assert.That(context.Trace).IsEqualTo("run 2 | appended 2 | boundary | queued after boundary | run 2 | appended 2");
        await Assert.That(machine.TryGetState(out Text text) ? text.Length : -1).IsEqualTo(4);
    }

    [Test]
    public async Task RequestingABatchBoundaryOutsideTheMachineIsRejected()
    {
        var (machine, _) = await Started();

        await Assert.That(() => ((IBoundaryMachine<byte>)machine).RequestBatchBoundary()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ARunCanRequestABoundaryAfterTheWholeRun()
    {
        var (machine, context) = await Started("BoundaryAfterRun");

        var consumed = await ((IBoundaryMachine<byte>)machine).FireUntilBoundaryAsync("abcdef"u8.ToArray());

        await Assert.That(consumed).IsEqualTo(6);
        await Assert.That(context.Trace).IsEqualTo("run 6 | appended 6");
    }
}

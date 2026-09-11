using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests.Performance;

/// <summary>
/// docs/concepts/concurrency.md, "Bounded inbox": callers that outrun a Serialized machine wait for room instead of
/// growing its queue. What the bound saves is memory, which no contract can see, so this looks at the generated
/// inbox itself.
/// </summary>
public class BoundedInboxTests
{
    private static int Queued(object machine) =>
        ((ICollection)machine.GetType().GetField("_inbox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(machine)!).Count;

    [Test]
    public async Task ACallerFindingTheInboxFullWaitsForRoom()
    {
        var context = new RecordingContext();
        var machine = new BoundedRecorderMachine(context);
        context.Machine = machine;
        await machine.StartAsync();

        var held = machine.FireAsync((byte)11).AsTask();
        var first = machine.FireAsync((byte)3).AsTask();
        var second = machine.FireAsync((byte)3).AsTask();
        await Assert.That(Queued(machine)).IsEqualTo(1);

        context.Gate.SetResult();
        await Task.WhenAll(held, first, second);
        await Assert.That(machine.TryGetState(out A1 a1) ? a1.Value : -1).IsEqualTo(2);
    }
}

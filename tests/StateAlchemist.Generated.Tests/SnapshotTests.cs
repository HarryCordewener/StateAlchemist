using System;
using System.Text.Json;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Joins;
using StateAlchemist.Contracts.Machines.Recalling;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>Snapshot and restore (issue #20): a machine saved, serialized, and put back in the same state.</summary>
public class SnapshotTests
{
    // begin-snippet: snapshot-json-options
    /// <summary>State structs keep their data in fields, which System.Text.Json writes only when asked to.</summary>
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    // end-snippet

    private static T RoundTrip<T>(T snapshot) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(snapshot, Json), Json)!;

    [Test]
    public async Task ARestoredMachineHasTheSameLeafAndDataAndRunsNoActions()
    {
        var context = new RecordingContext();
        var machine = new RecallingMachine(context);
        await machine.StartAsync();
        await machine.FireAsync(new byte[] { 3, 1, 2, 8, 8 });   // Player > Playing > Fast, counted twice

        // begin-snippet: snapshot-save-restore
        var json = JsonSerializer.Serialize(machine.TakeSnapshot(), Json);   // after FireAsync has completed

        // Later, perhaps in another process:
        var restoredContext = new RecordingContext();
        var restored = new RecallingMachine(restoredContext);
        restored.Restore(JsonSerializer.Deserialize<RecallingMachine.Snapshot>(json, Json)!);
        await restored.StartAsync();                                           // runs no [Entered] actions
        // end-snippet

        await Assert.That(json).Contains("\"State\":\"Fast\"");
        await Assert.That(restoredContext.Trace).IsEqualTo(string.Empty);
        await Assert.That(restored.IsIn<Fast>()).IsTrue();
        await Assert.That(restored.TryGetFast(out var fast)).IsTrue();
        await Assert.That(fast.Count).IsEqualTo(2);

        await restored.FireAsync((byte)8);
        restored.TryGetFast(out fast);
        await Assert.That(fast.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ARestoredMachineResumesFromTheHistoryItRecorded()
    {
        var machine = new RecallingMachine(new RecordingContext());
        await machine.StartAsync();
        await machine.FireAsync(new byte[] { 3, 1, 2, 9 });      // into Fast, then out to Idle

        var snapshot = RoundTrip(machine.TakeSnapshot());
        await Assert.That(snapshot.History!.Player).IsEqualTo("Fast");

        var restored = new RecallingMachine(new RecordingContext());
        restored.Restore(snapshot);
        await restored.StartAsync();
        restored.TryGetJukebox(out var root);
        await Assert.That(root.Leaves).IsEqualTo(1);

        await restored.FireAsync((byte)5);                        // deep history
        await Assert.That(restored.IsIn<Fast>()).IsTrue();
    }

    [Test]
    public async Task ARestoredJoinRemembersWhatArrived()
    {
        var machine = new JoinsMachine(new RecordingContext());
        await machine.StartAsync();
        await machine.FireAsync(new Paid { Amount = 5 });

        var snapshot = RoundTrip(machine.TakeSnapshot());
        await Assert.That(snapshot.Joins!.JoinModule_Ship!.Paid!.Value.Amount).IsEqualTo(5);
        await Assert.That(snapshot.Joins.JoinModule_Ship.Reserved).IsNull();

        var restored = new JoinsMachine(new RecordingContext());
        restored.Restore(snapshot);
        await restored.StartAsync();
        await restored.FireAsync(new Reserved { Warehouse = "north" });

        await Assert.That(restored.TryGetShipped(out var shipped)).IsTrue();
        await Assert.That(shipped.Label).IsEqualTo("north:5");
    }

    [Test]
    public async Task OnlyActiveStatesAreWritten()
    {
        var machine = new RecallingMachine(new RecordingContext());
        var snapshot = machine.TakeSnapshot();

        await Assert.That(snapshot.State).IsEqualTo("Idle");
        await Assert.That(snapshot.States.Jukebox).IsNotNull();
        await Assert.That(snapshot.States.Idle).IsNotNull();
        await Assert.That(snapshot.States.Player).IsNull();
        await Assert.That(snapshot.History!.Player).IsNull();
    }

    [Test]
    public async Task AnActiveStateTheSnapshotHasNoDataForStartsWithDefaultData()
    {
        var restored = new RecallingMachine(new RecordingContext());
        restored.Restore(new RecallingMachine.Snapshot { State = "Fast" });
        await restored.StartAsync();

        await Assert.That(restored.IsIn<Player>()).IsTrue();
        restored.TryGetFast(out var fast);
        await Assert.That(fast.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TakingASnapshotWhileADecisionIsPendingThrows()
    {
        var context = new RecordingContext();
        var machine = new DecidingMachine(context);
        await machine.StartAsync();
        var fire = machine.FireAsync((byte)2).AsTask();
        await context.Deciding.Task;

        var refused = Assert.Throws<InvalidOperationException>(() => machine.TakeSnapshot());
        await Assert.That(refused!.Message).Contains("DecidingModule.Ask");

        context.Answer.SetResult(new Verdict(new Accept("ann")));
        await fire;
        var snapshot = machine.TakeSnapshot();
        await Assert.That(snapshot.State).IsEqualTo("Account");
        await Assert.That(snapshot.States.Account!.Value.Name).IsEqualTo("ann");
    }

    [Test]
    public async Task AStoppedMachineCannotBeSnapshotted()
    {
        var machine = new RecallingMachine(new RecordingContext());
        await machine.StartAsync();
        await machine.StopAsync();

        Assert.Throws<InvalidOperationException>(() => machine.TakeSnapshot());
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
    }

    [Test]
    public async Task OnlyAMachineThatHasNotStartedCanBeRestored()
    {
        var machine = new RecallingMachine(new RecordingContext());
        await machine.StartAsync();

        Assert.Throws<InvalidOperationException>(() => machine.Restore(new RecallingMachine.Snapshot { State = "Fast" }));
        await Assert.That(machine.IsIn<Idle>()).IsTrue();
    }

    [Test]
    [Arguments("Nowhere")]
    [Arguments("Player")]
    [Arguments(null)]
    public async Task ASnapshotNamingNoLeafOfTheMachineIsRefused(string? state)
    {
        var machine = new RecallingMachine(new RecordingContext());

        Assert.Throws<ArgumentException>(() => machine.Restore(new RecallingMachine.Snapshot { State = state! }));
        await Assert.That(machine.IsIn<Idle>()).IsTrue();
    }

    [Test]
    public async Task RecordedHistoryMustBeALeafUnderItsState()
    {
        var machine = new RecallingMachine(new RecordingContext());
        var snapshot = new RecallingMachine.Snapshot
        {
            State = "Idle",
            History = new RecallingMachine.SnapshotHistory { Player = "Idle" },
        };

        Assert.Throws<ArgumentException>(() => machine.Restore(snapshot));
        await Assert.That(machine.IsIn<Idle>()).IsTrue();
    }
}

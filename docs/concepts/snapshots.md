# Snapshots

A machine can be saved between requests or processes and put back in the same state, with the same state data.
`TakeSnapshot` copies a machine; `Restore` puts a new machine in the state the copy describes.

<!-- snippet: snapshot-save-restore -->
<a id='snippet-snapshot-save-restore'></a>
```cs
var json = JsonSerializer.Serialize(machine.TakeSnapshot(), Json);   // after FireAsync has completed

// Later, perhaps in another process:
var restoredContext = new RecordingContext();
var restored = new RecallingMachine(restoredContext);
restored.Restore(JsonSerializer.Deserialize<RecallingMachine.Snapshot>(json, Json)!);
await restored.StartAsync();                                           // runs no [Entered] actions
```
<sup><a href='/tests/StateAlchemist.Generated.Tests/SnapshotTests.cs#L31-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-snapshot-save-restore' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## What a snapshot holds

`TakeSnapshot` returns the machine's generated `Snapshot` class:

| Property | Holds |
|---|---|
| `State` | The active leaf, as the name of its `StateId` member. |
| `States` | One property per state: the data of each active state, and null for the rest. |
| `History` | For each state some move enters [with history](states.md#going-back-with-history), the leaf that was active when it was last exited, or null before its first exit. Present only on a machine with history. |
| `Joins` | For each [join](triggers.md#joins) whose source is active, the latest payload of each listed event that has arrived. Present only on a machine with joins. |
| `Timers` | For each running [timer](timers.md), the UTC instant it is due, by its transition's name. Null when the machine had not started. Present only on a machine with timers. |

A snapshot leaves out the context and the config, which the machine is constructed with, and the token actions
take for the machine's lifetime.

Every member is a settable property, so a serializer can write and read it. State structs keep their data in
fields, and System.Text.Json does not serialize fields by default
([fields](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/fields)), so set
`IncludeFields`:

<!-- snippet: snapshot-json-options -->
<a id='snippet-snapshot-json-options'></a>
```cs
/// <summary>State structs keep their data in fields, which System.Text.Json writes only when asked to.</summary>
private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
```
<sup><a href='/tests/StateAlchemist.Generated.Tests/SnapshotTests.cs#L16-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-snapshot-json-options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## When a snapshot can be taken

Take a snapshot once `FireAsync` has completed, from outside the machine. `TakeSnapshot` throws
`InvalidOperationException` when:

- **a decision is pending.** The caller whose input started the [decision](decisions.md) is still awaiting
  `FireAsync` and holds the rest of its batch, which the machine cannot copy, so a snapshot taken then would be
  incomplete. The exception names the decision.
- **the machine is processing input**: another caller's `FireAsync` is running on a `Checked` or `Serialized`
  machine, or on any machine with an async decision. On an `Unchecked` machine, keeping a snapshot apart from
  firing is the host's job, as it is for `TryGet{State}`.
- **the call comes from an action or a hook**, which runs mid-transition.
- **the machine is stopped**: stopping has exited its states.

A machine that has not been started can be snapshotted: it is in its initial state, or in the state it was
restored to.

## Restoring

`Restore` is for a machine that has not been started; on a started one it throws `InvalidOperationException`. It
sets the active leaf, the data, the recorded history and the join arrivals, and runs nothing. The `StartAsync` that
follows runs no `[Entered]` actions: the states were entered, and their actions ran, in the machine the snapshot was
taken from. Anything those actions set up outside the machine, such as a connection, is for the host to set up
again.

A timer keeps its due time across the snapshot. The `StartAsync` after `Restore` starts each recorded timer for
what is left of it, measured on the restored machine's `TimeProvider`, and one that fell due while the snapshot was
stored fires at once. A snapshot with no `Timers`, from a machine that had not started or from before the machine
had timers, starts the timers of the active states afresh, as `StartAsync` would.

`Restore` checks the snapshot before changing anything. It throws `ArgumentException` when `State` does not name a
leaf of the machine, or when a recorded history leaf is not a leaf under its state.

## Changing the machine

A snapshot names states, history and joins; it does not number them. So a snapshot taken before a state was
added, or before states were reordered, still restores:

- An active state the snapshot has no data for starts with `default` data.
- A snapshot whose leaf has been renamed or removed is refused with `ArgumentException`.
- Within a state's data, which fields are written and read is up to the serializer.

# Lifecycle

A machine is constructed, started, fired, and stopped.

```csharp
await using var telnet = new SampleTelnet(context);   // not started: nothing has run
await telnet.StartAsync();                            // [Entered] actions on the initial path, root first
await telnet.FireAsync(bytes);                        // running
await telnet.StopAsync();                             // [Exited] actions from the leaf to the root
```

| Status | How it gets there | Firing |
|---|---|---|
| `NotStarted` | construction | throws `MachineNotRunningException` |
| `Running` | `StartAsync` completed | allowed |
| `Stopped` | `StopAsync`, or disposal after starting | throws `MachineNotRunningException` |

## Why starting is separate

**Construction runs no actions.** It resets every state's storage and sets the active leaf to the root's initial
path. Nothing else, so it cannot fail and cannot await.

**`StartAsync` runs the initial path's `[Entered]` actions once** — a second call throws
`InvalidOperationException`. Those actions may `Enqueue` events; they run before the first trigger. If one throws,
the [exception hooks](exceptions.md) apply as they do in a transition, and `Skip` skips the remaining lifecycle
actions. Keeping it separate lets the host finish wiring — a connection, a pipe, a writer — before any action
runs. A telnet server that speaks first sends its offers from an `[Entered]` action, so they go out under
`StartAsync` rather than from a constructor or on the first byte that may never arrive.

Forgetting to start is caught twice:

- **At compile time**, where it can be seen: [`SALCH0801`](../reference/diagnostics.md#salch0801) warns when a
  method creates a machine and fires it without starting it on every path. A machine that crosses methods, fields
  or dependency injection is left to the runtime check.
- **At runtime**, for one comparison: every `FireAsync` checks the machine's status first, and one that is not
  running throws instead of dispatching. The check is a field read and a predicted branch, below what the
  [benchmarks](concurrency.md) can measure.

## Stopping and disposal

`StopAsync` cancels a pending [decision](decisions.md), then runs `[Exited]` actions from the active leaf up to the
root. `DisposeAsync` stops the machine if it was started, and does nothing if it was not. Stopping twice is
harmless.

# Generated API

For a machine declared as

```csharp
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext), Config = typeof(TelnetConfig))]
[Include(typeof(TelnetCore)), Include(typeof(NawsModule))]
public sealed partial class MudTelnet;
```

the generator adds the members below to `MudTelnet`, which also implements `IMachine<byte>`. Nothing it
generates uses a dictionary, a hash lookup, or reflection: dispatch is a `switch` on the active state, then on the
trigger; storage is fields; the definition is static arrays. The generated code is plain C# 7.3, so a
`netstandard2.0` project compiles it on its default language version.

A machine that is `Serialized`, or has an async decision, also gets an inbox: calls become inputs that whoever
finds the machine idle processes in turn, as [concurrency](../concepts/concurrency.md) describes. Any other machine
runs each call inline, with no inbox and no lock.

A machine with an error gets no code at all; its diagnostics say why. Warnings about modules from other assemblies
are reported on the `[Machine]` attribute, where the application chose them. A module's own problems are reported
where the module is written, by the [analyzer](diagnostics.md#which-component-reports-what) in the same package.

## Construction and lifecycle

| Member | |
|---|---|
| `MudTelnet(TelnetContext context, in TelnetConfig config)` | Resets every state and sets the initial leaf. Runs no actions. Without `Config`, the constructor takes only the context; without `Context`, nothing. |
| `ValueTask StartAsync()` | Runs the initial path's `[Entered]` actions once. See [lifecycle](../concepts/lifecycle.md). |
| `ValueTask StopAsync()` | Cancels a pending decision; runs `[Exited]` actions from the leaf to the root. |
| `ValueTask DisposeAsync()` | Stops the machine if it was started. |
| `MachineStatus Status` | `NotStarted`, `Running` or `Stopped`. |

## State

| Member | |
|---|---|
| `StateId State` | The active leaf, as a generated enum with one member per state. |
| `bool IsIn(StateId state)` | Whether the state is the active leaf or one of its ancestors. |
| `bool TryGet{State}(out {State} value)` | One per state: a copy of the state's data, if it is active. |

## Firing

| Member | |
|---|---|
| `ValueTask FireAsync(byte value)` | Fires one value. Every `FireAsync` completes when its input has been processed — including waiting for any [decision](../concepts/decisions.md#deferral-and-backpressure) it started — so awaiting it is the backpressure. |
| `ValueTask FireAsync(ReadOnlyMemory<byte> values)` | Fires values in order, consuming [runs](../concepts/runs.md) in one call. |
| `ValueTask<int> FireUntilBoundaryAsync(ReadOnlyMemory<byte> values)` | Fires until every value is consumed or an action calls `RequestBatchBoundary()`, then returns the consumed count. |
| `ValueTask FireAsync(in {Event} e)` | One per event type the machine handles. |
| `void Fire(byte value)`, `void Fire(ReadOnlyMemory<byte> values)`, `void Fire(in {Event} e)` | The same calls without the `await`, for a machine whose actions and decisions are all synchronous. Using one on a machine that can suspend is [`SALCH0601`](diagnostics.md#salch0601): the call would block the calling thread until the action came back. On a `Serialized` machine the call also blocks while another thread holds the pump, which is what serialising means. The batch takes `ReadOnlyMemory` rather than a span because it is the same code path as `FireAsync`, where a suspended [run](../concepts/runs.md) needs a buffer that outlives the call. |
| `void Enqueue(in {Event} e)` | Queues an event for after the current transition. |
| `void RequestBatchBoundary()` | Asks a consumption-reporting batch to return after the current transition and its queued events. Calls outside the machine throw. Ordinary `FireAsync` batches continue to completion. |

## The pure layer

| Member | |
|---|---|
| `TransitionPlan Plan(byte value)`, `TransitionPlan Plan(in {Event} e)` | What a trigger would do now, evaluating guards, without doing it. |
| `static MachineDefinition Definition` | States, parents, transitions and triggers, as data. A [decision](../concepts/decisions.md) has no target — which one it takes is not known when the trigger arrives — so its `TransitionDefinition.Outcomes` name each outcome case and the state it moves to. |
| `const string Mermaid`, `const string Dot` | The machine as a diagram: a Mermaid `stateDiagram-v2` and a Graphviz digraph, a composite state per parent and an arrow per transition — or, for a decision, an arrow per outcome, so a state only a decision reaches is not drawn as unreachable. Written at compile time from the model the machine runs, so it cannot drift from the code. |

## Hooks

Generated `partial` methods, removed by the compiler unless the application implements them:

| Hook | |
|---|---|
| `partial void OnTransitioned(in TransitionInfo<byte> transition)` | After every transition. |
| `partial void OnUnhandled(StateId state, byte value)`, and one per event type | When nothing handles a trigger. |
| `partial void On{Phase}Exception(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution)` | For `Guard`, `Transform`, `Exited`, `Entered` and `Completed`; see [exceptions](../concepts/exceptions.md). |

## Through the interface

`IMachine<TValue>` exposes the same machine without its generated types, for code that must not depend on one
machine's shape: `StateType` instead of `StateId State`, `IsIn<TState>()`, `TryGetState<TState>(out TState)`,
`FireAsync<TEvent>(TEvent)`, `Enqueue<TEvent>(TEvent)` and `Plan<TEvent>(TEvent)`. The generic members compare
`typeof` constants that the JIT folds away, so they cost no more than the typed ones — except on
`netstandard2.0`, where `TryGetState<TState>` boxes.

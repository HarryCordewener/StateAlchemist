# Actions

Transforms change the machine's data; **actions run your code**: network writes, callbacks, logging. Actions run
*after* the state has changed, so their names are past tense, and they may be async.

## Where an action goes

| Declared as | Runs | Use it for |
|---|---|---|
| a transition's `Completed` / `CompletedAsync` | after this transition | "this happened": reply to an offer, raise a callback |
| `[Exited(typeof(S))]` on a module method | whenever `S` is left, by any transition | cleanup, tracing |
| `[Entered(typeof(S))]` on a module method | whenever `S` is entered, by any transition | "ready" notifications |

<!-- snippet: sample-class-form -->
<a id='snippet-sample-class-form'></a>
```cs
[Transition(From = typeof(Willing), To = typeof(Idle)), On(GmcpOption)]
public static class Accept
{
    public static void Transform(ref Connected root) => root.GmcpEnabled = true;

    public static ValueTask CompletedAsync(TelnetContext context) => context.SendAsync(Iac, Do, GmcpOption);
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/GmcpModule.cs#L11-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-class-form' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## The order of one transition

Every transition runs the same steps. The phase names say where you are: imperative before the state changes,
past tense after.

| Step | Phase | You declare it as | |
|---|---|---|---|
| 1 | **Guard** | the transition's `Guard` | May it fire? Read-only. |
| 2 | *reset* | — | The states being entered are reset. Nothing can see them yet. |
| 3 | **Transform** | the transition's `Transform` | Synchronous. The source is intact, the target fresh. |
| 4 | *commit* | — | The active leaf becomes the target. **The state has now changed.** |
| 5 | **Exited** | `[Exited(typeof(S))]` | For each state left, innermost first. |
| 6 | **Entered** | `[Entered(typeof(S))]` | For each state entered, outermost first. |
| 7 | **Completed** | the transition's `Completed` | Last. |
| 8 | *clear* | — | The states left are cleared. |
| 9 | *drain* | — | Events queued during the transition are processed. |

A stay has no exits or entries: steps 2, 5, 6 and 8 do nothing, and `Completed` follows the transform.

States left are cleared at step 8, not step 4, so the actions in steps 5–7 can still read what was left — a NAWS
`Completed` can report the window size from the state it just exited. Nothing else can observe the difference:
from step 4 those states are no longer active.

### Order among actions for the same state

Several modules may attach actions to the same state and phase. Within one module, declaration order decides.
Across modules, give each an `Order`; two with the same `Order` from different modules are
[`SALCH0103`](../reference/diagnostics.md#salch0103), because nothing else would decide between them.

```csharp
[Exited(typeof(Naws), Order = 1)]
public static void Trace(TelnetContext context, Naws naws) => context.Log.Add($"left NAWS at {naws.Index}");
```

## What an action can take

| Parameter | Meaning |
|---|---|
| the context | your object |
| a state, by value (or `in` for a synchronous action) | a copy of its data: states active after the change, and states just left |
| the value, the event, or a run as `ReadOnlyMemory<TValue>` | what fired the transition (`Completed` only) |
| a decision's outcome | for a decision's `Completed` overloads |
| `CancellationToken` | cancelled when the machine stops |
| `in TransitionInfo<TValue>` | which transition, which phase, which state |

An async action cannot take `ref` or `in` parameters — C# forbids it — which is why actions see copies.

## What async costs

An action returning `ValueTask` that completes synchronously costs nothing extra: the generated code checks
`IsCompletedSuccessfully` and carries on, entering no state machine and allocating nothing. An action that
suspends moves the rest of the call into a continuation. The rest of the call is the transition, the events it
queued and the release, and all of it finishes in one async method, whose state machine is pooled on `net6.0` and
later. So a suspending action costs what the action costs, plus that one continuation.

While a transition's actions are awaited, the transition is still running: it [runs to
completion](#run-to-completion) before the next trigger.

## Run to completion

A transition finishes — through every awaited action — before the next trigger is processed. A trigger fired
*during* a transition, such as an action calling `Enqueue(new Error())`, is queued and processed at step 9.
`Enqueue` is for code running inside the machine: an action, a hook, a decision. Called from outside it throws
`InvalidOperationException`; use `FireAsync` there. The queue is a small buffer inside the machine, and allocates
only if more than four events queue at once. Values are
never queued this way; see [decisions](decisions.md#deferral-and-backpressure).

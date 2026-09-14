# Exceptions

Your code runs inside a machine at five points — `Guard`, `Transform` (or a decision's `Complete`), and the
`Exited`, `Entered` and `Completed` actions. What happens when one throws depends on which side of the state
change it is on.

## By default

| Throws in | The state | Then |
|---|---|---|
| `Guard`, `Transform`, `Complete` | has **not** changed: nothing commits, the machine stays in the source | the exception propagates from `FireAsync` |
| `Exited`, `Entered`, `Completed` | **has** changed | the transition's remaining actions are skipped, the states left are still cleared, queued events are kept and run before the next trigger, and the exception propagates |
| `Decide`, `DecideAsync` | has **not** changed; the decision is over | a [`DecisionFailed`](decisions.md#when-the-decision-throws) event carrying the exception fires from the active leaf |

One consequence to design around: a `Transform` that throws halfway has already made the edits before the throw
to states that stay. Those are **not** rolled back — rolling back would mean copying every staying state before
every transition. Check what you need before you mutate.

### A batch that throws

When a transition throws out of `FireAsync(ReadOnlyMemory<TValue>)`, the rest of that batch is not processed — as if
your loop had stopped at that value — and a decision the batch started is cancelled. Events already queued are
kept, and run before the next trigger.

`FireUntilBoundaryAsync` has the same exception behavior. A propagated exception wins over a requested boundary;
because the call did not complete, it does not return a consumed count.

## Choosing the recovery

Each phase has an optional hook: a generated `partial` method the application can implement. Implemented, it
receives the exception and the transition, and chooses what the machine does. Not implemented, it does not exist
— the compiler removes it — and the generator emits no `try`/`catch` for that phase at all.

```csharp
public sealed partial class SampleTelnet
{
    partial void OnCompletedException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution)
    {
        Console.Error.WriteLine($"{transition} failed after the state changed: {exception.Message}");
        resolution = ExceptionResolution.Continue;      // keep going with the remaining actions
    }
}
```

The hooks are `OnGuardException`, `OnTransformException`, `OnExitedException`, `OnEnteredException` and
`OnCompletedException`. `TransitionInfo<TValue>` names the transition, its source, target and active leaf, the
trigger, the phase, and — for `Exited` and `Entered` — which state's action threw. `resolution` arrives set to
`Rethrow`:

| `resolution` | in `Guard` | in `Transform` / `Complete` | in `Exited` / `Entered` / `Completed` |
|---|---|---|---|
| `Rethrow` (default) | nothing commits; propagate | nothing commits; propagate | skip the remaining actions; clear; propagate |
| `Skip` | treat the guard as `false` and try the next candidate | nothing commits; drop the trigger; return normally | skip the remaining actions; clear; return normally |
| `Continue` | as `Skip` | as `Skip` — a half-run transform must never commit | swallow it and run the remaining actions |

A hook can also queue recovery, whatever it resolves: `Enqueue(new Error())` runs an `Error` transition when the
current one finishes.

A failed decision has no hook of its own: its `DecisionFailed` event already carries the exception and the
decision's name, and ordinary transitions on that event are the recovery.

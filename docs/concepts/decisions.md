# Decisions

Sometimes outside code has to choose what happens next: an account lookup, an authentication check, a user
callback that accepts or refuses a character-set table. A **decision** is a transition whose outcome that code
chooses. The choosing may be async; the change it leads to never is.

## Declaring one

A decision is a static class marked `[Decision]`. Its `Decide` or `DecideAsync` returns a **union** of outcome
types. Each outcome gets a `Complete` overload — the transform for that outcome — which names its own target with
`[To]`.

```csharp
public readonly record struct Accept(string Account);
public readonly record struct Reject(byte[] Reply);
public union AuthOutcome(Accept, Reject);

[Decision(From = typeof(AuthRequested)), On(Se)]
public static class CheckAuth
{
    public static async ValueTask<AuthOutcome> DecideAsync(TelnetContext context, AuthRequested request, CancellationToken ct) =>
        await context.Accounts.VerifyAsync(request.Data, ct) ? new Accept(request.Name) : new Reject(DenyReply);

    [To(typeof(Authenticated))]
    public static void Complete(in AuthRequested from, ref Authenticated to, Accept outcome) => to.Account = outcome.Account;

    [To(typeof(Idle))]
    public static void Complete(in AuthRequested from, ref Idle to, Reject outcome) { }

    public static ValueTask CompletedAsync(TelnetContext context, Accept outcome) => context.OnAuthenticatedAsync(outcome);
    public static ValueTask CompletedAsync(TelnetContext context, Reject outcome) => context.SendAsync(outcome.Reply);
}
```

- **Phases keep the grammar**: `Guard`, `Decide` and `Complete` run before the state changes; `Completed` after.
- `Decide` returns the union synchronously; `DecideAsync` returns `ValueTask<TUnion>`. The suffix must match the
  return type ([`SALCH0207`](../reference/diagnostics.md#salch0207)); declaring both is
  [`SALCH0208`](../reference/diagnostics.md#salch0208).
- Every outcome needs exactly one `Complete` ([`SALCH0401`](../reference/diagnostics.md#salch0401)); a `Complete`
  for something that is not an outcome is [`SALCH0402`](../reference/diagnostics.md#salch0402).
- `Decide` may take the context — it is how the outside world gets a say — along with the value or event, states
  by value, and (async only) a `CancellationToken`.

Outcome unions use C# 15 unions. A library targeting frameworks below .NET 11 declares the union attribute and
interface itself, internally, as the language allows; the generator reads the cases from the union's
single-parameter constructors either way.

## Synchronous decisions

A `Decide` that returns the union directly runs inline: decide, then the chosen `Complete`, as one transition. Use
it when the outside world answers immediately — a cache, a configuration flag. When the choice depends only on
the machine's own data, [guards](triggers.md#guards) are simpler.

## What an async decision does

1. The trigger resolves to the decision. The machine moves into a **pending state** — a generated child of the
   decision's source — and `FireAsync` returns. `IsDeferring` is now `true`.
2. `DecideAsync` runs with **values**: copies of the states it takes, the context, and a `CancellationToken` tied to
   the pending state.
3. When it completes, its outcome arrives as a generated event. The outcome picks its `Complete`, which runs as an
   ordinary transition from the pending state — reset, transform, commit, actions — with `ref`s to the data **as it
   is then**.

The pending state is a child of the source, so the source stays active and its data intact while the decision
runs.

### Leaving the pending state cancels the decision

The pending state owns its in-flight work the way states own data. Its slot holds the decision's
`CancellationTokenSource`; leaving the state — a disconnect, a timeout, any transition out — clears the slot, and
clearing it cancels the decision. A result that arrives after the pending state was left is dropped: it cannot
apply to a state that is no longer active.

### When the decision throws

A `DecideAsync` that throws produces a generated `DecisionFailed` event on the pending state, carrying the
exception. Handle it like any event — a transition out of the source on `DecisionFailed` is the recovery. If
nothing handles it, the machine's [unhandled-trigger](triggers.md#unhandled-triggers) behaviour applies.

## Deferral and backpressure

While a decision is pending, the machine **defers**:

- **Values are not accepted.** `FireAsync(value)` throws `MachineDeferringException`. The batch overload stops
  instead: `FireAsync(ReadOnlyMemory<TValue>)` returns how many values it consumed, stopping at the one that
  started the decision.
- **Events are queued** until the pending state resolves — except those the decision lists in `Handle`, which are
  handled immediately:

  ```csharp
  [Decision(From = typeof(AuthRequested), Handle = new[] { typeof(Disconnect), typeof(Timeout) }), On(Se)]
  ```

  Those resolve from the pending state like any trigger; a transition out of it cancels the decision.

On a socket, this is backpressure for free. The read loop advances its `PipeReader` by the consumed count and
waits:

```csharp
while (true)
{
    var read = await reader.ReadAsync(ct);
    var buffer = read.Buffer;
    var consumed = 0L;
    foreach (var segment in buffer)
    {
        var used = await machine.FireAsync(segment);
        consumed += used;
        if (used < segment.Length) break;           // a decision started deferring
    }

    var position = buffer.GetPosition(consumed);
    if (machine.IsDeferring)
    {
        reader.AdvanceTo(position);                 // unread bytes stay unexamined, so the next read returns them at once
        await machine.WhenReady();
    }
    else
    {
        reader.AdvanceTo(position, buffer.End);
        if (read.IsCompleted) break;
    }
}
```

While the machine defers, the pipe's buffer holds the unread bytes; once it passes its pause threshold, the pipe
stops reading from the socket, which pushes back on the sender. Nothing is copied and nothing grows without
bound. Marking only the consumed bytes as examined matters: marking the whole buffer examined would make the next
`ReadAsync` wait for new data from the socket instead of returning the bytes already waiting.

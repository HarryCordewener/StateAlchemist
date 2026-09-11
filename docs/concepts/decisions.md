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
   decision's source. Your `FireAsync` keeps waiting.
2. `DecideAsync` runs with **values**: copies of the states it takes, the context, and a `CancellationToken` tied to
   the pending state.
3. When it completes, its outcome arrives as a generated event. The outcome picks its `Complete`, which runs as an
   ordinary transition from the pending state — reset, transform, commit, actions — with `ref`s to the data **as it
   is then**.
4. The machine carries on with the rest of your input, and your `FireAsync` completes when all of it has been
   processed.

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

You do not have to do anything about a pending decision. **`FireAsync` completes when your input has been
processed** — every value in the batch, including waiting for any decision one of them started. While you await
it, you are not reading more input, and that is the backpressure:

```csharp
while (true)
{
    var read = await reader.ReadAsync(ct);
    foreach (var segment in read.Buffer)
    {
        await machine.FireAsync(segment);        // waits through any decision
    }

    reader.AdvanceTo(read.Buffer.End);
    if (read.IsCompleted) break;
}
```

This is the ordinary `PipeReader` loop, with nothing added. While a decision is pending, the loop is waiting on
`FireAsync`; bytes that arrive meanwhile collect in the pipe; once the pipe passes its pause threshold it stops
reading the socket, which pushes back on the sender. Nothing is copied — the machine keeps its place in your
segment until the `ValueTask` completes — and nothing grows without bound.

### Interrupting a pending decision

Your read loop is waiting, but other code is not: a timer, or the transport noticing the connection has closed.
While a decision is pending, the machine accepts **events** from any caller, whatever its
[concurrency mode](concurrency.md#while-a-decision-is-pending).

- Events the decision lists in `Handle` are handled at once, from the pending state. A transition out of it
  cancels the decision:

  ```csharp
  [Decision(From = typeof(AuthRequested), Handle = new[] { typeof(Disconnect), typeof(Timeout) }), On(Se)]
  ```

- Other events wait until the decision resolves; their callers' `FireAsync` completes once they have run.

```csharp
// In whatever notices the connection closing — not the read loop, which is waiting:
await machine.FireAsync(new Disconnect());
```

If the machine is stopped while your `FireAsync` is waiting on a decision, the decision is cancelled, the rest of
your input is discarded, and your `FireAsync` throws `MachineNotRunningException`.

### Two rules

- **Values come from one stream.** A second caller firing values while your batch waits on a decision is misuse:
  a `Checked` machine throws `ConcurrentUseException`; a `Serialized` machine queues them behind your batch.
- **Inside the machine, use `Enqueue`.** An action or decision that awaited `FireAsync` on its own machine would be
  waiting for itself to finish. `Enqueue(new Error())` queues the event and returns at once. The machine catches
  the mistake where it can do so for free — during a `Checked` machine's actions, which run while it is busy, and
  in any running decision, which it marks — and throws `ConcurrentUseException` instead of hanging. In the actions
  of a `Serialized` or `Unchecked` machine it cannot, and the call would hang.

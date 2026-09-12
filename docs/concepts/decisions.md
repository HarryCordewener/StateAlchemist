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
single-parameter constructors either way, and the case a result holds from the union's `Value`.

## Synchronous decisions

A `Decide` that returns the union directly runs inline: decide, then the chosen `Complete`, as one transition. Use
it when the outside world answers immediately — a cache, a configuration flag. When the choice depends only on
the machine's own data, [guards](triggers.md#guards) are simpler.

## What an async decision does

1. The trigger resolves to the decision. The machine moves into a **pending state**, generated below the active
   leaf: nothing is exited to enter it. Your `FireAsync` keeps waiting.
2. `DecideAsync` runs with **values**: copies of the states it takes, the context, and a `CancellationToken` tied to
   the pending state.
3. When it completes, its outcome arrives as a generated event. The outcome picks its `Complete`, which runs as an
   ordinary transition from the pending state — reset, transform, commit, actions — with `ref`s to the data **as it
   is then**.
4. The machine carries on with the rest of your input, and your `FireAsync` completes when all of it has been
   processed.

Because the pending state sits below the active leaf, every state on the active path — the source included — stays
active, with its data intact, while the decision runs.

### Leaving the pending state cancels the decision

The pending state owns its in-flight work the way states own data. Its slot holds the decision's
`CancellationTokenSource`; leaving the state — a disconnect, a timeout, any transition out — clears the slot, and
clearing it cancels the decision. A result that arrives after the pending state was left is dropped: it cannot
apply to a state that is no longer active.

### When the decision throws

A `Decide` or `DecideAsync` that throws ends the decision: the pending state is left, and a `DecisionFailed` event
fires from the active leaf, carrying the decision's name and the exception. `DecisionFailed` is a runtime type, so a
module can name it: a transition on it from the source, or any ancestor, is the recovery, and a guard on
`Decision` tells two decisions apart. If nothing handles it, the machine's
[unhandled-trigger](triggers.md#unhandled-triggers) behaviour applies.

A `Complete` or `Completed` that throws is an ordinary [transform or action exception](exceptions.md): the
decision is already over.

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

This is the ordinary `PipeReader` loop, with nothing added. While a decision is pending the loop waits on
`FireAsync`, bytes that arrive collect in the pipe, and once the pipe passes its pause threshold it stops reading
the socket, which pushes back on the sender. The machine keeps its place in your segment until the `ValueTask`
completes, so nothing is copied and no buffer grows without a limit.

### Interrupting a pending decision

Your read loop is waiting, but other code is not: a timer, or the transport noticing the connection has closed.
While a decision is pending, the machine accepts **events** from any caller, whatever its
[concurrency mode](concurrency.md#while-a-decision-is-pending).

- Events the decision lists in `Handle` are handled at once, from the pending state. A transition out of it
  cancels the decision:

  ```csharp
  [Decision(From = typeof(AuthRequested), Handle = new[] { typeof(Disconnect), typeof(Timeout) }), On(Se)]
  ```

- Other events wait until the decision resolves. Then they run first, in the order they arrived, before the rest of
  your input; their callers' `FireAsync` completes once they have run.

```csharp
// In whatever notices the connection closing — not the read loop, which is waiting:
await machine.FireAsync(new Disconnect());
```

If the machine is stopped while your `FireAsync` is waiting on a decision, the decision is cancelled, the rest of
your input is discarded, and your `FireAsync` throws `MachineNotRunningException`.

### Rules

- **Values come from one stream.** A second caller firing values while your batch waits on a decision is misuse:
  a `Checked` machine throws `ConcurrentUseException`; a `Serialized` machine queues them behind your batch.
- **Inside the machine, use `Enqueue`.** An action or decision that awaited `FireAsync` on its own machine would be
  waiting for itself to finish. `Enqueue(new Error())` queues the event and returns at once. The machine catches
  the mistake where it can do so for free — during a `Checked` machine's actions, which run while it is busy; in
  any running decision; and in any transition while a decision is pending — and throws `ConcurrentUseException`
  instead of hanging. In the actions of a `Serialized` or `Unchecked` machine otherwise, it cannot, and the call
  would hang. An event a decision enqueues waits for that decision like any other, unless the decision handles it;
  once the decision is no longer pending, what it enqueues is dropped.
- **One decision at a time.** An event handled while a decision is pending cannot start another decision: the
  machine throws `InvalidOperationException`.

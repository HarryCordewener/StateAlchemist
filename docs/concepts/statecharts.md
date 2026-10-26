# Statecharts

Final states, transitions that fire on their own, deferred and internal events, and machines that run other
machines. The examples are from an order in the [AOT sample](../../samples/StateAlchemist.AotSample/Order.cs),
which publishes native with no warnings:

```
OrderRoot ─┬─ Cart [Initial]
           ├─ Fulfilling [Defer(Cancel)]   invokes AotPayment and AotPacking, forwarding to both
           ├─ Review                         eventless to Shipped once approved
           ├─ Shipped
           ├─ Delivered [Final]
           └─ Returned [Final]
```

<!-- snippet: statechart-states -->
<a id='snippet-statechart-states'></a>
```cs
[Defer(typeof(Cancel))]
public struct Fulfilling : IState<OrderRoot>
{
}

public struct Review : IState<OrderRoot>
{
}

public struct Shipped : IState<OrderRoot>
{
}

[Final]
public struct Delivered : IState<OrderRoot>
{
}

[Final]
public struct Returned : IState<OrderRoot>
{
}

public struct Cancel : IEvent
{
}

/// <summary>Raised from inside the order only.</summary>
[Internal]
public struct Audit : IEvent
{
}

/// <summary>Known to the packing machine alone: the order forwards it.</summary>
public struct Packed : IEvent
{
}
```
<sup><a href='/samples/StateAlchemist.AotSample/Order.cs#L35-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-statechart-states' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Final states and completion

`[Final]` marks a leaf where its parent's work is done. A final state has no transitions of its own (`SALCH0005`).

- Entering a final child of the **root** completes the machine. `Status` becomes `MachineStatus.Completed`, its
  timers stop, and `FireAsync` throws `MachineNotRunningException` with that status. Inputs already waiting in an
  inbox are dropped without running. `StopAsync` still runs the `[Exited]` actions from the leaf to the root.
- Entering a final child of any **other** state makes that state done: its `[OnDone]` transitions are tried, as
  any trigger's are.

`IStatechart<TValue>.Completion` is a `Task` that completes when the machine completes or stops; `Status` says
which. It never faults. A machine restored from a snapshot taken in a final child of the root is completed as soon
as it starts.

## Eventless transitions

`[Always]` in place of a trigger makes a transition fire as soon as its source is active and its guard passes. It
is tried at the end of every transition into or within its source, and after `StartAsync`, before the call that
caused it returns, so the next trigger always finds the machine settled.

<!-- snippet: statechart-always -->
<a id='snippet-statechart-always'></a>
```cs
[Transition(From = typeof(Review), To = typeof(Shipped)), Always]
public static class Approve
{
    public static bool Guard(OrderContext context) => context.Approved;
}
```
<sup><a href='/samples/StateAlchemist.AotSample/Order.cs#L113-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-statechart-always' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Here a `Review` with `Approved` set moves straight on to `Shipped`; one without waits, and the eventless transition
is tried again after each transition that leaves the machine in `Review`, including a stay.

- An eventless transition must move: one that stays, and one that re-enters its own state without a guard, are
  `SALCH0110`, and so are unguarded eventless transitions that form a loop.
- Guarded transitions can still loop at run time. The chain stops with an `InvalidOperationException` after 64
  transitions in a row.
- An eventless transition takes no value or event (`SALCH0204`).

## Done transitions

`[OnDone]` fires a transition from a state when it is done: when one of its `[Final]` children is entered, or when
every machine it [invokes](#invoked-machines-and-parallel-regions) has completed. A done transition on a state that
has neither is `SALCH0111`. Like an eventless transition, it takes no value or event.

## Deferred events

`[Defer(typeof(E))]` on a state keeps an `E` that arrives while the state, or a state below it, is active, unless a
transition from a nearer state handles it. The kept events are not lost and not handled: when a transition next
moves the machine, at its commit, they are queued again in arrival order, ahead of anything the transition's
actions enqueue, and resolved in the new state. A state the machine moves to may defer them again.

In the order, a `Cancel` that arrives while it is being fulfilled waits until fulfilment is done, and then returns
the order from `Shipped`.

- A state that both defers and handles an event is `SALCH0112`; so is `[Defer]` naming a type that is not an
  event.
- The caller's `FireAsync` completes when the event is kept, not when it later runs.
- `Plan` reports a deferred event with `TransitionPlan.IsDeferred`.
- A snapshot holds the kept events, and the restored machine keeps them.
- `StopAsync` drops them.

## Internal events

`[Internal]` on an event type means only the machine raises it, with `Enqueue` from an action, a hook or a
decision. The generator writes no `FireAsync(in E)` or `Fire(in E)` for it, `FireAsync<TEvent>` returns a faulted
task for it, and calling `FireAsync` with one is `SALCH0602`. `PermittedTriggers` does not list it.

<!-- snippet: statechart-internal -->
<a id='snippet-statechart-internal'></a>
```cs
[Transition(From = typeof(Cart), To = typeof(Fulfilling)), On(2)]
public static class Checkout
{
    public static bool Guard(in Cart cart) => cart.Items > 0;

    public static void Completed(OrderContext context) => context.Machine!.Enqueue(new Audit());
}

[Transition(From = typeof(OrderRoot)), OnEvent(typeof(Audit))]
public static void Audited(OrderContext context) => context.Log.Add("audited");
```
<sup><a href='/samples/StateAlchemist.AotSample/Order.cs#L86-L97' title='Snippet source file'>snippet source</a> | <a href='#snippet-statechart-internal' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Invoked machines and parallel regions

`[Invoke(typeof(S))]` on a module method runs the machine the method returns while `S` is active. The method is
`public static`, returns a type that implements `IStatechart<TValue>` (a `[Machine]` class of the same project
does), and may take active states by `in`, the configuration, the context and a `CancellationToken`, the parent's
lifetime token (`SALCH0113`).

<!-- snippet: statechart-invoke -->
<a id='snippet-statechart-invoke'></a>
```cs
[Invoke(typeof(Fulfilling), Forward = true)]
public static AotPayment Pay(OrderContext context) => new(context);

[Invoke(typeof(Fulfilling), Forward = true)]
public static AotPacking Pack(OrderContext context) => new(context);

[Transition(From = typeof(Fulfilling), To = typeof(Review)), OnDone]
public static class Fulfilled
{
    public static void Completed(OrderContext context) => context.Log.Add("fulfilled");
}
```
<sup><a href='/samples/StateAlchemist.AotSample/Order.cs#L99-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-statechart-invoke' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- **Starting.** A transition that enters `S` calls the method and starts the child after the transition's actions,
  before `OnTransitioned`. So does a start whose path goes through `S`.
- **Stopping.** A transition that exits `S` stops the child at its commit, before the `[Exited]` actions. Stopping
  the parent stops every child first; pausing the parent pauses its running children, and resuming resumes them.
- **Completing.** A child that completes reaches its parent through the parent's inbox, as a timer's firing does.
  Once every child of `S` has completed, `S` is done and its `[OnDone]` transitions fire. A child that is stopped
  without completing does not make its state done. What a done transition fired this way throws has no caller to
  reach.
- **Forwarding.** With `Forward = true`, a trigger the parent does not handle goes to each running forwarding child,
  in declaration order, instead of to the parent's unhandled-trigger handling. With no such child running, the
  parent handles it as unhandled. Events always go, including event
  types the parent has no transition for; values go to children whose value type is the parent's. `SALCH0501` does
  not report a leaf under such a state, since what it leaves unhandled goes to the child.

Several `[Invoke]` methods on one state run their machines side by side. That is how StateAlchemist runs parallel
regions: each region is a machine of its own, with its own active leaf, so a machine still rests in exactly one leaf
([D21](../superpowers/specs/2026-09-11-statealchemist-design.md#4-decision-log)). The cost is that regions do not
share states or data with the parent, and talk to it only through completion and forwarding.

A machine that invokes others always has an inbox. `TakeSnapshot` throws while a child runs, and `Restore` refuses
a snapshot whose leaf is under an invoking state: the parent cannot record its children.

<!-- snippet: statechart-run -->
<a id='snippet-statechart-run'></a>
```cs
var context = new OrderContext();
await using var order = new AotOrder(context);
context.Machine = order;
var observer = new CountingObserver();
order.Observer = observer;
await order.StartAsync();

await order.FireAsync(1);
await order.FireAsync(1);
Console.WriteLine("permitted in Cart: " + string.Join(", ", order.PermittedTriggers()));
await order.FireAsync(2);          // Fulfilling: AotPayment and AotPacking start
await order.FireAsync(new Cancel()); // deferred until fulfilment is done
order.Pause();
order.Resume();
await order.FireAsync(10);         // forwarded to both: the payment completes
await order.FireAsync(new Packed()); // unknown to the order, forwarded: the packing completes
await order.Completion.WaitAsync(TimeSpan.FromSeconds(5));
```
<sup><a href='/samples/StateAlchemist.AotSample/Program.cs#L44-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-statechart-run' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Observing and steering a running machine

`IStatechart<TValue>`, which every generated machine and the reference interpreter implement, adds:

| Member | What it does |
|---|---|
| `IReadOnlyList<TriggerDefinition> PermittedTriggers()` | The values (joined into ranges) and events that would fire a transition now, evaluating guards without their exception hooks. Timers, eventless and done transitions, and internal events are not listed. |
| `ValueTask StartAsync<TState>()` | Starts the machine in `TState`'s `[Initial]` leaf, running the `[Entered]` actions from the root down. For tests. |
| `Task Completion` | Completes when the machine completes or stops. |
| `IMachineObserver<TValue>? Observer` | Told of every transition and every unhandled trigger, without a generated hook. |
| `void Pause()`, `void Resume()` | Pausing refuses triggers with `MachineNotRunningException(Paused)` and holds the timers, each keeping the time it had left; inputs already in an inbox wait. A machine without an inbox refuses `Pause` from inside itself. |

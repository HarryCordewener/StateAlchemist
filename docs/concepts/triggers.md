# Triggers

A trigger is what makes a transition fire. There are two kinds, dispatched differently.

| | Values | Events |
|---|---|---|
| What | the machine's value type: a byte, a `char`, a small enum | a struct implementing `IEvent`, with its own payload |
| Dispatched by | a `switch` on the value | the event's type, known at compile time |
| Declared with | `[On(value)]`, `[OnRange(from, to)]`, `[OnAny]` | `[OnEvent(typeof(E))]` |
| Fired with | `FireAsync(value)`, or a batch `FireAsync(ReadOnlyMemory<TValue>)` | a generated `FireAsync(in E)` per event type |
| Typical use | a byte stream | `Error`, `Timeout`, `Disconnect`, decision results |

A machine has one value type, of 16 bits or fewer ([`SALCH0105`](../reference/diagnostics.md#salch0105)): the
generator emits a dense `switch` over it, and [runs](runs.md) scan it with vector instructions.

## Declaring triggers

```csharp
[Transition(From = typeof(Idle)), On(LineFeed)]              // one value
public static void EndOfLine(ref Idle self) => self.LineLength = 0;

[Transition(From = typeof(Idle)), OnRange(0x20, 0x7E)]       // a range, inclusive
public static void Printable(ref Idle self, byte value) => self.LineLength++;

[Transition(From = typeof(Idle)), OnAny]                     // anything else
public static void Text(ref Idle self, byte value) => self.LineLength++;

[Transition(From = typeof(Connected), To = typeof(Idle)), OnEvent(typeof(Error))]
public static void Recover(in Error error) { }
```

`[On]` and `[OnRange]` may repeat on one transition — `[On(1), On(2)]` fires on either. A transition fires on
values or on one event type, never both ([`SALCH0106`](../reference/diagnostics.md#salch0106)). A value must fit
the value type ([`SALCH0104`](../reference/diagnostics.md#salch0104)).

## Which transition wins

From the active leaf up to the root, the first match wins, and **at each level** the order is:

1. transitions on the exact value;
2. transitions on a range containing it;
3. the `[OnAny]` transition.

Only when a level has nothing does resolution move to the parent. Two things follow:

- **`[OnAny]` is a state's "or else".** It shadows everything its ancestors do with values. `Willing`'s `[OnAny]`
  refuses every option no module accepts by exact value.
- **`[OnAny]` never matches an event.** An `Error` transition declared on the root still reaches `Willing`.

Events resolve the same way without the categories: the exact event type at each level, leaf first.

### Guards

A class-form transition with a `Guard` fires only when the guard returns `true`. Several transitions may share a
state and a trigger if at most one is unguarded. Guarded ones are tried in their `Order` (lowest first), the
unguarded one last:

```csharp
[Transition(From = typeof(Idle), To = typeof(Command), Order = 1), On(Iac)]
public static class CommandWhenNegotiating
{
    public static bool Guard(TelnetContext context) => context.Negotiating;
}

[Transition(From = typeof(Idle)), On(Iac)]                    // unguarded: the fallback
public static void LiteralIac(ref Idle self) => self.LineLength++;
```

If every guard at a level fails, resolution continues with the next category, then the parent. Two unguarded
transitions for the same state and trigger are [`SALCH0101`](../reference/diagnostics.md#salch0101) — including
two *modules* claiming the same option, which the generator sees because it compiles the whole machine at once.
Two guarded ones with the same `Order` are [`SALCH0102`](../reference/diagnostics.md#salch0102).

## Unhandled triggers

A trigger that matches nothing at any level calls the machine's `OnUnhandled` hook, if the application
implements it, and is then ignored. `[Machine(Unhandled = Unhandled.Throw)]` throws `UnhandledTriggerException`
instead. Warning [`SALCH0501`](../reference/diagnostics.md#salch0501) names every leaf that leaves some values
unhandled with no `[OnAny]` on its path.

```csharp
public sealed partial class SampleTelnet
{
    partial void OnUnhandled(StateId state, byte value) => Enqueue(new Error());
}
```

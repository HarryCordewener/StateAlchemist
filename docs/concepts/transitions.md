# Transitions

A transition owns the transformation of one state into the next. It is declared on its own, not inside a state, so
a module can add a transition out of a state it did not write.

## Two forms

**A method**, when the transition only changes data. The method *is* the transition's `Transform`:

<!-- snippet: sample-move -->
<a id='snippet-sample-move'></a>
```cs
[Transition(From = typeof(Idle), To = typeof(Command)), On(Iac)]
public static void BeginCommand(in Idle from)
{
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/TelnetCore.cs#L20-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-move' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**A static class**, when it has more than one part. Its methods are named for when they run:

<!-- snippet: sample-guard -->
<a id='snippet-sample-guard'></a>
```cs
[Transition(From = typeof(NawsEscaping), To = typeof(Idle)), On(Se)]
public static class Finish
{
    public static bool Guard(in NawsEscaping from) => from.Captured.Index == 4;

    public static void Transform(in NawsEscaping from, ref Connected root)
    {
        var bytes = from.Captured.Bytes!;
        root.Width = (bytes[0] << 8) | bytes[1];
        root.Height = (bytes[2] << 8) | bytes[3];
    }

    public static void Completed(TelnetContext context, Connected root) => context.Log.Add($"window {root.Width}x{root.Height}");
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/NawsModule.cs#L30-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-guard' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

| Method | Runs | Shape |
|---|---|---|
| `Guard` | before anything changes: may this transition fire? | `static bool`, read-only |
| `Transform` | before the state changes: turn the source into the target | `static void`, synchronous |
| `Completed` / `CompletedAsync` | after the state has changed, after every state's `[Exited]` and `[Entered]` actions | `void` / `ValueTask` |

The grammar is the rule: **imperative names run before the state changes; past-tense names run after it**. A
name ending in `Async` returns `ValueTask`; without it, the method is synchronous. `Guard`, `Transform` and
`Complete` have no `Async` form — they run before the state changes, and C# already refuses `ref` parameters on
async methods. Every method is optional. A method in the class that is not a phase name is
[`SALCH0206`](../reference/diagnostics.md#salch0206).

## Three kinds

| `To` | Kind | What happens to data |
|---|---|---|
| omitted | **stay** | nothing exits or enters; the source's data persists |
| another state | **move** | exits up to the lowest common ancestor, enters down to the target |
| the source itself | **re-entry** | exits and re-enters the source, clearing its data |

A stay is how a state consumes input without leaving:

<!-- snippet: sample-capture -->
<a id='snippet-sample-capture'></a>
```cs
[Transition(From = typeof(Naws)), OnAny]
public static void Capture(ref Naws self, byte value)
{
    self.Bytes ??= new byte[4];
    if (self.Index < 4)
    {
        self.Bytes[self.Index] = value;
        self.Index++;
    }
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/NawsModule.cs#L15-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-capture' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A re-entry clears data, so re-entering a state that has data is warning
[`SALCH0301`](../reference/diagnostics.md#salch0301): if you meant "keep going", you meant a stay. The root is
the exception: it is never exited, so re-entering it keeps its data and restarts everything below it.

### Moving to a state you are already in

A move whose target is on the active path — the active leaf, or one of its ancestors — **starts that state over**:
the target is exited and entered again, with fresh data. A move to the root is the one exception: the root is
never exited, so everything below it is, and the root's initial path is entered.

## What a transition may touch

A transition names the states it reads or writes as parameters, and the generator checks each against the tree.
It works out the **lowest common ancestor** (LCA) of the active leaf and the target, which gives every state one
role:

| Role | States | Take it as | Why |
|---|---|---|---|
| exiting | the active leaf up to, not including, the LCA | `in` | it is cleared when the transition ends |
| staying | the LCA and everything above it | `ref` (or `in`) | it survives, so edits persist |
| entering | below the LCA down to the target | `ref` | it starts reset; this is where the transition writes the new state |
| started over | exited and entered by the same move | `in` for the old data, `ref` for the new | both at once, in one signature |

Here the LCA of `AwaitingOption` and `Naws` is `SubNegotiation`, so the transition can record the option in the
parent that outlives both:

<!-- snippet: sample-roles -->
<a id='snippet-sample-roles'></a>
```cs
[Transition(From = typeof(AwaitingOption), To = typeof(Naws)), On(NawsOption)]
public static void Begin(in AwaitingOption from, ref SubNegotiation parent, ref Naws to) => parent.Option = NawsOption;
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/NawsModule.cs#L10-L13' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-roles' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Change `To` to `Idle` and `SubNegotiation` becomes exiting: `ref SubNegotiation` is then
[`SALCH0201`](../reference/diagnostics.md#salch0201), because an edit to a state about to be cleared would be lost
without a trace. Read it with `in` and write what matters into a state that survives. Naming a state the
transition does not touch at all — a sibling, an unrelated branch — is
[`SALCH0202`](../reference/diagnostics.md#salch0202).

### Transitions declared on a parent

A transition declared on a parent applies to every leaf beneath it; the generator plans it separately for each,
so states the leaf and target share are never exited. A parameter, though, is declared once for all those leaves,
so it may name a state only if it **binds the same way from every leaf**: `in` must read the state as it was, and
`ref` must write the state as it will be. `ref Naws` on a transition from `SubNegotiation` to `Naws` is fine —
from `AwaitingOption`, `Naws` is entering; from `Naws`, it is started over; either way `ref` writes the fresh
`Naws`. `in AwaitingOption` on the same transition is not: from `AwaitingOption` it reads the state being left,
from `Naws` there is nothing to read.

### Everything else a transition can take

| Parameter | Meaning |
|---|---|
| the value type (`byte value`) | the value that fired a value transition |
| `in TEvent e` | the event that fired an event transition |
| `ReadOnlySpan<TValue> run` | a [run](runs.md) transition's run |
| `in TConfig config` | the machine's immutable configuration |
| the context | the implementer's object — allowed unless the machine is [`Purity.Strict`](machines.md#options) |

Anything else is [`SALCH0204`](../reference/diagnostics.md#salch0204).

## Naming pitfall

Inside a module, a transition method named like a constant it uses — a method `Will` with `[On(Will)]` — makes
the attribute refer to the method, and the module fails to compile. Name methods for what they do:
`BeginWilling`, not `Will`.

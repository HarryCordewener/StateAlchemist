# States

A state is a `public struct`. Its fields are its data. Its parent is named by a marker interface, so the states of
a machine form a tree the compiler can read.

<!-- snippet: sample-states -->
<a id='snippet-sample-states'></a>
```cs
/// <summary>The root: lives as long as the connection.</summary>
public struct Connected : IRootState
{
    public bool GmcpEnabled;
    public int Width;
    public int Height;
}

/// <summary>Reading ordinary text. Where the machine starts.</summary>
[Initial]
public struct Idle : IState<Connected>
{
    public int LineLength;
}

/// <summary>After IAC: a command follows.</summary>
public struct Command : IState<Connected>
{
}

[Initial]
public struct AwaitingVerb : IState<Command>
{
}

/// <summary>After IAC WILL: the option follows.</summary>
public struct Willing : IState<Command>
{
}

/// <summary>After IAC SB: a subnegotiation follows.</summary>
public struct SubNegotiation : IState<Connected>
{
    public byte Option;
}

[Initial]
public struct AwaitingOption : IState<SubNegotiation>
{
}

/// <summary>Collecting NAWS's four bytes.</summary>
public struct Naws : IState<SubNegotiation>
{
    public byte[]? Bytes;
    public int Index;

    /// <summary>Rewinds but keeps the buffer, so entering NAWS again allocates nothing.</summary>
    public void Reset() => Index = 0;
}

/// <summary>After IAC inside NAWS: SE ends it.</summary>
public struct NawsEscaping : IState<SubNegotiation>
{
    public Naws Captured;
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/States.cs#L3-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-states' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## The tree

- **The root** implements `IRootState`. A machine has exactly one, named by `[Machine(Root = ...)]`.
- **Every other state** implements `IState<TParent>` for exactly one parent. Implementing two parent markers, or
  none, is [`SALCH0001`](../reference/diagnostics.md#salch0001).
- **`[Initial]`** marks the child a parent enters first. Every state with children in a machine marks exactly one
  child `[Initial]` ([`SALCH0004`](../reference/diagnostics.md#salch0004)).
- **The machine rests only in leaves.** Entering a state that has children continues down its `[Initial]` children:
  entering `SubNegotiation` lands in `AwaitingOption`. A parent is *active* whenever one of its descendants is.

Which states belong to a machine is decided by the machine, not the state: a state is in the machine if it is the
root, if an included module's transition names it, or if it is an ancestor of one that is. A state whose children
are all left out of a machine is a leaf in that machine.

## Going back with history

Entering a parent normally continues down its `[Initial]` children. A move can instead ask for the parent's
**history**: what was active under it when it was last exited. A car radio that comes back on the band it was
playing:

<!-- snippet: sample-radio-states -->
<a id='snippet-sample-radio-states'></a>
```cs
/// <summary>The radio.</summary>
public struct Radio : IRootState
{
}

/// <summary>Switched off. Where the machine starts.</summary>
[Initial]
public struct Off : IState<Radio>
{
}

/// <summary>Switched on, playing one band or the other.</summary>
public struct Playing : IState<Radio>
{
}

/// <summary>FM, the band a fresh start plays.</summary>
[Initial]
public struct Fm : IState<Playing>
{
}

/// <summary>AM.</summary>
public struct Am : IState<Playing>
{
}
```
<sup><a href='/samples/StateAlchemist.Samples/Radio/CarRadio.cs#L18-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-radio-states' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample-radio-module -->
<a id='snippet-sample-radio-module'></a>
```cs
/// <summary>The transitions.</summary>
[Module]
public static class RadioModule
{
    /// <summary>Switching on enters the band that was playing when the radio was switched off.</summary>
    [Transition(From = typeof(Off), To = typeof(Playing), History = History.Deep), On(Knob.Power)]
    public static void SwitchOn()
    {
    }

    [Transition(From = typeof(Playing), To = typeof(Off)), On(Knob.Power)]
    public static void SwitchOff()
    {
    }

    [Transition(From = typeof(Fm), To = typeof(Am)), On(Knob.Band)]
    public static void ToAm()
    {
    }

    [Transition(From = typeof(Am), To = typeof(Fm)), On(Knob.Band)]
    public static void ToFm()
    {
    }

    /// <summary>Without <c>History</c>, a move into <see cref="Playing"/> enters its <c>[Initial]</c> path.</summary>
    [Transition(From = typeof(Radio), To = typeof(Playing)), On(Knob.Reset)]
    public static void Reset()
    {
    }

    /// <summary>A control that does not apply where the radio is.</summary>
    [Transition(From = typeof(Radio)), OnAny]
    public static void Ignore()
    {
    }
}
```
<sup><a href='/samples/StateAlchemist.Samples/Radio/CarRadio.cs#L47-L85' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-radio-module' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample-radio-run -->
<a id='snippet-sample-radio-run'></a>
```cs
await using var radio = new CarRadio();
await radio.StartAsync();

await radio.FireAsync(Knob.Power);   // on: FM, Playing's [Initial] child
await radio.FireAsync(Knob.Band);    // AM
await radio.FireAsync(Knob.Power);   // off
await radio.FireAsync(Knob.Power);   // on again, by history: AM
// radio.State == CarRadio.StateId.Am
```
<sup><a href='/tests/StateAlchemist.Generated.Tests/DocumentationExampleTests.cs#L99-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-radio-run' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- **`History.Deep`** enters the leaf that was active. **`History.Shallow`** enters the child that was active, then
  that child's `[Initial]` path.
- **Before the parent has been exited once**, there is nothing to recall: the move enters the `[Initial]` path.
- **A move without `History`** enters the `[Initial]` path, whatever was active before. `Reset` above does this.
- **History restores which state is active, not its data.** Every state the move enters starts reset, as on any
  entry. Data that should survive belongs in a state above, as [below](#how-long-data-lives).
- **A move to a parent on the active path** exits it, which records the leaf the machine is in, so it comes back to
  that same leaf with fresh data.
- A decision outcome asks for history the same way: `[To(typeof(Playing), History = History.Deep)]`.
- History needs something to recall: on a stay, on a target with no children, or on the root it is
  [`SALCH0210`](../reference/diagnostics.md#salch0210).
- A move by history can write its target and the states above it, but not the states below the target: which of
  them it enters is known only when it runs ([`SALCH0202`](../reference/diagnostics.md#salch0202)).

The machine keeps one `StateId` per parent that some move enters by history, written when a move exits that parent.
Recalling it is a `switch`; nothing allocates. `Plan` names the leaf the move would enter now, and the
[diagrams](../guides/examples.md) draw the move as an arrow to an `H` (shallow) or `H*` (deep) node inside the parent.

## How long data lives

Entering a state **resets** its data; leaving it **clears** it. So *where data sits in the tree is how long it
lives*:

| Data in | Lives until |
|---|---|
| the root, `Connected` | the machine stops |
| `SubNegotiation` | the machine leaves the subnegotiation, whichever child it was in |
| `Naws` | the machine leaves `Naws` |

If data has to outlive a state, it belongs in an ancestor that both sides of the transitions share. A value two
sibling states both need — the option a subnegotiation is for — goes in their parent.

### Clearing without reallocating

Clearing assigns `default` — unless the state declares `public void Reset()`, which is called instead. `Naws`
uses it to keep its buffer:

<!-- snippet: sample-naws-state -->
<a id='snippet-sample-naws-state'></a>
```cs
/// <summary>Collecting NAWS's four bytes.</summary>
public struct Naws : IState<SubNegotiation>
{
    public byte[]? Bytes;
    public int Index;

    /// <summary>Rewinds but keeps the buffer, so entering NAWS again allocates nothing.</summary>
    public void Reset() => Index = 0;
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/States.cs#L46-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-naws-state' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The first transition into `Naws` allocates the array (`to.Bytes ??= new byte[4]`); every later one reuses it. A
machine instance has one storage slot per state for its whole life, so entering a state never allocates on its
own.

## Where state data can be read

- **Transitions** read and write states according to their role in the transition — see
  [what a transition may touch](transitions.md#what-a-transition-may-touch).
- **Actions** read states by value after the state has changed — see [actions](actions.md).
- **Outside code** reads a copy of an active state's data with the generated `TryGet{State}(out {State})`, or
  `IMachine<TValue>.TryGetState<TState>(out TState)`.

## Events are not states

Most triggers are *values* — bytes, characters, small enums. A trigger with its own typed payload is an *event*:
a struct implementing `IEvent`. See [triggers](triggers.md).

<!-- snippet: sample-event -->
<a id='snippet-sample-event'></a>
```cs
/// <summary>Something went wrong; recover to <see cref="Idle"/>.</summary>
public readonly struct Error : IEvent
{
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/Events.cs#L3-L8' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

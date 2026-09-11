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
<sup><a href='/samples/StateAlchemist.Samples/Telnet/States.cs#L3-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-states' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/samples/StateAlchemist.Samples/Telnet/States.cs#L45-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-naws-state' title='Start of snippet'>anchor</a></sup>
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

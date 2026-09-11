# StateAlchemist

> *"To obtain, something of equal value must be lost."*
> — Alphonse Elric, *Fullmetal Alchemist*

A source-generated hierarchical state machine library for .NET. Your states own their data, your transitions
own the transformation, and the compiler writes the machine.

> **Status: design.** Nothing is on NuGet yet, and the API below is the planned shape, not a shipped one.
> The design spec is in review:
> [`2026-09-11-statealchemist-design.md`](https://github.com/HarryCordewener/TelnetNegotiationCore/blob/claude/statealchemist-design/docs/superpowers/specs/2026-09-11-statealchemist-design.md).

## Why

State machine libraries such as [Stateless](https://github.com/dotnet-state-machine/stateless) interpret your
configuration at runtime: every trigger is looked up in dictionaries, dispatched through delegates, and allocates
along the way. That's fine for a workflow that fires a few times a minute. It isn't fine for a protocol parser
on a TCP connection, where every byte is a trigger.

Measured on .NET 11 RC1 against the shape of [TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore)'s
state machine, per byte fired:

| | Stateless 5.20 | StateAlchemist (target) |
|---|---|---|
| Negotiation byte | 611 ns, 1,575 B | ≤ 25 ns, **0 B** |
| Subnegotiation payload byte | 282 ns, 1,193 B | a vectorised run: ≥ 1 GB/s, **0 B** |
| Building the machine, per connection | 4.4 ms, 2.2 MB | nothing: the definition is generated code |

StateAlchemist moves that work to compile time. A source generator reads your declarations and emits the
machine as straight-line code: a `switch` on the active state, then on the trigger, calling your
transformations directly.

## The idea

**Equivalent exchange.** A transition consumes one state to produce another. Entering a state resets its data;
leaving a state clears it. Nothing survives a transition except what the transition carries across, or what
lives in a state that isn't being left.

- **A state is a type, and it owns its data.** States are structs arranged in a tree. Where data sits in the
  tree is its lifetime: the root's data lives as long as the machine, and a leaf's data lives until you leave it.
- **A transition owns the transformation.** Transitions are declared on their own, not inside states, so a
  plugin can add a transition out of a state it didn't write. A transition's parameters say what it touches.
  The generator checks each one against the tree: a state being left can be read (`in`), a state that stays or
  is being entered can be written (`ref`), and anything else is a compile error.
- **External code is kept separate from state changes.** Transforms are synchronous and pure. Actions (network
  writes, callbacks, logging) run after the state commits, and may be async. One that completes synchronously
  allocates nothing.
- **Async decisions defer.** When outside code has to choose the outcome (an auth check, an API lookup), the
  machine parks in a generated pending state and defers further input until it has an answer. On a `Pipe`, the
  pipe's own buffer holds the deferred bytes, and the pipe pushes back on the sender. Leaving the pending state
  cancels the decision.

```csharp
// Planned API: subject to change during design review.
public struct SubNegotiation : IState<Connected> { public byte Option; }
[Initial] public struct AwaitingOption : IState<SubNegotiation> { }
public struct Naws : IState<SubNegotiation> { public byte[]? Bytes; public int Index; }

[Module] public static partial class NawsModule
{
    [Transition(From = typeof(AwaitingOption), To = typeof(Naws)), On(31)]
    public static void Begin(in AwaitingOption from, ref SubNegotiation parent, ref Naws to) => parent.Option = 31;

    [Transition(From = typeof(Naws)), OnAny]                       // stay in Naws, capture the byte
    public static void Capture(ref Naws self, byte value)
    { self.Bytes ??= new byte[4]; if (self.Index < 4) self.Bytes[self.Index++] = value; }
}

// In the application: pick the modules; the generator writes the machine.
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(NawsModule))]
public sealed partial class MudTelnet;
```

## Planned features

- Hierarchical states whose data is reset on entry and cleared on exit, with no allocation per entry.
- Two kinds of trigger: *values* (a byte, a `char`, a small enum), matched exactly, by range, or by `OnAny`,
  which catches whatever a state doesn't otherwise handle; and typed *events* with their own payload.
- *Run* transitions: a vectorised scan hands a whole stretch of input to one call, instead of one dispatch per value.
- Async actions and decisions with a synchronous fast path, run-to-completion semantics, and backpressure.
- Compile-time diagnostics for conflicts between plugins, role violations, and decision outcomes with no
  transition.
- A pure *plan* layer (what would this trigger do?) and the whole machine as data, with Mermaid and DOT diagrams
  generated as constants.
- Native AOT and trimming clean, with no reflection. Targets `netstandard2.0`, `net8.0`, `net10.0` and `net11.0`.

## Roadmap

| Milestone | Scope |
|---|---|
| M0 | Repository skeleton, generator plumbing, CI |
| M1 | Flat machines declared across assemblies, `switch` dispatch |
| M2 | Hierarchy, data lifetimes, roles, guards |
| M3 | Actions, events, run-to-completion |
| M4 | Async decisions, deferral and backpressure |
| M5 | Run transitions and the performance targets |
| M6 | `1.0.0-preview.1` |

The first consumer is [TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore) 4.0.

## License

[Apache-2.0](LICENSE)

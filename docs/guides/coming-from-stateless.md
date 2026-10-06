# Coming from Stateless

StateAlchemist takes part of its model from [Stateless](https://github.com/dotnet-state-machine/stateless): the
library owns the machine's state, and actions run inside `Fire` and may be async. Both have hierarchical states,
entry and exit actions, guards, reentry and internal transitions, so most of what you write with Stateless has a
direct counterpart. This page lists them, then the places where the two work differently.

The [phone call example](examples.md#a-phone-call) is the one Stateless's README opens with, written for
StateAlchemist.

## Configuration

| Stateless | StateAlchemist |
|---|---|
| `Configure(state)` | a `public struct S : IState<Parent>`; see [states](../concepts/states.md) |
| `SubstateOf(parent)` | the `IState<Parent>` marker |
| `InitialTransition(child)` | `[Initial]` on the child |
| `Permit(trigger, target)` | `[Transition(From = typeof(S), To = typeof(T)), On(trigger)]` |
| `PermitIf(trigger, target, guard)` | a class-form transition with a `Guard`, and `Order` when several can match; see [guards](../concepts/triggers.md#guards) |
| `PermitIfAsync(trigger, target, guard)` | a [decision](../concepts/decisions.md) with `DecideAsync`; a `Guard` is synchronous |
| `PermitReentry(trigger)` | a re-entry: `To` names the source itself; see [three kinds](../concepts/transitions.md#three-kinds) |
| `PermitReentryIf(trigger, guard)` | a re-entry with a `Guard` |
| `InternalTransition(trigger, action)` | a stay: omit `To` |
| `Ignore(trigger)` | a stay with an empty transform |
| `PermitDynamic(trigger, selector)` | a guard per outcome, or a [decision](../concepts/decisions.md) |
| `SetTriggerParameters<T>(trigger)` | an event: a struct implementing `IEvent`, carrying its payload; see [triggers](../concepts/triggers.md) |

## Actions

| Stateless | StateAlchemist |
|---|---|
| `OnEntry` / `OnExit` | `[Entered(typeof(S))]` / `[Exited(typeof(S))]` on a module method; see [actions](../concepts/actions.md) |
| `OnEntryAsync` / `OnExitAsync` | the same attributes on a method returning `ValueTask` |
| `OnEntryFrom(trigger, action)` | the transition's `Completed`, which can take the value or event that fired it |
| `OnTransitioned(...)` | `partial void OnTransitioned(in TransitionInfo<TValue> transition)` |
| `OnUnhandledTrigger(...)` | `partial void OnUnhandled(StateId state, TValue value)`, and one per event type |

## Running the machine

| Stateless | StateAlchemist |
|---|---|
| `new StateMachine<TState, TTrigger>(initial)` | the generated constructor, then `StartAsync()`; see [lifecycle](../concepts/lifecycle.md) |
| `FireAsync(trigger)` | `FireAsync(value)`, or `FireAsync(in e)` for an event |
| `Fire(trigger)` | `Fire(value)`, when nothing in the machine is async |
| `State` | `State`, a generated `StateId` enum |
| `IsInState(state)` | `IsIn(StateId.S)` |
| `PermittedTriggers` | `Plan(value)`: what one trigger would do, without doing it |
| `GetInfo()` | the static `Definition` |
| `UmlDotGraph.Format(...)` / `MermaidGraph.Format(...)` | the generated `Dot` and `Mermaid` constants |

The [generated API](../reference/generated-api.md) lists every member.

## What works differently

**The machine is declared, not configured.** Stateless builds the machine at runtime from configuration calls. In
StateAlchemist, states and transitions are declared in [modules](../concepts/machines.md), an application names the
modules its machine includes, and a source generator writes the machine when the application compiles. Conflicts
between transitions, such as two unguarded ones for the same state and trigger, are compile errors.

**A state is a type that owns its data.** A Stateless state is a value of `TState`, often an enum. A StateAlchemist
state is a struct whose fields are its data; entering a state resets them and leaving it clears them. A transition
says which states it reads and writes, and the generator checks that against the tree; see
[what a transition may touch](../concepts/transitions.md#what-a-transition-may-touch).

**A transform comes before the actions.** A transition's `Transform` is synchronous and changes data; actions run
after the state has changed. [The order of one transition](../concepts/actions.md#the-order-of-one-transition)
lists every step.

**Reentry and internal transitions.** Stateless's reentrant transition runs the state's exit and entry actions, as
its README describes. A StateAlchemist re-entry does the same and also resets the state's data, so re-entering a
state that has data is warning [`SALCH0301`](../reference/diagnostics.md#salch0301). To handle a trigger and keep
the data, use a stay, the equivalent of `InternalTransition`: nothing is exited or entered.

**Unhandled triggers are ignored by default.** Stateless throws unless a trigger is ignored or
`OnUnhandledTrigger` is set. StateAlchemist calls `OnUnhandled`, if implemented, and carries on;
`[Machine(Unhandled = Unhandled.Throw)]` throws instead. See [unhandled triggers](../concepts/triggers.md#unhandled-triggers).

**Triggers are values or events.** A machine has one value type of 16 bits or fewer, such as a `byte`, a `char` or
a small enum, plus any number of event types. A trigger that carries data is an event.

**Concurrency is chosen per machine.** Stateless's README notes that a `StateMachine` may be used asynchronously
but not concurrently by several threads. StateAlchemist offers three modes, `Checked`, `Unchecked` and
`Serialized`; the last lets any thread fire and runs each transition, including its awaited actions, before the
next. See [concurrency](../concepts/concurrency.md).

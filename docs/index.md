# StateAlchemist documentation

> *"To obtain, something of equal value must be lost."*
> — Alphonse Elric, *Fullmetal Alchemist*

StateAlchemist is a hierarchical state machine library for .NET where the machine is **compiled, not
interpreted**. Libraries declare states and transitions; an application picks the modules it wants; a source
generator writes the machine as straight-line code.

> **Status: released.** [1.0.0](https://www.nuget.org/packages/StateAlchemist) is on NuGet — `dotnet add package
> StateAlchemist` brings the runtime, the generator, the analyzers and the code fixes. Most code samples on these
> pages are included from the compiled samples project, so they cannot drift.

## Start here

- [Examples](guides/examples.md) — five machines, smallest first: a phone call, a crossing, telnet, a card
  reader, a pipe.
- [Getting started](guides/getting-started.md) — one telnet machine, end to end.

## Concepts

| Page | What it covers |
|---|---|
| [States](concepts/states.md) | State structs, the tree they form, how long their data lives |
| [Transitions](concepts/transitions.md) | Two forms, three kinds, and which states a transition may read or write |
| [Triggers](concepts/triggers.md) | Values and events; which transition wins; guards; unhandled triggers |
| [Actions](concepts/actions.md) | Code that runs after the state changes, and the exact order of a transition |
| [Decisions](concepts/decisions.md) | When outside code chooses the outcome; pending states, deferral, backpressure |
| [Runs](concepts/runs.md) | Consuming a whole stretch of input in one call |
| [Machines](concepts/machines.md) | Modules, the machine declaration, and its options |
| [Lifecycle](concepts/lifecycle.md) | Starting, stopping, disposing |
| [Concurrency](concepts/concurrency.md) | `Checked`, `Unchecked` and `Serialized`, and what each costs |
| [Exceptions](concepts/exceptions.md) | What happens when your code throws, and how to choose the recovery |

## Guides

- [Examples](guides/examples.md) — the five samples, what each one is for, and the code from all of them.
- [Testing a machine](guides/testing.md) — the pure layer, the machine interface, and the reference interpreter.
- [Writing a module](guides/writing-a-module.md) — adding a protocol to a machine you do not own.
- [Contributing](../CONTRIBUTING.md) — building the repository, where the tests live, and what a change has to do.

## Reference

- [Generated API](reference/generated-api.md) — every member the generator adds to a machine.
- [Diagnostics](reference/diagnostics.md) — every `SALCH` diagnostic, with its cause and fix.
- [Releasing](releasing.md) — for maintainers: how a version is decided and what the release workflow does.
- [Design specification](superpowers/specs/2026-09-11-statealchemist-design.md) — every decision and why.

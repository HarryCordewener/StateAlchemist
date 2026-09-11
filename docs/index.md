# StateAlchemist documentation

> *"To obtain, something of equal value must be lost."*
> — Alphonse Elric, *Fullmetal Alchemist*

StateAlchemist is a hierarchical state machine library for .NET where the machine is **compiled, not
interpreted**. Libraries declare states and transitions; an application picks the modules it wants; a source
generator writes the machine as straight-line code.

> **Status: documentation first.** These pages describe the library as designed and approved. The code is being
> built against them, in the order set out in the [roadmap](superpowers/plans/2026-09-11-00-roadmap.md). Code
> samples here show the intended API; once the samples project compiles in CI, they are included from it and can
> no longer drift.

## Start here

- [Getting started](guides/getting-started.md) — a small telnet machine, end to end.

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

- [Testing a machine](guides/testing.md) — the pure layer, the machine interface, and the reference interpreter.
- [Writing a module](guides/writing-a-module.md) — adding a protocol to a machine you do not own.

## Reference

- [Generated API](reference/generated-api.md) — every member the generator adds to a machine.
- [Diagnostics](reference/diagnostics.md) — every `SALCH` diagnostic, with its cause and fix.
- [Design specification](superpowers/specs/2026-09-11-statealchemist-design.md) — every decision and why.

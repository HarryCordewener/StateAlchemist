# Diagnostics

Every problem StateAlchemist can find before your code runs. **Declaring library** diagnostics are reported where
a module or state is written; **app** diagnostics where a machine is assembled, because they depend on which
modules it includes.

| Range | About |
|---|---|
| `SALCH00xx` | states and the tree |
| `SALCH01xx` | triggers, conflicts, the machine declaration |
| `SALCH02xx` | method shapes, parameters, roles |
| `SALCH03xx` | re-entry |
| `SALCH04xx` | decisions |
| `SALCH05xx` | coverage and reachability |
| `SALCH06xx` | generated API |
| `SALCH07xx` | runs |
| `SALCH08xx` | lifecycle |
| `SALCH09xx` | authoring assistance: hints whose code fixes write code for you |

## Where each diagnostic is reported

Two components report these, and which one depends on what the problem needs to know.

- **The analyzer** reports a module's own problems, in the project where the module is written: a state that is not
  a public struct, a member that is not public static, a transition with no trigger, a phase whose name or
  signature is wrong. It has to, because Roslyn cannot see a referenced assembly's non-public members — a library's
  mistakes would otherwise be invisible until an application assembled a machine, and then be reported in the wrong
  place. It also carries the `SALCH09xx` hints whose code fixes write a phase for you.
- **The generator** reports everything that depends on which modules a machine includes: conflicts, coverage,
  reachability, roles, and the machine declaration itself. Those answers change with the machine, so they belong to
  the application that chose it.
- **Two analyzers read your calls** rather than your declarations: `SALCH0801`, when a method constructs a machine
  and fires it without starting it, and `SALCH0601`, when a machine that can suspend is fired synchronously.

Both ship in the package, so referencing StateAlchemist is all it takes.

---

## SALCH0001

**Invalid state declaration** · error · declaring library

> State '{0}' must be a public struct with exactly one parent marker; {1}

A state is a `public struct` implementing `IRootState` or exactly one `IState<TParent>`. A class, a non-public
struct, a struct implementing two markers, or a type used as a state that implements neither, is refused.

**Fix:** make it `public struct` and give it exactly one parent marker.

## SALCH0002

**Member is not public static** · error · declaring library

> '{0}' must be public and static

The generated machine lives in the application and calls your transitions, decisions and actions directly, so
each must be `public static`, in a public type.

## SALCH0003

**Invalid state hierarchy** · error · app

> State '{0}' {1}

The states of a machine form one tree: one root, every other state's parent in the machine, no loops. Reported
for a second root, a parent missing from the machine, or a parent chain that loops.

## SALCH0004

**Initial child required** · error · app

> State '{0}' has {1} [Initial] children; it needs exactly one

The machine rests only in leaves, so entering a state with children must know which child to enter.

**Fix:** mark exactly one child `[Initial]`.

## SALCH0101

**Conflicting transitions** · error · app

> '{0}' and '{1}' both handle {2} in state '{3}' without a guard

Two unguarded transitions of the same kind — two exact values, two overlapping ranges, two `[OnAny]`, two of the
same event — from the same state. Often two modules claiming the same option.

**Fix:** remove one, or guard them and give them distinct `Order`s. An exact value and a range, or a range and
`[OnAny]`, never conflict: the more specific one wins.

## SALCH0102

**Ambiguous guard order** · error · app

> Guarded transitions '{0}' and '{1}' both handle {2} in state '{3}' with Order {4}

Guarded transitions for the same trigger are tried in `Order`; two with the same `Order` have no defined order.

**Fix:** give them distinct `Order` values.

## SALCH0103

**Ambiguous action order** · error · app

> '{0}' and '{1}' both run when '{2}' is {3}, from different modules, with Order {4}

`[Exited]` or `[Entered]` actions for the same state from different modules run in `Order`; within one module,
declaration order decides. Two from different modules with the same `Order` have no defined order.

## SALCH0104

**Trigger outside the value type** · error · app

> '{0}' fires on {1}, which is outside the value type '{2}'

`[On(300)]` on a `byte` machine can never fire.

## SALCH0105

**Unsupported value type** · error · app

> The value type '{0}' is not supported: use an integral type or an enum of 16 bits or fewer

The generator emits a dense `switch` over the value type and scans it with vector instructions; both need a
small integral type.

## SALCH0106

**Invalid transition declaration** · error · declaring library

> '{0}' {1}

A transition does not name its `From` state, has no trigger, mixes value and event triggers, has an empty range,
or names a trigger value that is not an integral constant.

## SALCH0107

**Incomplete machine declaration** · error · app

> Machine '{0}' {1}

A `[Machine]` without `Root` or `Value`, or an `[Include]` of a type that is not a `[Module]`.

## SALCH0201

**Writing to an exiting state** · error · app

> Parameter '{0}' of '{1}' takes '{2}' by ref, but the transition exits it; take it as in

A state the transition leaves is cleared when the transition ends, so an edit to it would vanish.

**Fix:** take it as `in`, and write anything that must survive into a state that stays or is entered — there is a
code fix that does the first half. See
[what a transition may touch](../concepts/transitions.md#what-a-transition-may-touch).

## SALCH0202

**State not available here** · error · app

> Parameter '{0}' of '{1}' names '{2}', which {3}

The parameter names a state the transition does not touch — a sibling, an unrelated branch — or, for a
transition declared on a parent, a state that binds differently depending on the active leaf. For a state action,
a state that is neither the action's state nor one of its ancestors.

## SALCH0203

**Invalid phase signature** · error · app

> '{0}' must {1}

`Guard` returns `bool`; `Transform` and `Complete` return `void`; `Completed` returns `void` and
`CompletedAsync` `ValueTask`; `DecideAsync` returns `ValueTask<TOutcome>`; a decision declares `Decide` or
`DecideAsync`.

## SALCH0204

**Unbindable parameter** · error · app

> Parameter '{0}' of '{1}' cannot be bound: {2}

A parameter that is not a state, the value, the event that fired, a run, the configuration, the context, a
decision outcome, a `CancellationToken` or the transition info — or one of those where its method cannot take it,
such as a `CancellationToken` on a transform, or a value on an event transition.

## SALCH0205

**Context under strict purity** · error · app

> '{0}' takes the context, which machine '{1}' forbids with Purity.Strict

See [purity](../concepts/machines.md#purity).

## SALCH0206

**Unknown phase method** · error · declaring library

> '{0}' is not a phase; a class-form transition may declare Guard, Transform, Decide, DecideAsync, Complete, Completed and CompletedAsync

In a class-form transition, a method's name says when it runs. A name that is not a phase would never run.

## SALCH0207

**Async suffix does not match** · error · declaring library

> '{0}' {1}

A phase returning a task is named with `Async`; one that does not is named without. `Guard`, `Transform` and
`Complete` run before the state changes and cannot be async. The code fix renames the method.

## SALCH0208

**Two decide methods** · error · declaring library

> Decision '{0}' declares both Decide and DecideAsync

## SALCH0209

**Unchecked machine with async actions** · warning · app

> Machine '{0}' is Unchecked but has async actions or decisions; await every FireAsync before calling the next

See [concurrency](../concepts/concurrency.md#unchecked).

## SALCH0301

**Re-entry clears data** · warning · app

> '{0}' re-enters '{1}', which has data; the data is cleared

`To` equal to `From` exits and re-enters the state. If you meant to keep going, omit `To`: that is a stay. Not
reported for the root: it is never exited, so its data survives.

## SALCH0401

**Decision outcome not completed exactly once** · error · app

> Decision '{0}' has {1} Complete methods for outcome '{2}'; it needs exactly one

Every case of a decision's outcome union needs exactly one `Complete` overload.

## SALCH0402

**Unknown decision outcome** · error · app

> '{0}' completes '{1}', which is not an outcome of decision '{2}'

## SALCH0501

**Unhandled values** · warning · app

> Leaf '{0}' leaves {1} values unhandled and no [OnAny] covers them

In that leaf, those values match no transition at any level and fall to the machine's
[unhandled](../concepts/triggers.md#unhandled-triggers) behaviour. Add an `[OnAny]` on the leaf or an ancestor if
that is not what you want.

## SALCH0502

**Unreachable state** · warning · app

> State '{0}' cannot be reached from the initial state

No sequence of transitions, assuming every guard passes, reaches it.

## SALCH0601

**Synchronous fire on an async machine** · error · app

> '{0}' has async actions or decisions: fire it with FireAsync and await that

Every machine has the synchronous `Fire`, because the generator writes one machine at a time and cannot know how a
caller means to use it. On a machine that can suspend the call would block the calling thread until the action came
back — a deadlock waiting for a synchronization context — so using it there is an error at the call, where the
choice is made.

## SALCH0701

**Invalid run transition** · error · app

> [Run] on '{0}' is invalid: {1}

A run is a stay on `[OnAny]` or a range, without a guard, whose transform takes
`(ref TState self, ReadOnlySpan<TValue> run)` and optionally the configuration and context. See
[runs](../concepts/runs.md).

## SALCH0801

**Fired before started** · warning · anywhere

> '{0}' is fired before StartAsync on some path

See [lifecycle](../concepts/lifecycle.md#why-starting-is-separate).

## SALCH0901

**Transition declares no phases** · info · declaring library

> Transition '{0}' declares no phases

A class-form transition with no methods only changes state. That is valid, but usually unfinished. The code fixes
add **Guard**, **Transform** and **Completed**, each with the parameters this transition's own states give it: the
source as `in` for a move, the state itself as `ref` for a stay, the target as `ref`. A decision's `Decide` and
`Complete` are not offered — their outcome type is a union you choose, and a fix cannot invent one. If the
transition really is state-only, write it as a method instead.

## SALCH0902

**Phase can be added** · hidden · declaring library

> Transition '{0}' can declare {1}

Never shown as a warning: it exists so the same **Add Guard** / **Add Transform** / **Add Completed** code fixes
are available on a transition that already declares some phases. In Rider, Visual Studio and VS Code they appear
where the IDE offers quick-fixes.

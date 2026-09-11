# StateAlchemist — a source-generated hierarchical state machine library

**Status:** design, approved 2026-09-11 · **Package:** `StateAlchemist`

**First consumer:** TelnetNegotiationCore (TNC) 4.0. The TNC migration gets its own spec once this library's
design is settled; section 12 only maps the concepts across.

---

## 1. Summary

StateAlchemist is a hierarchical state machine library for .NET where the machine is **compiled, not interpreted**.
Libraries *declare* states and transitions; the consuming application declares which of them make up its
machine, and a source generator emits that machine as straight-line code: a `switch` on the active state and
then on the trigger, calling the declared transformations directly.

Its model combines the two libraries it was measured against:

- From **Stateless**: the library owns the machine's state. Actions run inside `Fire`, and may be async.
- From **FunctionalStateMachine (FSM)**: transitions are pure transformations that can be planned, inspected and
  tested without running any side effects.
- New to both: **a state is a type that owns its data**, the hierarchy decides that data's lifetime, and
  **a transition owns the transformation** of one state into the next.

The goal is performance on a TCP pipe: no allocation per trigger on synchronous paths, no machine built per
connection, and whole runs of bytes consumed by a vectorised scan instead of one dispatch per byte.

## 2. Why

Measured on .NET 11 RC1, Release, against TNC's shape (see the investigation that led here):

| | Stateless 5.20 | FSM 1.4 |
|---|---|---|
| Negotiation byte (`IAC WILL GMCP`) | 611 ns, 1,575 B | 191–398 ns, 1,008–1,057 B |
| Subnegotiation payload byte | 282 ns, 1,193 B | 67–78 ns, 301–412 B |

- TNC 2.17 measured a Stateless fire at ~3.3 KB. Its plain-text shortcut avoids the machine for text, but every
  subnegotiation payload byte (GMCP JSON, MSDP, MSSP) still pays a full fire.
- TNC builds its machine **per connection**: 4.4 ms and 2.2 MB allocated for a server with 8 plugins, rebuilding
  73 states and 944 transitions each time.
- Neither library generates its machine. Stateless has no code generation; FSM generates only a trigger-type
  registry, a command dispatcher and diagrams. Both dispatch through dictionaries and delegates, and FSM
  allocates on every fire (a `HashSet` in `ResolveInitialLeaf`, lists for its ancestor search, a boxed trigger key).
- FSM is sync-only, cannot sit in a parent state, gives entry actions no trigger, and has no runtime
  introspection. TNC depends on all four.
- TNC's only runtime use of `GetInfo` is filling gaps — "every trigger this state doesn't handle goes to its bad
  state" — which forces `ApplySafetyConfiguration` to run after every plugin and caused 2.17's
  "refused as option 0" bug. That is a missing primitive (`OrElse`), not a need for introspection.

## 3. Goals and non-goals

**Goals**

1. Zero allocation per trigger on every synchronous path, including hierarchical moves with entry and exit.
2. One static definition per machine configuration; a machine instance is its state data plus a few fields.
3. Runs of values that a state handles identically are consumed by a vectorised scan, not per value.
4. Async actions and async decisions, with a synchronous fast path that costs nothing when nothing suspends.
5. Every structural error that can be caught at compile time is a compiler diagnostic in the app that
   assembles the machine: role violations, conflicts between plugins, uncovered decision outcomes.
6. The machine is inspectable as data (states, transitions, diagrams) without reflection. AOT- and trim-clean.
7. Generated code uses no dictionaries, hashing, or reflection at runtime: dispatch is `switch` statements over the
   generated `StateId` and the trigger value, storage is fields, and the definition is static arrays.

**Non-goals for v1** — parallel (orthogonal) regions; history states; immutable data with FSM-style
`ModifyData`; selecting plugins at runtime; more than one value-trigger type per machine; overlaying sibling
states' storage.

## 4. Decision log

Every decision below was taken or approved during design review on 2026-09-11.

| # | Decision | Status |
|---|---|---|
| D1 | The goal is performance: cost per trigger and per connection. | Decided |
| D2 | Transitions are fixed at compile time; the generator emits dispatch. | Decided |
| D3 | A standalone, generic library; TNC is the first consumer. | Decided |
| D4 | Two layers from one declaration: a pure plan and a fused executor. | Decided |
| D5 | The library owns states and their data; the implementer owns only context (metadata). | Decided |
| D6 | States own their data. A state is a type; the hierarchy is a tree; leaving a state clears its data. | Decided |
| D7 | Transitions are first-class and own the transformation. States do not own transitions, so a plugin can add a transition from a state it does not own. | Decided |
| D8 | A transition's access to each state follows its role relative to the lowest common ancestor: exiting `in`, staying `ref`, entering `ref`. The parameter list declares what it touches. | Decided |
| D9 | Async work that decides an outcome is split: an async decision over values, then a synchronous `Complete` per outcome over `ref`s, via a generated pending state that owns its cancellation. | Decided |
| D10 | While a decision is pending, other triggers are **deferred** by default. | Decided |
| D24 | Phase names are discoverable through code fixes, which work in Rider, Visual Studio and VS Code: `SALCH0901` (info, an empty class-form transition) and `SALCH0902` (hidden, on any transition) offer **Add Guard / Transform / Completed / CompletedAsync** and **Add Complete for** an uncovered outcome, each with the exact signature the transition's roles allow; `SALCH0206` offers a rename for near-miss names. A base class with overridable phases was rejected: phase signatures depend on the tree, and instances would replace static calls. | Decided |
| D23 | Deferral is invisible to the host: `FireAsync` completes when its input has been processed, including waiting for any decision it started, so awaiting it *is* the backpressure. There is no `IsDeferring`, `WhenReady()`, consumed count or `MachineDeferringException`. | Decided |
| D11 | The generator runs in the consuming app over the whole program; plugins are chosen at compile time. | Decided |
| D12 | Two kinds of trigger: *values* (a `switch`, ranges, `OrElse`) and typed *events*. | Decided |
| D13 | Transforms and guards may take the context; the signature shows it and the definition records it. `[Machine(Purity = Purity.Strict)]` forbids it, for machines that want the pure layer guaranteed. | Decided |
| D14 | Every part of a transition is a method named for its phase. Imperative names (`Guard`, `Transform`, `Decide`, `Complete`) run before the state changes; past-tense names (`Exited`, `Entered`, `Completed`) run after, in that order (§6.2). Several actions in one phase from different modules need an explicit `Order`. | Decided |
| D15 | Run transitions: a vectorised scan over values a state handles identically (§6.7). | Decided |
| D16 | Hooks (`OnTransitioned`, `OnUnhandled`, the exception hooks) are generated `partial` methods, free when not implemented. | Decided |
| D17 | Sync and async decisions, named by the .NET convention: `Decide` returns the union, `DecideAsync` returns `ValueTask<TUnion>`. The same holds for `Completed`/`CompletedAsync`. The suffix must match the shape. Guards cover the simple "pick a target" case. | Decided |
| D18 | Exception semantics (§6.9), with optional per-phase exception hooks that receive the exception and the transition and choose the recovery. | Decided |
| D19 | Concurrency is a compile-time choice per machine: `Checked` (default; concurrent use throws), `Unchecked` (no guard), or `Serialized` (any thread may fire; a `Channel` inbox, drained inline, processes calls in turn; an optional capacity makes it bounded, for backpressure on event producers). The deferring path uses the same inbox in every mode. | Decided |
| D20 | The library is named **StateAlchemist**; diagnostics use the prefix `SALCH` (StyleCop owns `SA`). | Decided |
| D21 | A state with children always enters an `[Initial]` child; the machine rests only in leaves. | Decided |
| D22 | Construction runs no actions; `StartAsync()` runs the initial path's `[Entered]` actions once, and `StopAsync()` runs `[Exited]` from the leaf to the root. "Not started" and "stopped" are states in the dispatch `switch`, so checking them costs nothing. An analyzer flags firing a machine that is not started on every path (`SALCH0801`). | Decided |

## 5. The model

### 5.1 Three kinds of assembly

```
StateAlchemist package      runtime types (attributes, IState<T>, IMachine<T>, plan/definition types)
                            + analyzers/: the source generator and diagnostics. No dependencies on
                            net8.0+; System.Threading.Channels on netstandard2.0.
      ▲
Declaring libraries         state structs, transition methods, events, grouped into modules.
(TNC core, TNC plugins,     No dispatch is generated here — only declaration-site diagnostics
 third-party plugins)       (e.g. "transition methods must be public static").
      ▲
Consuming app               [Machine] partial class — the generator runs here, sees every referenced
                            module, and emits storage, dispatch, the plan layer and the definition.
```

### 5.2 States

A state is a `public struct`. Its fields are its data. Its parent is given by a marker interface, so the
hierarchy is a tree the generator reads from the type system:

```csharp
public struct Connected : IRootState { public Encoding Charset; }                  // lives as long as the machine
public struct SubNegotiation : IState<Connected> { public byte Option; }
[Initial] public struct AwaitingOption : IState<SubNegotiation> { }                // where IAC SB lands
public struct Naws : IState<SubNegotiation> { public byte[]? Bytes; public int Index;
    public void Reset() => Index = 0; }                                            // keep the array, rewind
```

- **Active configuration** is the path from the root to one leaf. The machine rests only in leaves (D21):
  every state that has children in this machine marks exactly one child `[Initial]` (`SALCH0004`), and entering
  the parent — at construction or as a transition's target — continues down the `[Initial]` children to a leaf.
  Those states are part of the entering side (§6.3).
- **Lifetime.** Entering a state resets its storage slot; leaving it clears the slot. Clearing calls the state's
  `void Reset()` if it declares one (to keep buffers and their capacity), otherwise assigns `default`. Nothing
  is freed or allocated per entry: every state has one slot in the machine instance for the instance's life.
- **Machine-lifetime data** is the root state's data; the root is never exited.
- Buffers are allocated lazily by a transform (`to.Bytes ??= new byte[4]`) and kept by `Reset`, so a state's
  first entry may allocate once per machine instance and never again.

### 5.3 Triggers

- **Values.** One value type per machine (`TValue`): an integral type or an enum with a 16-bit or narrower
  underlying type. TNC uses `byte`. A value is its own payload. Transitions match a single value (`[On(31)]`),
  a range (`[OnRange(0x20, 0x7E)]`), or any other value (`[OnAny]` — the `OrElse` primitive).
- **Events.** `public readonly struct` types implementing `IEvent`, each with its own payload (`Error`,
  `Timeout`, `Disconnect`, and generated completion events). The machine gets one typed
  `FireAsync(in TEvent)` overload per event type: no boxing, no runtime type test.

### 5.4 Transitions

A transition owns the transformation of its source into its target. It is declared wherever convenient — a
plugin declares transitions *from* core states into its own. It takes one of two forms:

- **A method**, when the transition is only a transform: `[Transition]` on a `public static void` method. The
  method *is* the transition's `Transform`.
- **A static class**, when the transition has more than one part: `[Transition]` on a `public static class`
  whose methods are named for their phase (D14). Every method is optional; each runs at the point its name says.

| Method | Tense | Runs | Shape |
|---|---|---|---|
| `Guard` | imperative | before anything changes: may this transition fire? | `static bool`, read-only access |
| `Transform` | imperative | before the state changes: turn the source into the target | `static void`, synchronous, `ref`/`in` by role |
| `Completed` / `CompletedAsync` | past | after the state has changed, and after every state's `[Exited]` and `[Entered]` actions | `void Completed` or `ValueTask CompletedAsync`; external code |

The rule is the grammar: **imperative names run before the state changes, past-tense names run after**, and the
past-tense phases always run in the order `Exited` → `Entered` → `Completed` (§6.2). `Exited` and `Entered`
belong to *states* (§5.5); `Completed` belongs to the transition.

The .NET `Async` suffix is part of the name (D17): a phase that may suspend is written `CompletedAsync` or
`DecideAsync` and returns `ValueTask` (or `ValueTask<T>`); without the suffix it is synchronous. A name whose
suffix disagrees with its return type is `SALCH0207`, with a code fix that renames it. `Guard`, `Transform` and
`Complete` have no `Async` form: they run before the state changes and are synchronous by rule.

```csharp
[Module] public static partial class NawsModule
{
    [Transition(From = typeof(AwaitingOption), To = typeof(Naws)), On(31)]                     // move
    public static void Begin(in AwaitingOption from, ref SubNegotiation parent, ref Naws to) => parent.Option = 31;

    [Transition(From = typeof(Naws)), OnAny]                                                   // stay
    public static void Capture(ref Naws self, byte value)
    { self.Bytes ??= new byte[4]; if (self.Index < 4) { self.Bytes[self.Index] = value; self.Index++; } }

    [Transition(From = typeof(Naws), To = typeof(NawsEscaping)), On(255)]
    public static void Escape(in Naws from, ref NawsEscaping to) => to.Captured = from;

    [Transition(From = typeof(NawsEscaping), To = typeof(AwaitingCommand)), On(SE)]              // class form
    public static class Finish
    {
        public static bool Guard(in NawsEscaping from) => from.Captured.Index == 4;
        public static ValueTask CompletedAsync(TelnetContext context, NawsEscaping from)
            => context.OnWindowSizeAsync(from.Captured.Bytes!);
    }
}
```

**Kinds**, decided by `To`:

| `To` | Kind | Data |
|---|---|---|
| omitted | **Stay** (internal) | Nothing exits or enters; `ref Self` persists. |
| a different state | **Move** | Exit to the LCA, enter to the target. |
| the source itself | **Re-enter** | Exits and re-enters: the data is cleared. The source is passed as a snapshot. Warning `SALCH0301` when the state has data, unless it is the root, which is never exited. |

**Parameter binding.** The generator binds each parameter by type and checks it against the transition's
roles (§6.3). Allowed parameters:

| Parameter | Meaning |
|---|---|
| `in S` / `ref S` for a state `S` | Access to `S`'s slot, by role. |
| `TValue value` | The value that fired (value transitions). |
| `ReadOnlySpan<TValue> run` | The run being consumed (run transitions, §6.7). |
| `in TEvent e` | The event that fired (event transitions). |
| `in TConfig config` | The machine's immutable configuration. |
| `TContext context` | The implementer's context (D13). The definition marks the transition *context-dependent*; under `[Machine(Purity = Purity.Strict)]` it is `SALCH0205`. |
| the outcome case value | A decision's `Complete` only (§5.6). |

Anything else is error `SALCH0204`. `Transform` is synchronous and returns `void`; C# already forbids `ref`/`in`
parameters on async methods, so no transform can await, whatever it takes.

**Guards.** A class-form transition's `Guard` binds like `Transform`, with every state read-only (`in`).
Several transitions may share a source and a trigger only if at most one is unguarded; when more than one is
guarded they need distinct `Order` values on `[Transition]` (`SALCH0102`). Guards are tried in `Order`; the
unguarded transition is tried last.

### 5.5 Actions — external code

Actions run the implementer's code: network writes, callbacks, logging. They may be `void` or `ValueTask`, and
they always run *after* the state has changed, so their names are past tense (D14):

- a transition's own `Completed` method (§5.4) — "this transition happened";
- `[Exited(typeof(S))]` on a module method — "`S` was left", whichever transition left it;
- `[Entered(typeof(S))]` on a module method — "`S` was entered", whichever transition entered it.

```csharp
[Transition(From = typeof(Willing), To = typeof(GmcpEnabled)), On(GMCP)]
public static class AcceptGmcp
{
    public static void Transform(ref Connected root) => root.GmcpEnabled = true;
    public static ValueTask CompletedAsync(TelnetContext context) => context.WriteAsync([IAC, DO, GMCP]);
}

[Entered(typeof(AwaitingCommand))] public static void Ready(TelnetContext context) => context.NotifyReady();
[Exited(typeof(Naws), Order = 1)] public static void Trace(TelnetContext context, Naws naws) => context.Log(naws);
```

When one phase has several actions for the same state or transition from different modules, each needs a
distinct `Order` (`SALCH0103`); within one module, declaration order is used.

Allowed parameters: `TContext`, the value/event (or `ReadOnlyMemory<TValue>` for a run), `CancellationToken`
(the machine's lifetime token), and states **by value** (a snapshot — async methods cannot hold `ref`s; sync
actions may take `in`). An action may read the states active after the change and — because exiting states are
cleared only after every action has run (§6.2) — the states that were left.

### 5.6 Decisions — where external code chooses the outcome

A decision is how the outside world picks between transformations: an API lookup, an auth check, a user
callback that accepts or refuses. It is a class-form transition whose `Decide` or `DecideAsync` returns a **union** of outcome
types; each case gets a `Complete` overload, which names its own target.

```csharp
[Decision(From = typeof(AuthRequested)), On(SE)]
public static class CheckAuth
{
    public static async ValueTask<AuthOutcome> DecideAsync(TelnetContext context, AuthRequested request, CancellationToken ct)
        => await context.Accounts.VerifyAsync(request.Data, ct) ? new Accept(...) : new Reject(...);

    [To(typeof(Authenticated))] public static void Complete(in AuthRequested from, ref Authenticated to, Accept outcome) { ... }
    [To(typeof(AwaitingCommand))] public static void Complete(in AuthRequested from, ref AwaitingCommand to, Reject outcome) { ... }

    public static ValueTask CompletedAsync(TelnetContext context, Accept outcome) => context.OnAuthenticatedAsync(outcome);
    public static ValueTask CompletedAsync(TelnetContext context, Reject outcome) => context.WriteAsync(outcome.Reply);
}
```

- The phases keep the D14 grammar: `Guard`, `Decide` and `Complete` run before the state changes (`Complete` is
  each outcome's `Transform`); `Completed`/`CompletedAsync` runs after, and may be overloaded by outcome type.
- `Decide`/`DecideAsync` may take the context (it is the bridge to the outside), values, events, and states by value.
- `Decide` (returns the union) runs inline: decide, then complete, as one transition.
- `DecideAsync` (returns `ValueTask<TUnion>`) gets a generated pending state (§6.5). Declaring both is `SALCH0208`.
- Every union case needs exactly one `Complete` (`SALCH0401`, `SALCH0402`). The generator reads the cases from the
  union's single-parameter constructors, so declaring libraries need the `[Union]` shape, not a particular
  compiler version.

### 5.7 Modules and the machine declaration

A **module** is a `[Module] static partial class` grouping transitions, actions and decisions — TNC core is a
module; each plugin is a module. States may be declared anywhere; a state is part of a machine when the root
is it, or a transition in an included module names it.

```csharp
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext), Config = typeof(TelnetConfig))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class MudTelnet;
```

An app that needs several configurations (a client and a server, say) declares several machine types.

## 6. Semantics

### 6.1 Resolving a trigger

For a value trigger, starting at the active leaf and walking up to the root, the first match wins; **at each
level**:

1. transitions on the exact value (guarded ones in `Order`, then the unguarded one);
2. transitions on a range containing the value;
3. an `[OnAny]` transition.

Only then does resolution move to the parent. So a state's `OrElse` shadows everything its ancestors do with
values — which is what "refuse anything this state doesn't handle" means. **`OrElse` never matches events**:
an `[OnAny]` in `Willing` cannot swallow `Error` recovery declared on the root.

For an event: the exact event type at each level, leaf first. Nothing matching at any level is *unhandled*
(§6.8).

Ambiguity is a compile error: two unguarded transitions for the same source and trigger (`SALCH0101`) — including
two plugins claiming the same option byte, which the whole-program generator sees.

### 6.2 Order of operations inside one transition

The phase names say where each piece runs (D14): imperative names before the state changes, past-tense names
after it.

| Step | Phase | Declared as | |
|---|---|---|---|
| 1 | **`Guard`** | the transition's `Guard` | Resolve (§6.1); read-only. |
| 2 | *reset* | — | Reset the entering slots (target side, LCA exclusive). A state being started over is first copied to a snapshot; every other entering slot is inactive, so nothing can observe the reset. |
| 3 | **`Transform`** | the transition's `Transform` (or `Complete`) | Synchronous; the source intact, the target fresh (§6.3). |
| 4 | *commit* | — | The active leaf becomes the target. **The state has now changed.** |
| 5 | **`Exited`** | `[Exited(typeof(S))]` | For each exited state, leaf → LCA; within a state, by `Order`. |
| 6 | **`Entered`** | `[Entered(typeof(S))]` | For each entered state, LCA → target; within a state, by `Order`. |
| 7 | **`Completed`** | the transition's `Completed` | Last: every state involved has been exited or entered. |
| 8 | *clear* | — | Clear the exiting slots, leaf → LCA. |
| 9 | *drain* | — | Process events queued during the transition (§6.4), then return. |

Steps 5–7 are started one after another; when a `ValueTask` completes synchronously the next starts at once, and
only one that actually suspends moves the rest to a continuation. Exiting slots are inactive from step 4, so
clearing them at step 8 instead is invisible to other transitions — and it lets steps 5–7 read what was left.
A **stay** has no exits or entries: steps 2, 5, 6 and 8 do nothing, and `Completed` runs after the transform.

For a state that is **started over** — a re-entry's source, or the target of a move to a state on the active path —
the exiting slot is also the entering slot: step 2 first copies it to a snapshot, which `Transform` receives as `in`
and the `Exited` actions receive as their value; step 8 leaves the started-over slot alone. If step 3 throws, the
snapshot is put back, so nothing commits.

### 6.3 Roles and access

The generator computes the lowest common ancestor (LCA) of source and target at compile time. Every state a
transition can name has exactly one role:

| Role | States | Transform access | Why |
|---|---|---|---|
| Exiting | source leaf → below the LCA | `in` | Cleared at the end of the transition; read to carry data out. |
| Staying | the LCA and above | `ref` | Survives; edits persist. |
| Entering | below the LCA → target | `ref`, starts reset | Being initialised. |
| Anything else (siblings, unrelated) | — | none | Inactive before and after. |

`ref` on an exiting state is `SALCH0201` (with a code fix to `in`); naming a state with no role is `SALCH0202`.
For a **stay**, everything on the active path is staying.

Transitions declared **from an ancestor** apply to every leaf in its subtree (e.g. `Error` recovery from the
root). The generator emits them per leaf, so exits and entries always use the *real* LCA of the actual leaf and
the target — states the two share are never exited. A parameter, though, is declared once for all those leaves,
so it may name a state only if it **binds the same way from every leaf**: `in` must read the state as it was
(exiting or staying), `ref` must write the state as it will be (entering or staying). `ref Naws` on a transition
from `SubNegotiation` to `Naws` binds the fresh `Naws` whether the leaf was `AwaitingOption` (entering) or `Naws`
itself (started over), so it is allowed; `in AwaitingOption` on the same transition reads a state that exists for
one leaf and not the other, so it is `SALCH0202`. Descendants below the declared source are exited and cleared
without being nameable.

**Moving to a state on the active path starts it over.** When the target is the active leaf or one of its
ancestors, the lowest common ancestor is taken as the target's parent, so the target is exited and entered again
with fresh data; a re-entry is this case. A move to the root is the exception: the root is never exited, so
everything below it is exited and the root's initial path entered. A state both exited and entered by one move is
*started over*: `in` reads its old data (from the snapshot of §6.2), `ref` writes its new.

Where data sits in the tree **is** its lifetime: data that must outlive a state belongs in an ancestor common to
both sides of the transitions that need it.

### 6.4 Run-to-completion

A transition runs to completion — through all its external actions, awaited — before the next trigger is
processed. A trigger fired while a transition is in progress (an action firing `Error`, say) is **queued** and
processed at step 9. The event queue is a small inline buffer that allocates only if more than four events
queue at once. Value triggers are never queued by the machine (§6.6).

### 6.5 Async decisions and pending states

An async decision generates a **pending state** below the active leaf — a decision plans as a stay, so nothing is
exited to enter it — and every state on the active path, the source included, stays active with its data while
the decision runs.

1. The trigger resolves to the decision, and the machine moves into the pending state. The caller's `FireAsync`
   does not complete yet: it completes once the decision has been completed and the rest of the caller's input
   processed (§6.6).
2. The decision starts with **values**: a snapshot of the states it takes, the context, and a
   `CancellationToken` linked to the pending state's slot.
3. When it completes, its result is processed as a generated completion event: the case selects its
   `Complete` overload, which runs as a normal transition *from the pending state* — `Complete` as its
   `Transform`, then the §6.2 steps — with `ref`s to the data as it is **then**.
4. **Leaving the pending state cancels the decision.** The pending slot holds the `CancellationTokenSource`;
   clearing the slot (disconnect, timeout, any transition out) cancels it. A completion that arrives after the
   pending state was left is dropped — it cannot apply to a state that is no longer active.
5. A decision that throws — `Decide` or `DecideAsync` — ends: the pending state is left, and a `DecisionFailed`
   event fires from the active leaf; unhandled, it follows §6.8. `DecisionFailed` is a runtime type carrying the
   decision's name and the exception, so modules can declare transitions on it.
6. The pending state ends when its decision does — with an outcome, a failure, or a transition out of it. Events
   that waited then run, in arrival order, before the rest of the paused input. One decision is pending at a time:
   an event handled while one is pending cannot start another (`InvalidOperationException`).

### 6.6 Deferral and backpressure (D10, D23)

Deferral is invisible to the host. **`FireAsync` completes when its input has been processed** — every value of a
batch, including waiting for any decision one of them started. The rest of the batch stays in the caller's
`ReadOnlyMemory<TValue>`, which the machine holds until the returned `ValueTask` completes; nothing is copied.

Awaiting `FireAsync` is therefore the backpressure. A read loop that awaits it before reading again stops reading
while a decision is pending; the `Pipe`'s buffer holds what has arrived, and once it passes its pause threshold
the pipe stops reading the socket, which pushes back on the sender. The host writes the ordinary loop and gets
this for free:

```csharp
var read = await reader.ReadAsync(ct);
foreach (var segment in read.Buffer)
{
    await machine.FireAsync(segment);            // waits through any decision
}
reader.AdvanceTo(read.Buffer.End);
```

While a decision is pending:

- **Events from other callers are accepted in every concurrency mode.** The caller whose input started the
  decision is awaiting, not running, so the machine is idle; events go through the inbox (§6.10). Events the
  pending state handles (`[Decision(..., Handle = new[] { typeof(Disconnect), typeof(Timeout) })]`) resolve at
  once from the pending state, and a transition out of it cancels the decision (§6.5); the rest queue until the
  decision resolves, and their callers' `FireAsync` completes when they have been processed.
- **Values from another caller are misuse**, as at any other time: a `Checked` machine throws
  `ConcurrentUseException`, a `Serialized` machine queues them behind the waiting batch. Values come from one
  stream, in order.
- **Code inside the machine must not fire it.** An action or decision that awaited `FireAsync` on its own machine
  would be waiting for itself; use `Enqueue`, which queues the event and returns at once. The machine detects the
  mistake where detection is free: a `Checked` machine is busy during actions, so the call throws
  `ConcurrentUseException`; and every machine marks a running `DecideAsync`, and every transition run while a
  decision is pending, with an `AsyncLocal` (both are rare, so the cost is negligible), so a call from inside one
  throws the same. Detecting it in the actions of a
  `Serialized` or `Unchecked` machine would cost an `AsyncLocal` per transition, so there it is documented, not
  checked.

If the machine is stopped while a caller is waiting on a decision, the decision is cancelled, the rest of that
caller's input is discarded, and its `FireAsync` throws `MachineNotRunningException`.

### 6.7 Run transitions (D15)

A stay transition matching `[OnAny]` or a range may be marked `[Run]` and take `ReadOnlySpan<TValue>`:

```csharp
[Transition(From = typeof(GmcpPayload)), OnAny, Run]
public static void Capture(ref GmcpPayload self, ReadOnlySpan<byte> run) => self.Buffer.Append(run);
```

For each state, the generator computes the **stop set**: the values with a more specific transition at that
level (§6.1). In the batch API, when the active leaf has a run transition, the generated code scans ahead
with `SearchValues<TValue>` (`net8.0`+; a scalar loop on `netstandard2.0`) to the next stop value, and hands
the whole run to the transform in one call. TNC's 2.17 plain-text shortcut is a run with the stop set
`{IAC, LF}`; GMCP/MSDP payloads are runs with the stop set `{IAC}`. A run transition's `Completed` receives the
run as `ReadOnlyMemory<TValue>`. A single value fired with `FireAsync(TValue)` reaches a run transition as a run of
length one: runs change how fast a batch is consumed, never what it does.

### 6.8 Unhandled triggers

A trigger that matches nothing at any level calls the generated `partial void OnUnhandled(StateId state, TValue
value)` (and an event overload), then is ignored. The app implements the partial method to log, or to fire an
`Error` event. `[Machine(Unhandled = Unhandled.Throw)]` throws instead. Diagnostic `SALCH0501` (warning) lists
reachable leaves that leave some values unhandled with no `[OnAny]` on their path.

### 6.9 Exceptions (D18)

- **Guard or transform throws** (steps 1–3): no commit; the machine stays in the source state. Entering slots
  were inactive and are reset on the next entry; a state being started over gets its snapshot back (§6.2). Edits
  already made to *staying* slots are **not** rolled back —
  transforms should validate before they mutate. The exception propagates from `FireAsync`.
- **An `Exited`, `Entered` or `Completed` action throws** (steps 5–7): the transition has committed. Remaining
  actions of that transition are skipped, exiting slots are still cleared (step 8 runs in a `finally`), queued
  events are kept — they run before the next trigger — and the exception propagates. (TNC's per-byte `catch` keeps working as it does today.)
- **`Decide` or `DecideAsync` throws**: the decision ends, and a `DecisionFailed` event (carrying the decision's
  name and the exception) fires from the active leaf. Transitions on that event are the recovery; unhandled, it
  follows §6.8.
- **A batch whose transition throws** stops there: the rest of the batch is discarded, a decision it started is
  cancelled, and queued events are kept.

**Exception hooks (D18).** Each phase has an optional hook — a generated `partial` method, so it costs nothing
unless the app implements it (D16). A hook receives the exception and the transition, and chooses the recovery:

```csharp
partial void OnGuardException(Exception exception, in TransitionInfo transition, ref ExceptionResolution resolution);
partial void OnTransformException(Exception exception, in TransitionInfo transition, ref ExceptionResolution resolution);
partial void OnExitedException(Exception exception, in TransitionInfo transition, ref ExceptionResolution resolution);
partial void OnEnteredException(Exception exception, in TransitionInfo transition, ref ExceptionResolution resolution);
partial void OnCompletedException(Exception exception, in TransitionInfo transition, ref ExceptionResolution resolution);
```

`TransitionInfo` names the source, target, trigger, phase, the declaring transition, and — for `Exited` and
`Entered` — which state's action threw. `resolution` arrives set to `Rethrow`, the default behaviour above:

| `resolution` | `Guard` | `Transform` / `Complete` | `Exited` / `Entered` / `Completed` |
|---|---|---|---|
| `Rethrow` (default) | No commit; propagate. | No commit; propagate. | Skip the remaining actions; clear; propagate. |
| `Skip` | Treat the guard as false and try the next candidate (§6.1). | No commit; drop the trigger; return normally. | Skip the remaining actions; clear; return normally. |
| `Continue` | — (as `Skip`) | — (as `Skip`): a half-run transform must never commit. | Swallow; run the remaining actions. |

A hook may also queue recovery with the generated `Enqueue(in TEvent)` — say an `Error` event — which the
machine processes at step 9 whatever the resolution. The generator emits a phase's `try`/`catch` only when the
app implements that phase's hook, so a machine without hooks has no exception-handling code on its paths.

### 6.10 Threading (D19)

Concurrency is chosen per machine at compile time, `[Machine(Concurrency = …)]`, and the generator emits only
that mode's code.

| Mode | Who may fire | Misuse | Cost per call (measured, uncontended) |
|---|---|---|---|
| `Checked` (default) | One caller at a time | Detected: throws `ConcurrentUseException`, never corrupts state | +6.5 ns (one `Interlocked.Exchange`) |
| `Unchecked` | One caller at a time | Undefined: state can corrupt | none |
| `Serialized` | Any thread | None: calls are queued and processed one at a time, in turn | +20.7 ns (unbounded `Channel` inbox, drained inline); +47.7 ns bounded |

- **`Checked`** follows `Dictionary`, which detects concurrent modification and throws instead of corrupting:
  wrong use fails loudly, and correct use pays one interlocked operation per call.
- **`Unchecked`** follows `Channels`' `SingleReader`/`SingleWriter`: the host promises a single caller, and the
  machine takes the faster path.
- **`Serialized`** follows Orleans' turn-based grains: any thread may call `FireAsync`. Each call is written to a
  `Channel` created with `SingleReader = true`, and whichever caller finds the machine idle drains it inline with
  `TryRead` — no dedicated consumer task, no thread hop when uncontended. Each transition, *including its awaited
  actions*, runs to completion before the next begins. That is what external locking cannot give Stateless: a
  lock around `FireAsync` breaks down once handlers are async.
  - **Bounded inbox:** `[Machine(Concurrency = Concurrency.Serialized, InboxCapacity = n)]` uses a bounded channel
    with `FullMode = Wait`, so event producers that outrun the machine are made to wait instead of growing the
    queue. It costs +47.7 ns per call (the bounded channel takes a lock), so it is opt-in.
  - **Inbox items** are a generated struct: a tag plus the value or one of the event payloads. Nothing is boxed.
  - **Completion:** a caller whose trigger queued behind another awaits a pooled `IValueTaskSource`
    (`ManualResetValueTaskSourceCore`) that completes when its transition does, carrying that transition's
    exception if it throws. Nothing is allocated per call in steady state.
- **Guards are paid per call, not per value.** The batch `FireAsync(ReadOnlyMemory<TValue>)` takes the guard once
  for the whole batch, so a 4 KB read pays its +6.5 ns or +18 ns once, not 4,096 times.
- **The deferring path is serialised in every mode.** While a decision is pending, the caller that started it is
  awaiting and the decision's completion arrives on another thread, so events fired meanwhile go through the same
  `Channel` inbox even in `Checked` and `Unchecked` machines — which is what lets a disconnect or timeout reach a
  pending decision (§6.6). The cost exists only while a decision is pending.

For comparison on the same machine: a raw `ConcurrentQueue` inbox +18.3 ns, `System.Threading.Lock` +13 ns,
`Monitor` +15 ns, `SemaphoreSlim.WaitAsync` +32 ns — and a lock alone is not correct once actions are async.
The `Channel` costs 2.4 ns more than the raw queue it is built on, for standard completion semantics and the
bounded option.

## 7. Generated code

For `[Machine] partial class MudTelnet` the generator emits into that class:

| Member | Purpose |
|---|---|
| one field per state struct, `StateId _leaf`, pending slot, event queue | Storage. |
| `MudTelnet(TContext context, in TConfig config)` | Construction: resets every slot and sets the initial leaf (root → `[Initial]` children). Runs no actions. |
| `ValueTask StartAsync()`, `ValueTask StopAsync()`, `DisposeAsync()` | Lifecycle (D22): `StartAsync` runs the initial path's `[Entered]` actions once; `StopAsync` cancels a pending decision and runs `[Exited]` from the leaf to the root; `DisposeAsync` stops if started. Firing before start or after stop throws `MachineNotRunningException`; starting twice throws `InvalidOperationException`. |
| `StateId State`, `bool IsIn(StateId)` | Current leaf; ancestry test. |
| `bool TryGet{State}(out {State} value)` per state | Read a copy of an active state's data. |
| `ValueTask FireAsync(TValue)`, `ValueTask FireAsync(ReadOnlyMemory<TValue>)`, `FireAsync(in TEvent)` × N | Executor. Each completes when its input has been processed, including waiting for any decision it started (§6.6). |
| `void Fire(ReadOnlySpan<TValue>)`, `void Fire(TValue)` | Only when no action or decision in the machine is async (`SALCH0601` otherwise). |
| `void Enqueue(in TEvent)` × N | Queue an event for processing after the current transition (recovery from hooks, §6.9). Only for code running inside a transition: called from outside, it throws `InvalidOperationException`. |
| `TransitionPlan Plan(TValue)`, `Plan(in TEvent)` | Pure layer: what would fire, evaluating guards read-only, without firing. |
| `static MachineDefinition Definition` | States, hierarchy, transitions, guards, actions — as data. |
| `const string Mermaid`, `const string Dot` | Diagrams. |
| `partial void OnTransitioned(in TransitionInfo t)`, `partial void OnUnhandled(...)`, `partial void On{Phase}Exception(...)` | Hooks; removed by the compiler when not implemented (D16, §6.9). |

Nothing generated uses a dictionary, a hash, or reflection (goal 7). The executor is a `switch` on `_leaf`,
then on the value; each move is a generated method with its reset,
transform, commit, actions and clear written out in order. Sketch of the output:

```csharp
public ValueTask FireAsync(byte value)
{
    switch (_leaf)
    {
        case StateId.Naws:
            if (value == 255) return Move_Naws_NawsEscaping(value);
            NawsModule.Capture(ref _naws, value);            // stay, [OnAny]
            OnTransitioned(new TransitionInfo(StateId.Naws, StateId.Naws, value));
            return default;
        case StateId.Willing:
            return value == 201 ? Move_Willing_Idle_201(value) : Move_Willing_BadWilling(value);   // [OnAny]
        // ...
    }
}

private ValueTask Move_Willing_Idle_201(byte value)
{
    _idle = default;                                         // 2 reset
    AcceptGmcp.Transform(ref _connected);                    // 3 Transform
    _leaf = StateId.Idle;                                    // 4 commit
    var t = AcceptGmcp.CompletedAsync(_context);             // 5–7 Exited, Entered, Completed (only Completed here)
    if (!t.IsCompletedSuccessfully) return Continue_Willing_Idle_201(t);
    _willing = default;                                      // 8 clear
    return DrainOrDefault();                                 // 9 drain
}
```

The async continuation (`Continue_…`) finishes the remaining actions and steps 8–9. On `net8.0`+ it uses
`PoolingAsyncValueTaskMethodBuilder`, so even a suspended action allocates nothing in steady state.

## 8. Diagnostics

| Id | Severity | Where | Condition |
|---|---|---|---|
| SALCH0001 | Error | declaring lib | State type is not a `public struct`, or has two parents. |
| SALCH0002 | Error | declaring lib | Transition/action/decision is not `public static`. |
| SALCH0003 | Error | app | Hierarchy cycle, or a state's parent is not in the machine. |
| SALCH0004 | Error | app | A state with children in the machine has no `[Initial]` child, or more than one. |
| SALCH0101 | Error | app | Two unguarded transitions for one source and trigger (including across modules). |
| SALCH0102 | Error | app | Several guarded transitions for one source and trigger without distinct `Order`s. |
| SALCH0104 | Error | app | A trigger value outside the value type. |
| SALCH0105 | Error | app | A value type that is not integral or an enum of 16 bits or fewer. |
| SALCH0106 | Error | declaring lib | A transition with no `From`, no trigger, mixed value and event triggers, an empty range, or a non-constant value. |
| SALCH0107 | Error | app | A machine without `Root` or `Value`, or an `[Include]` of a type that is not a `[Module]`. |
| SALCH0103 | Error | app | Several actions in one phase for the same state or transition, from different modules, without distinct `Order`s. |
| SALCH0201 | Error + fix | app | `ref` on a state the transition exits. |
| SALCH0202 | Error | app | Parameter names a state with no role in the transition, or that binds differently from different leaves of an ancestor-declared transition. |
| SALCH0203 | Error | app | `Transform` or `Complete` is not synchronous `void`; `Guard` is not synchronous `bool`. |
| SALCH0204 | Error | app | Parameter type not bindable. |
| SALCH0205 | Error | app | The context taken by `Guard`, `Transform` or `Complete` in a machine declared `Purity.Strict`. |
| SALCH0206 | Error | declaring lib | A class-form transition has a method whose name is not a phase (`Guard`, `Transform`, `Decide`, `DecideAsync`, `Complete`, `Completed`, `CompletedAsync`). |
| SALCH0207 | Error + fix | declaring lib | A phase's `Async` suffix disagrees with its return type, or `Guard`/`Transform`/`Complete` is written with `Async`. |
| SALCH0208 | Error | declaring lib | A decision declares both `Decide` and `DecideAsync`. |
| SALCH0209 | Warning | app | `Unchecked` concurrency on a machine whose actions or decisions are async: continuations run on other threads, so a single caller must still await every `FireAsync` before the next. |
| SALCH0301 | Warning | app | Re-entry on a state that has data (it will be cleared). Not reported for the root, which is never exited. |
| SALCH0401 | Error | app | Decision outcome case with no `Complete`. |
| SALCH0402 | Error | app | `Complete` for a type that is not a case of the decision's union. |
| SALCH0501 | Warning | app | Reachable leaf with unhandled values and no `[OnAny]` on its path. |
| SALCH0502 | Warning | app | State unreachable from the root's initial leaf. |
| SALCH0601 | Error | app | Synchronous `Fire` used on a machine with async actions or decisions. |
| SALCH0701 | Error | app | `[Run]` on a transition that is not a stay, or whose stop set cannot be computed. |
| SALCH0901 | Info | declaring lib | A class-form transition declares no phases; code fixes add them with the right signatures (D24). |
| SALCH0902 | Hidden | declaring lib | Carries the same code fixes on any transition (D24). |
| SALCH0801 | Warning | anywhere | `FireAsync` on a machine that this method constructed and has not started on every path to that call (control-flow analysis within the method). Machines that cross methods, fields or DI are left to the runtime check. |

## 9. Performance targets (acceptance criteria)

Measured with BenchmarkDotNet on x64, `net11.0`, against Stateless 5.20 on the same machine shape and against a
hand-written `switch` baseline.

| Scenario | Target |
|---|---|
| Stay transition, sync transform, one value | **0 B**, ≤ 10 ns |
| Move across two levels with sync `[Exited]`/`[Entered]` actions | **0 B**, ≤ 25 ns |
| Run capture of a 1 KB payload | **0 B**, ≥ 1 GB/s |
| Action that suspends (steady state, `net8.0`+) | **0 B** amortised (pooled builder) |
| Within the hand-written `switch` baseline | ≤ 2× on every scenario above |
| Machine construction (TNC-sized, ~80 states) | ≤ 10 µs; allocations = the instance only |
| Generator, TNC-sized machine (~80 states, ~1,000 transitions) | Full generation ≤ 1 s; incremental — no regeneration on unrelated edits |

Every sync-path target is also an allocation test in the test suite (`GC.GetAllocatedBytesForCurrentThread`),
so a regression fails the build, not only the benchmark.

## 10. Testing

- **Generator**: `CSharpGeneratorDriver` tests over small declaration sets, with snapshot comparison of the
  emitted source; every diagnostic has a positive and a negative test; the code fix for `SALCH0201` is tested.
- **Cross-assembly from day one**: test declarations live in a separate declaring-library project, consumed by
  an app project, so the whole-program path is the only path ever tested.
- **Semantics**: entry/exit order, roles, precedence, re-entry snapshots and clearing are tested against a small
  reference interpreter over randomly generated trees (property tests): for any tree and trigger sequence, the
  generated machine and the interpreter agree on state, data and action order.
- **Async and deferral**: decisions completing sync and async, cancellation on exit, late completions dropped,
  `Handle` events, and a `Pipe` sample proving the reader stops and resumes at the right byte.
- **Concurrency**: `Checked` throws on overlapping calls from two threads and never corrupts state; `Serialized`
  under contention processes every call exactly once, one transition at a time, including across awaited actions.
- **Allocation tests** for every sync path (§9). **Benchmarks** in a separate project. **AOT**: a
  `PublishAot` sample must publish with zero warnings in CI.
- Framework: TUnit, matching TNC. Runtime matrix: `net8.0`, `net10.0`, `net11.0`; the `netstandard2.0` build is
  compile-checked.

## 11. Packaging and repository

- One package, `StateAlchemist`: `lib/{netstandard2.0,net8.0,net10.0,net11.0}` (runtime; its only dependency is
  `System.Threading.Channels` on `netstandard2.0`) and
  `analyzers/dotnet/cs` (generator and analyzers, `netstandard2.0`, Roslyn 4.8 — i.e. apps building with the
  .NET 8 SDK or later).
- Repository conventions follow TNC: warnings as errors, `global.json` pinning the .NET 11 SDK (`latestFeature`),
  CI testing each runtime by framework, a CHANGELOG, a version bump per PR.

## 12. Mapping TNC onto it (outline for the migration spec)

| TNC on Stateless | On StateAlchemist |
|---|---|
| `Configure(S)` | `public struct S : IState<Parent>` |
| `SubstateOf(P)` | the `IState<P>` marker |
| resting in a parent (`Accepting`, `SubNegotiation`) | an `[Initial]` leaf child where the parent used to rest (e.g. `AwaitingOption`) |
| `Permit(t, T)` | `[Transition(From = S, To = T), On(t)]` |
| `PermitReentry(t)` + `OnEntryFrom(t, Capture)` | stay transition `Capture(ref S self, byte value)`, or a `[Run]` |
| `OnEntryFrom(t, action)` | that transition's `Completed` |
| `OnEntryAsync` / `OnExitAsync` | `[Entered]` / `[Exited]` actions, or the transition's `Completed` |
| `Ignore(t)` | stay transition with an empty transform |
| `PermitDynamic` | guards, or a sync decision |
| `TelnetSafeInterpreter` gap filling via `GetInfo` | `[OnAny]` transitions to the `Bad*` states |
| `OnUnhandledTriggerAsync` + `Trigger.Error` | `OnUnhandled` hook + an `Error` event transition from the root |
| `OnTransitioned` trace logging | `partial void OnTransitioned` |
| 2.17 plain-text shortcut | `[Run]` on `ReadingCharacters`, stop set `{IAC, LF}` |
| auth / TTABLE / encryption callbacks | async decisions with union outcomes, deferred by default |
| plugin `ConfigureStateMachine` + `AddPlugin<T>()` | a `[Module]` + `[Include(typeof(T))]` on the app's machine |
| `ByteOrTrigger`, `ReadNextCharacter` | gone: values are bytes, `Error` is an event |

The migration breaks TNC's public API (`TelnetStateMachine`, `IProtocolContext.StateMachine`,
`ConfigureStateMachine`, the `State` enum) and is TNC 4.0. How TNC's runtime options (client/server mode,
per-plugin options) map onto machine declarations and `TConfig` is the migration spec's first question.

## 13. Milestones

Each milestone ends with its exit criteria green in CI.

| | Scope | Exit criteria |
|---|---|---|
| **M0** Skeleton | Repo, runtime/generator/tests/benchmarks/samples projects, CI matrix, `global.json`. | An empty generator runs in a sample app on all TFMs. |
| **M1** Flat machines, cross-assembly | States without hierarchy; value triggers (`On`, `OnAny`); stay/move; sync transforms; `switch` dispatch; `StateId`; `Definition`; SALCH0001–0002, SALCH0101, SALCH0203–0204. Declarations in a separate library from the start. | A flat telnet-negotiation sample; 0 B per fire; first benchmark against Stateless recorded. |
| **M2** Hierarchy and data lifetimes | `IState<T>`; LCA and roles; generated exit/entry sequences; `Reset`; per-level precedence; ranges; re-entry snapshots; guards and `Order`; `[Initial]` children; SALCH0003–0004, SALCH0102, SALCH0201–0202, SALCH0301, SALCH0502. | Property tests against the reference interpreter pass on random trees. |
| **M3** Actions and events | `void`/`ValueTask` actions with the sync fast path and continuations; class-form transitions and phase names; `Completed`, `[Exited]`, `[Entered]` with `Order`; `StartAsync`; typed events; the event queue; hooks; exception semantics; exception hooks and `Enqueue`; `StopAsync`/`DisposeAsync`; SALCH0801; SALCH0103, SALCH0205–0207, SALCH0501, SALCH0601. | Order-of-operations and exception-hook tests; 0 B when actions complete synchronously. |
| **M4** Decisions and deferral | Sync and async decisions; pending states; union outcomes; cancellation on exit; `Handle`; `FireAsync` waiting through a decision, and events from other callers while it waits; the three concurrency modes and the `Channel` inbox (bounded and unbounded); SALCH0208–0209, SALCH0401–0402. | A `Pipe` sample with the ordinary read loop stops reading while a decision is pending and resumes at the right byte; a disconnect event cancels a pending decision; cancellation tests. |
| **M5** Runs and performance | `[Run]` with `SearchValues`/scalar fallback; pooled continuations; full benchmark suite; AOT sample; SALCH0701. | Every §9 target met. |
| **M6** Preview release | Diagrams; `Plan`; docs for every diagnostic; `1.0.0-preview.1` package. | Published preview; the TNC migration spec written against it. |

## 14. Open questions

1. **Minimum SDK for consumers** — proposed: .NET 8 SDK (Roslyn 4.8). Newer Roslyn APIs would raise it.

## 15. Risks

| Risk | Mitigation |
|---|---|
| Generator complexity and debuggability. | Generated code written to be read (one method per move, comments naming the declaration); snapshot tests; the reference interpreter as an oracle. |
| Choosing plugins at compile time is a large change for TNC users. | It is TNC 4.0; the migration guide maps every plugin; an app can declare several machines. |
| Declarations cross assembly boundaries as metadata only. | Attribute arguments must be constants (`typeof`, literals); declaration-site analyzers catch shape errors where they are written. |
| Whole-program generation cost grows with plugin count. | Incremental pipeline keyed per module; the §9 generator target is a CI check. |
| `SearchValues` is `net8.0`+. | Scalar fallback for `netstandard2.0`, covered by the same run tests. |

# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added
- [`CONTRIBUTING.md`](CONTRIBUTING.md): how to build the repository, where each kind of test belongs, and what a
  change has to do before it can merge. Issue and pull request templates go with it, and
  [`docs/releasing.md`](docs/releasing.md) now states that a release fixing a publicly known vulnerability names
  it in the changelog. Together these are the [OpenSSF Best Practices](https://www.bestpractices.dev/) criteria
  the repository did not yet meet.

### Added
- `TransitionDefinition.Outcomes`: a decision's outcome cases and the state each one moves to, as
  `OutcomeDefinition`. A decision has no target — which one it takes is not known when the trigger arrives — so
  without these the states only a decision reaches were named by nothing in `MachineDefinition`. Purely additive;
  the existing constructor still works and makes a transition with no outcomes.

### Fixed
- **A decision's outcomes were missing from the generated diagrams.** `Mermaid` and `Dot` drew a decision as one
  self-arrow, so a state only an outcome reaches had no arrow into it and read as unreachable — the door sample's
  `Unlocked`, whose only way in is a decision, among them. A decision is now one arrow per outcome, labelled
  `<trigger> decide / <Outcome>`.
- **The generated `Mermaid` constant did not always parse.** Mermaid reads a bare `state X` written immediately
  before a nested `state Y {` as a single state name and refuses the whole diagram, which is what happened to any
  machine with a leaf sibling ahead of a composite one — the telnet machine the documentation uses among them.
  Composite children are now written first. `Dot` was never affected.

### Changed
- [Examples](docs/guides/examples.md) opens each machine with a diagram of its own: every state, every parent,
  every `[Initial]` child and every transition, each arrow labelled with its trigger and what that step does.
  A test reconciles each diagram against the machine's `MachineDefinition`, so an arrow that is not a transition,
  or a transition with no arrow, fails the build.
- The phone example counts the seconds of a call, so the lesson its prose draws — that holding the call leaves
  `Talking` and loses what `Talking` was counting — is something the example actually shows.

## [1.2.0] — 2026-09-12

There is no 1.1.0.

### Added
- `SALCH0702`: a run that shadows a value one of its ancestors handles. A state's trigger beats an ancestor's,
  which is what makes `[OnAny]` a state's "or else" — but a run takes a stretch of input in one call, so the
  ancestor's value is swallowed by the run rather than ending it, and the machine never leaves the state. Found by
  declaring TelnetNegotiationCore's core framing as a machine, where it ate every newline.

### Fixed
- **The package works on the .NET 8 SDK again.** A dependency update had compiled the generator, the analyzers and
  the code fixes against Roslyn 5.9, and a compiler refuses an analyzer built against a newer Roslyn than its own
  (`CS9057`): every consumer on an older SDK would have got no generated machine at all. They are built against
  Roslyn 4.8 once more, which runs on every compiler from the .NET 8 SDK upwards. This never reached a release.

### Changed
- The documentation says what a stay is where the word is first used, and every other page links to it.
- `eng/check-package.sh` consumes the package on the .NET 8 SDK when one is installed, so the floor is tested
  rather than asserted.

## [1.0.1] — 2026-09-11

Four races in the inbox, each found by review and each now covered by a test that fails without its fix.

### Fixed
- **`Fire` on a machine with an inbox threw instead of waiting.** A pooled `IValueTaskSource` cannot be waited on
  with `GetResult`: on a call another thread was still pumping it threw `InvalidOperationException` and returned a
  queued input to the pool, which then served two callers at once. It blocks, as its documentation said.
- **A stop could strand a caller forever.** `FireAsync` read the status outside the lock, so a call that started
  while the machine was running could join the inbox after `StopAsync` had drained it, and wait for a pump that
  would never come. Joining now happens under the same lock that stops the machine, and a call that loses the race
  throws `MachineNotRunningException`.
- **A bounded inbox leaked room.** An event a pending decision handles is taken out of the inbox by the pump, and
  the slot it held was never released — after `InboxCapacity` of them every `FireAsync` parked forever. Only a
  machine that is `Serialized` with an `InboxCapacity` and has an async decision was affected.
- **Stopping and disposing at once ran the exit actions twice.** The check and the stop are now one claim.
- **A pooled input could be settled after it had been re-used**, completing a different caller's `FireAsync` before
  its trigger had run. Inputs carry a generation, and a decision only settles the caller it actually belongs to.
- **The generator threw on an attribute written with a property it does not have** (`[Exited(Of = typeof(X))]`),
  which suppressed the whole machine and buried the mistake under a `CS0246` for every state. A malformed
  attribute is reported and the rest of the machine is still written.

## [1.0.0] — 2026-09-11

The first release. One package: the runtime under `lib/`, the generator, the analyzers and the code fixes under
`analyzers/`.

### Added
- **States and transitions.** States are structs in a tree (`IRootState`, `IState<TParent>`, `[Initial]`); entering
  one resets its data and leaving one clears it. Transitions are declared apart from the states they connect, so a
  module can add a transition out of a state it did not write. A transition's parameters are checked against the
  tree: a state being left is readable (`in`), one that stays or is entered is writable (`ref`).
- **Triggers.** A value matched exactly or by range, `[OnAny]` for the rest, and typed `IEvent`s with their own
  payload. `[Run]` takes a whole stretch of matching input in one call, scanned with `SearchValues`.
- **Actions.** `[Entered]` and `[Exited]` run after the state commits, and may be async. One that completes
  synchronously allocates nothing.
- **Decisions.** When outside code chooses the outcome, the machine parks in a generated pending state until it
  answers, accepting only what the decision `Handle`s meanwhile. Leaving the pending state cancels the decision;
  a reader that throws becomes a `DecisionFailed` the machine can recover from.
- **Concurrency.** `Checked`, `Unchecked` and `Serialized`, an inbox with an inline fast path for an idle machine,
  and `InboxCapacity` for backpressure. `FireAsync` completes when the input has been processed, so a `Pipe` read
  loop pushes back on the sender; `Fire` is the synchronous overload for callers that know it cannot suspend.
- **Modules.** `[Machine]` with `[Include]` names what an application wants. A library offers a module with
  `[assembly: ExportsModule(typeof(M))]` and a machine takes what its references offer with `[IncludeExported]`,
  optionally `Except` some — so adding a protocol is adding a package reference.
- **Compile-time checks.** Diagnostics for conflicts between modules, role violations, unreachable states and
  uncovered decision outcomes, with code fixes for the ones a fix can write.
- **The machine as data.** A plan layer that answers what a trigger would do without running it, and Mermaid and
  DOT diagrams as generated constants.
- **No reflection**, so AOT- and trim-clean. Targets `netstandard2.0`, `net8.0`, `net10.0` and `net11.0`.
- **Packaging.** MinVer decides the version from the git tag, the package carries a `.snupkg` and Source Link,
  package validation runs on every build, and `.github/workflows/release.yml` publishes to GitHub Packages and
  nuget.org through trusted publishing. See [docs/releasing.md](docs/releasing.md).

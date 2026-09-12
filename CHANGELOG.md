# Changelog

All notable changes to this project are documented here.

## [Unreleased]

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

# Contributing

StateAlchemist takes contributions — a bug report, a diagnostic that fires where it should not, a module for a
protocol, a page of documentation that was wrong. This says how, and what a change has to do before it can be
merged.

There is no contributor licence agreement to sign and no sign-off to add. Everything here is
[Apache-2.0](LICENSE), and opening a pull request offers your change under that licence.

## Reporting something

| What | Where |
|---|---|
| A bug, a wrong diagnostic, generated code that misbehaves | [Open an issue](https://github.com/HarryCordewener/StateAlchemist/issues/new/choose) |
| An idea, or a machine shape the library cannot express | [Open an issue](https://github.com/HarryCordewener/StateAlchemist/issues/new/choose), or ask on [Discord](https://discord.gg/SK2cWERJF7) |
| Something exploitable | **Not an issue.** [`SECURITY.md`](SECURITY.md) — report it privately |

Issues are the archive: every report and its answer stays public and searchable. Discord is for conversation, and
anything decided there that matters ends up in an issue or a pull request.

A bug report is most useful when it carries the declarations that produce it. The machine is generated from what
you wrote, so a state, a transition and the trigger you fired are usually the whole reproduction; the diagnostic
id (`SALCH….`) if there is one, and the target framework, say the rest. Expect an acknowledgement within a week.

## Building it

The [.NET 11 SDK](https://dotnet.microsoft.com/download) builds the repository; `global.json` pins the version and
rolls forward within the feature band. The package itself supports the .NET 8 SDK and upwards, which
`eng/check-package.sh` tests rather than asserts.

```bash
dotnet build StateAlchemist.slnx
dotnet test StateAlchemist.slnx
```

The test suite runs on net8.0, net10.0 and net11.0, in Debug and Release, and takes well under a minute. A change
that touches what the package ships is worth checking end to end, which packs it and uses it from a throwaway
application:

```bash
eng/check-package.sh
```

The documentation's code is included from the compiled samples, so after changing a sample:

```bash
dotnet tool restore
dotnet mdsnippets
```

CI fails on any `.md` that step would have changed.

## What a change has to do

**Every change that adds or fixes behaviour comes with a test.** This is the one rule that is not negotiable. A
bug fix brings a test that fails without it; new functionality brings tests for what it does and for the edges it
introduces. The tests live next to the kind of thing they check:

| Project | What belongs there |
|---|---|
| `tests/StateAlchemist.Model.Tests` | the model and its analyses, with no compiler |
| `tests/StateAlchemist.Generators.Tests` | what the generator emits, and every diagnostic |
| `tests/StateAlchemist.Contracts` | behaviour every machine must have — the suite runs against both the generated machines and the reference interpreter, so the two cannot disagree |
| `tests/StateAlchemist.Generated.Tests` | whole generated machines, the samples, and the documentation |
| `tests/StateAlchemist.Tests` | the runtime, and the public API surface |

A behaviour worth guaranteeing belongs in `StateAlchemist.Contracts`, not in one of the two implementations: that
is what keeps the reference interpreter honest.

**The build is warning-free, and stays that way.** `TreatWarningsAsErrors` is on for every project, so a warning
is a failure. Do not silence one with a `#pragma` where fixing it is possible; where it is not, say why in a
comment next to the suppression.

**Public API changes are deliberate.** `tests/StateAlchemist.Tests/Api/RuntimeApi.verified.txt` is a snapshot of
the whole public surface. A change to it fails the test until the new snapshot is accepted, and the diff is part
of the review. Package validation also diffs every build against the last published release, so removing or
re-signaturing a public member fails the build rather than a consumer's restore.

**Diagnostics are documented.** Every diagnostic in the catalogue has an entry in
[`docs/reference/diagnostics.md`](docs/reference/diagnostics.md) with its exact id, title, severity and message,
and a test fails if one is missing or if the documentation says something the catalogue does not.

**Dependencies are locked.** Every project has a `packages.lock.json` pinning the graph by content hash. After
changing a `PackageReference`, restore with `/p:RestoreForceEvaluate=true` and commit whatever moves. On CI the
lock file is law: a restore that would have to change it fails instead.

**The changelog records it.** Add an entry under `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md) saying what
changed and why it mattered. A fix for something a user could hit says what went wrong, not just what was
repaired.

**Style is what `.editorconfig` says**, and otherwise what the surrounding code does. File-scoped namespaces,
four spaces, `System` usings first. Comments explain why, not what.

## Opening a pull request

Branch from `main`, keep the change to one subject, and describe what it does and how you know it works. A pull
request needs a review from a code owner and a green CI run — the test matrix, the AOT sample, the package check,
the documentation check and CodeQL — before it can merge.

Small, obviously-correct fixes are welcome without discussion first. For anything that changes the public API,
adds a diagnostic, or changes what the generator emits, open an issue first: those have consequences for every
consumer, and it is cheaper to agree on the shape before the code exists.

## Where things are

| | |
|---|---|
| `src/StateAlchemist` | the runtime, and the attributes a declaration uses |
| `src/StateAlchemist.Model` | the machine model, its analyses, and the diagnostic catalogue — no Roslyn |
| `src/StateAlchemist.Generators` | the source generator and the analyzers |
| `src/StateAlchemist.CodeFixes` | the fixes for the diagnostics a fix can write |
| `src/StateAlchemist.Reference` | a reflecting interpreter, used to check the generator against something |
| `samples/` | the machines the documentation is written from |
| `docs/` | concepts, guides and reference |

[`docs/index.md`](docs/index.md) is the way in, and
[`docs/superpowers/specs`](docs/superpowers/specs) records why each decision was made.

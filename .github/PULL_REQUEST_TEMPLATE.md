<!--
  CONTRIBUTING.md has the detail. The short version is below; delete what does not apply.
  Anything exploitable is a security advisory, not a pull request — see SECURITY.md.
-->

## What this does

<!-- And why it mattered. For a fix, what went wrong, not just what was repaired. -->

## How it is known to work

<!--
  Which test fails without this change, and where it lives. A behaviour every machine must have belongs in
  tests/StateAlchemist.Contracts, so the generator and the reference interpreter are both held to it.
-->

## Checks

- [ ] A test that fails without this change
- [ ] `dotnet build StateAlchemist.slnx` is warning-free
- [ ] `dotnet test StateAlchemist.slnx` passes
- [ ] An entry under `## [Unreleased]` in `CHANGELOG.md`
- [ ] `dotnet mdsnippets` run, if a sample changed
- [ ] Lock files updated, if a `PackageReference` changed
- [ ] `RuntimeApi.verified.txt` reviewed, if the public API changed

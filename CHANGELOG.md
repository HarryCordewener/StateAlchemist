# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added
- Exported modules: a library offers a module with `[assembly: ExportsModule(typeof(M))]` and a machine takes what
  its references offer with `[IncludeExported]`, optionally `Except` some — so adding a protocol is adding a
  package reference. New diagnostics `SALCH0108` and `SALCH0109`.
- Packaging: MinVer decides the version from the git tag, the package carries a `.snupkg` and Source Link,
  package validation runs on every build, and `.github/workflows/release.yml` publishes to GitHub Packages and
  nuget.org through trusted publishing. See [docs/releasing.md](docs/releasing.md).
- Repository skeleton, state markers (`IRootState`, `IState<TParent>`, `[Initial]`) and `IEvent`.

# Releasing

For maintainers. One package, `StateAlchemist`: the runtime under `lib/`, the generator and the code fixes under
`analyzers/dotnet/cs`.

## How a version is decided

[MinVer](https://github.com/adamralph/minver) reads it from git, so there is no version number in the repository
to forget to bump:

| Commit | Version |
|---|---|
| tagged `v1.2.3` | `1.2.3` |
| anything else | the next patch as `1.2.4-preview.0.N` |
| a tree with no git history (extracted from the plans) | `0.0.0-alpha.0` |

That is why the release job checks out with `fetch-depth: 0`, and why the workflow does **not** pass `/p:Version` —
it would fight MinVer. A "Verify the packed version matches the tag" step fails the build if the two disagree.

## Cutting a release

1. Move every entry under `## Unreleased` in [`CHANGELOG.md`](../CHANGELOG.md) into a new version heading, and
   commit it to `main`.

   A release that fixes a publicly known vulnerability says so there, by CVE or advisory id, under `### Fixed`.
   A reader has to be able to tell from the changelog alone whether upgrading is a security matter; "no known
   vulnerabilities have been reported against any release" is the reason that section is empty today, not an
   omission.
2. Tag and push:

   ```bash
   git tag v1.0.1
   git push origin v1.0.1
   ```

   The tag triggers `.github/workflows/release.yml`; `workflow_dispatch` with the tag name as input does the same
   thing by hand.
3. After the release lands on nuget.org, set `PackageValidationBaselineVersion` to it in
   `src/StateAlchemist/StateAlchemist.csproj`. Package validation then diffs every later build against that
   published surface and fails on a break.

   For a release that does remove or re-signature public API, drop the property for that build, release, then set
   it to the new version. The API snapshot test (`RuntimeApi.verified.txt`) still records the change.

A tag whose run fails publishes nothing: `build` and `release` need `test` and `package` to pass first. Fix the
commit on `main`, then move the tag — `git push --delete origin vX.Y.Z`, delete the GitHub Release, and tag again.

## What the release workflow does

| Job | |
|---|---|
| `test` | builds Release and runs the whole suite on net8.0, net10.0 and net11.0 |
| `package` | runs [`eng/check-package.sh`](../eng/check-package.sh): packs, checks the contents, then builds and runs a throwaway application that references nothing but the package. The machine is generated, the diagram prints, and the analyzer's `SALCH0002` fails that application's build when it should |
| `build` | refuses a tag whose commit is not on `origin/main`, packs, checks that the version matches the tag and that both Roslyn components are inside, attests build provenance, and uploads the `.nupkg`/`.snupkg` |
| `release` | pushes to GitHub Packages, then to nuget.org, then attaches the `.nupkg`, the `.snupkg` and the provenance bundle to the GitHub release, creating it if the tag has none |

Provenance attestation means a consumer can verify that a given `.nupkg` was built by this workflow, from this
repository, at that commit:

```bash
gh attestation verify StateAlchemist.1.0.1.nupkg --repo HarryCordewener/StateAlchemist
```

The same bundle is attached to the release as `StateAlchemist.<version>.sigstore.json`, so the proof travels with
the file rather than only living in GitHub's attestation store. [`SECURITY.md`](../SECURITY.md) says how to report
anything wrong with it.

## Authentication

There are **no long-lived NuGet API keys in this repository**: nothing to leak, rotate or expire. Two
mechanisms, both short-lived:

- **GitHub Packages** uses the workflow's own `GITHUB_TOKEN`.
- **nuget.org** uses [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing):
  `NuGet/login@v1` exchanges the workflow's OIDC token for a key that lasts minutes. It requires a
  trusted-publishing policy on nuget.org for the `harrycordewener` account naming this repository and the
  `Release` workflow. Without that policy the push step fails with an authentication error.

## What 1.0.0 cost

The first tag failed: CI built only Debug, so the release workflow was the first build that ever compiled Release,
and an allocation test that bounded a cost relative to a Release-only zero failed there. CI's matrix now covers
both configurations, which is what keeps a tag from being the first place a build is tried.

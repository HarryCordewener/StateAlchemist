# Security policy

## Supported versions

The latest released version is supported. StateAlchemist is a single package, published to
[nuget.org](https://www.nuget.org/packages/StateAlchemist); fixes go into the next release rather than into patches
of older ones.

| Version | Supported |
|---|---|
| 1.0.x | yes |

## Reporting a vulnerability

Report privately through GitHub:
[**open a security advisory**](https://github.com/HarryCordewener/StateAlchemist/security/advisories/new). That
reaches the maintainers without the report being public, and is the only channel to use for something exploitable.
Please do not open a public issue for it.

Expect an acknowledgement within a week. What happens next depends on what it is: a fix with a released version, an
explanation of why it is not a vulnerability, or a question if the report needs more to reproduce. You will be
credited in the advisory unless you would rather not be.

Anything that is not a vulnerability — a wrong diagnostic, generated code that misbehaves, a crash in the
generator — belongs in a public [issue](https://github.com/HarryCordewener/StateAlchemist/issues), where it is
easier to discuss.

## What counts

StateAlchemist is a compile-time library: the generator, the analyzers and the code fixes run inside the compiler,
and the runtime it ships is a small amount of code your own machine calls. Reports worth making privately include:

- Generated code that is memory-unsafe, or that races in a way the concurrency mode promises it will not.
- Anything that lets a declaration in one assembly change what is generated in another beyond the documented
  module handshake (`[Include]`, `[assembly: ExportsModule]`, `[IncludeExported]`).
- A supply-chain problem in what is published: the package contents, the release workflow, or the provenance
  attestation.

A machine that behaves differently from its declarations is a correctness bug, and a public issue is the right
place for it.

## How releases are made

There are no long-lived publishing keys. Releases are built and published by the
[release workflow](.github/workflows/release.yml) from a tag on `main`, pushed to nuget.org through
[trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing), and carry an
[attestation](https://docs.github.com/en/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds)
of the workflow and commit that built them. [`docs/releasing.md`](docs/releasing.md) has the detail.

Verify a downloaded package with the GitHub CLI:

```bash
gh attestation verify StateAlchemist.1.0.1.nupkg --repo HarryCordewener/StateAlchemist
```

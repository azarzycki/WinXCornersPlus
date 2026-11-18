# Releasing WinXCornersPlus

Releases are fully automated by [`.github/workflows/release.yml`](../.github/workflows/release.yml).
Nobody edits a version number to release, nobody builds on their own machine, and nothing is committed
back to the repository.

## In short

* **A release happens when a change to the app is merged into `master`.** Specifically, a push to
  `master` that touches `src/**`, `Directory.Build.props`, `build/release/**` or the release workflow.
  Documentation-only or test-only merges do not release.
* The version is computed from git tags (`vMAJOR.MINOR.PATCH`), the commit messages since the last
  release and the `<Version>` in `src/WinXCorners.App/WinXCorners.App.csproj`.
* The workflow tests, builds, packages and publishes a GitHub Release with
  `WinXCornersPlus-<version>-win-x64.zip`, `SHA256SUMS.txt` and a signed build-provenance attestation.

## How the version is chosen

Git tags are the single source of truth. The csproj keeps one `<Version>` (1.5.0 today): it is the
version of local/dev builds, the first release, and a floor for every later release.

1. Only tags of the form `vMAJOR.MINOR.PATCH` (optionally `-prerelease`) count. Everything else (`v1`,
   `v1.2`, `vnext`, `v01.2.3`, `V1.2.3`, `v1.2.3+build`, components above 65534) is ignored, never an
   error. Versions are compared numerically: `v1.10.0` is newer than `v1.9.0`.
2. Prerelease tags (`v1.6.0-rc.12`) are ignored when looking for the latest release.
3. **No release tag yet:** the release is the csproj `<Version>` itself. The first release is **1.5.0**.
4. **Otherwise** the latest stable tag is bumped according to the commits since that tag
   ([Conventional Commits](https://www.conventionalcommits.org/)):
   * **major** if any commit subject has a `!` before the colon (`feat!: ...`, `refactor(settings)!: ...`)
     or any line of a message starts with `BREAKING CHANGE:` (or `BREAKING-CHANGE:`, upper case);
   * else **minor** if any commit subject starts with `feat:` or `feat(scope):`;
   * else **patch**. Most commits here are not in Conventional Commits form, so **patch is the normal
     outcome**, including when there are no commits at all since the tag (a manual run).
5. A manual run can force `patch`, `minor` or `major` instead of inferring it (it does not apply to the
   first release, which is always the csproj version).
6. **If the csproj `<Version>` is higher than the result, the csproj version wins.** This is how a
   maintainer deliberately jumps ahead (for example to 2.0.0).
7. A prerelease is the version computed above plus `-rc.<workflow run number>`.
8. If the resulting tag already exists, the run fails instead of re-releasing.

| Latest stable tag | Commits since                                   | csproj  | Next release  |
|-------------------|-------------------------------------------------|---------|---------------|
| (none)            | anything                                        | 1.5.0   | 1.5.0         |
| v1.5.0            | `Fix tray icon`, `Update README`                | 1.5.0   | 1.5.1         |
| v1.5.1            | `fix: x`, `feat(tray): new menu`                | 1.5.0   | 1.6.0         |
| v1.6.0            | `fix: x` with a `BREAKING CHANGE:` footer       | 1.5.0   | 2.0.0         |
| v1.9.0, v1.10.0   | `Fix crash`                                     | 1.5.0   | 1.10.1        |
| v1.6.0            | `Fix crash`                                     | 2.0.0   | 2.0.0         |
| v1.6.0, v1.7.0-rc.4 | `feat: x`, prerelease run #31                 | 1.5.0   | 1.7.0-rc.31   |

The rules live in one pure, unit-tested PowerShell module,
[`build/release/ReleaseTools.psm1`](../build/release/ReleaseTools.psm1) (`Get-NextReleaseVersion`);
[`Get-ReleaseVersion.ps1`](../build/release/Get-ReleaseVersion.ps1) only gathers the facts from git.
The computed version, the previous tag and the reason are shown in the job summary of the
*Compute version* job.

The build stamps all four version properties on the command line (`Version` and
`InformationalVersion` = the SemVer version, `AssemblyVersion` and `FileVersion` = `X.Y.Z.0`), so
whatever the csproj says otherwise does not matter, and the package step **fails** unless the
published `WinXCornersPlus.exe` and `.dll` carry exactly that version. The About box shows the
informational version (without the `+commit` suffix the SDK appends).

## What the workflow does

`permissions: {}` at the top; each job gets only what it needs. Jobs run in this order, each only if the
previous one succeeded:

| Job | Runner | What | Permissions |
|---|---|---|---|
| Release script tests | ubuntu + windows | Pester tests of `build/release` | `contents: read` |
| Gate | (CI) | the whole CI workflow (`ci.yml`: Core tests with coverage gate on two OSes, app build) | `contents: read` |
| Compute version | ubuntu | full clone with tags; decides dry run or real; computes the version | `contents: read` |
| Package | windows-2025 | `dotnet publish src/WinXCorners.App -c Release -r win-x64 --self-contained false` with the version and `ContinuousIntegrationBuild=true`; verifies the stamp; zips; writes `SHA256SUMS.txt`; uploads both as the artifact `WinXCornersPlus-<version>-win-x64` | `contents: read` |
| Check artifact and write release notes | ubuntu | downloads the artifact, re-checks the checksum, renders the release notes (previewed in the job summary) | `contents: read` |
| Publish | ubuntu | **real releases only**: re-checks the checksum, refuses an existing tag, creates the provenance attestation, creates tag `v<version>` on the built commit and the GitHub Release | `contents: write`, `id-token: write`, `attestations: write` |

The zip contains `WinXCornersPlus.exe`, its DLLs, `*.deps.json`, `*.runtimeconfig.json` and
`LICENSE.txt`; never `.pdb` files or a `settings.json`. The release body states the SHA-256, how to
verify it, that the build is not code-signed and that it needs the .NET 10 Desktop Runtime; GitHub's
generated change list is appended below.

Releases never run in parallel on a branch and a running release is never cancelled
(`concurrency: release-<ref>`, `cancel-in-progress: false`). The tag and release are created with the
workflow's `GITHUB_TOKEN`, which by GitHub's design does not trigger further workflow runs, and nothing
is pushed back, so the workflow cannot loop.

## How to ...

### ... release

Merge to `master`. That's it. Watch *Actions > Release*; the release appears under *Releases* when the
Publish job is done.

### ... force a minor or major bump

Either write the commit (or the squash-merge title) in Conventional Commits form:

* `feat: add per-monitor corners` -> minor
* `feat!: drop Windows 10 support`, or a body line `BREAKING CHANGE: settings format changed` -> major

or run the workflow manually: *Actions > Release > Run workflow* on `master`, choose `bump` = `minor`
or `major`, untick **dry_run**. To jump to a specific version (e.g. 2.0.0), raise `<Version>` in the
csproj to 2.0.0 and merge: the higher csproj version wins.

### ... cut a prerelease

*Actions > Release > Run workflow* on `master`, tick **prerelease**, untick **dry_run**. The result is
e.g. `v1.6.0-rc.57`, published as a GitHub *pre-release*. Prerelease tags never count as the "latest
release", so the next normal release is still computed from the last stable tag.

### ... do a dry run

* Push to any `hardening/**` branch (touching the paths listed above): always a dry run.
* Or *Run workflow* with **dry_run** ticked (the default). On any branch other than `master`, a manual
  run is a dry run even if you untick it.

A dry run does everything except the attestation, the tag and the release: tests, gate, version
(see the job summary), build, stamp check, zip, checksum, artifact download and release-notes preview.
Download the artifact from the run page to inspect the zip.

### ... verify a download (users)

Each release states the SHA-256 of the zip and ships `SHA256SUMS.txt`. In PowerShell:

```powershell
(Get-FileHash .\WinXCornersPlus-1.5.0-win-x64.zip -Algorithm SHA256).Hash.ToLower()
```

must equal the published hash. To verify that the zip was built by this repository's release workflow
from a specific commit (build provenance, signed through Sigstore):

```
gh attestation verify WinXCornersPlus-1.5.0-win-x64.zip --repo <owner>/WinXCornersPlus
```

## What is NOT done

* **No code signing.** The exe is not Authenticode-signed, so SmartScreen/UAC show "Unknown
  publisher" and new releases may get a "Windows protected your PC" warning until they build
  reputation. The checksum and the attestation prove integrity and origin, but Windows does not check
  them. Signing would take: a code-signing certificate (an OV/EV certificate from a CA, or a service
  such as Azure Trusted Signing or SignPath's free OSS programme); its credentials stored as an
  environment secret behind a protected `release` environment with required reviewers; a signing step
  in the Package job between `dotnet publish` and the zip (sign `WinXCornersPlus.exe` and the DLLs with
  `signtool`/the service's action, with an RFC 3161 timestamp); and a `Get-AuthenticodeSignature` check
  next to the version-stamp check.
* No installer, no self-contained build, no x86/arm64 build, no auto-update channel.
* No changelog file: the release notes are GitHub's generated list of merged PRs and commits.

## One-time repository settings

* The publish job requests its own `contents: write`, so the repository's default workflow permission
  can stay read-only. If an organisation policy caps `GITHUB_TOKEN` at read-only, releases will fail at
  the Publish step.
* Artifact attestations are available for public repositories on every plan (private repositories need
  GitHub Enterprise Cloud). No setting is needed.
* If tag protection or a ruleset restricts creating `v*` tags, allow GitHub Actions to create them.
* Recommended: protect `master` so that only reviewed, CI-green changes are merged (a merge is a release).

## Pinned actions

Third-party actions are pinned to full commit SHAs, with the version in a comment
(`actions/checkout@3d3c42e... # v7.0.1`). To update one, look up the commit of the new tag and replace
both:

```
git ls-remote https://github.com/actions/checkout refs/tags/v7.0.1 'refs/tags/v7.0.1^{}'
```

(use the `^{}` line if present: it is the commit behind an annotated tag). Dependabot can do this
automatically with a `github-actions` entry in `.github/dependabot.yml`. The CI workflow (`ci.yml`)
still uses major-version tags (`@v4`); pin it the same way.

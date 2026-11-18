# Releasing WinXCornersPlus

Releases are built by [`.github/workflows/release.yml`](../.github/workflows/release.yml). Nobody edits a
version number to release, nobody builds on their own machine, and nothing is committed back to the
repository.

## In short

* **The maintainer publishes a release by running the workflow by hand:** *Actions > Release > Run
  workflow* on `master`, untick **dry_run**. The version is still computed automatically.
* **Merging into `master` does not publish anything by itself** (unless you opt in, see
  [How to turn on fully automatic releases](#how-to-turn-on-fully-automatic-releases)). It runs a *dry
  run*: everything is built and tested, the files are uploaded as workflow artifacts and the job summary
  shows the version the next release will get.
* A release has six files: two installers (`WinXCornersPlus-<version>-x64.msi`, `-arm64.msi`), two
  portable zips (`WinXCornersPlus-<version>-win-x64.zip`, `-win-arm64.zip`), `SHA256SUMS.txt` over all
  four, and a signed build-provenance attestation for each. Which file is for whom is explained in the
  release notes and in [INSTALLING.md](INSTALLING.md).
* Releases are **not code-signed** yet. The pipeline is ready for it: see [Code signing](#code-signing).

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
whatever the csproj says otherwise does not matter, and the build job **fails** unless the
published `WinXCornersPlus.exe` and `.dll` carry exactly that version. The About box shows the
informational version (without the `+commit` suffix the SDK appends).


The installer's ProductVersion is the same `X.Y.Z` (Windows Installer versions are numeric, so a release
candidate `X.Y.Z-rc.N` and its final release share it; the final one replaces the candidate in place).
Major and minor must stay at or below 255 for an MSI; the build fails otherwise.

## Dry run or real release

The decision is made in one place, `Test-IsRealRelease` in `ReleaseTools.psm1`, and shown at the top of
the *Compute version* job summary:

| Event | Branch | Result |
|---|---|---|
| Manual run (*Run workflow*), **dry_run** unticked | `master` | **real release** |
| Manual run, **dry_run** ticked (the default) | any | dry run |
| Manual run | any other branch | dry run, even with **dry_run** unticked |
| Push (merge) | `master`, repository variable `RELEASE_ON_PUSH` is exactly `true` | **real release** |
| Push (merge) | `master`, `RELEASE_ON_PUSH` unset or anything else | dry run |
| Push | `hardening/**` | dry run |

The workflow has no pull-request trigger at all. A push only starts it when it touches `src/**`,
`Directory.Build.props`, `build/release/**`, `installer/**` or the workflow itself.

A dry run does everything except the code signing, the attestation, the tag and the release: script
tests, the CI gate, the version, both builds with their smoke tests, zips, MSIs, the installer tests on
both architectures, checksums and the release-notes preview (in the summary of *Checksums and release
notes*). Download the artifact `WinXCornersPlus-<version>` from the run page to inspect the files.

### How to turn on fully automatic releases

If you want every merge into `master` that changes the app to publish a release on its own:
*Settings > Secrets and variables > Actions > Variables > New repository variable*, name
`RELEASE_ON_PUSH`, value `true`. Delete the variable (or set anything else) to go back to publishing by
hand. Before you turn it on, protect `master` so that only reviewed, CI-green changes are merged, because
every such merge then becomes a release.

## What the workflow does

`permissions: {}` at the top; each job gets only what it needs. Third-party actions are pinned to commit
SHAs.

| Job | Runner | What | Permissions |
|---|---|---|---|
| Release script tests | ubuntu + windows | Pester tests of `build/release` (version rules, signing plan, release notes, and a static test of this workflow's gating) | `contents: read` |
| Gate | (CI) | the whole CI workflow (`ci.yml`). Runs in parallel with the packaging; **publishing needs it to pass** | `contents: read` |
| Compute version | ubuntu | full clone with tags; dry run or real; the version; whether this run code-signs | `contents: read` |
| Build and smoke-test | `windows-2025` (x64), `windows-11-arm` (arm64) | `dotnet publish -r win-<arch> --self-contained false` with the version stamped; checks the stamp; stages the payload (no `.pdb`, no `settings.json`, plus `LICENSE.txt`); **starts the exe on that runner** as a temporary standard user (the runners are elevated, and an elevated first start stops at the approval dialog): it must be a native image of that architecture, still running after 6 s, running natively with the .NET runtime loaded; then it is stopped and the user and its profile are deleted | `contents: read` |
| Code signing - program files | windows | **real releases with a signing provider only**: signs the `.exe` and `.dll` files, verifies every signature | `contents: read`, `actions: read`, `id-token: write` |
| Zip and MSI | matching runner per arch | zips the payload; builds the MSI with WiX (restored from NuGet) and reads it back (name, publisher, version, platform, UpgradeCode, per-machine, file list); builds two test-only MSIs (a higher version; an unmeetable .NET prerequisite) | `contents: read` |
| Code signing - installer | windows | **real releases with a signing provider only**: signs the MSIs, verifies | as above |
| Installer tests | matching runner per arch | the checks below, every `msiexec` run logged with `/l*v`; logs uploaded as `installer-test-logs-<arch>` | `contents: read` |
| Checksums and release notes | ubuntu | collects the final (signed, if signing ran) files, writes `SHA256SUMS.txt`, re-checks it, renders the notes | `contents: read` |
| Publish | ubuntu | **real releases only, and only if the gate passed**: re-checks the checksums, refuses an existing tag, attests all four files, creates tag `v<version>` on the built commit and the GitHub Release | `contents: write`, `id-token: write`, `attestations: write` |

32-bit x86 is deliberately not built (Windows 11 has no 32-bit edition). The builds are
framework-dependent; users need the .NET 10 Desktop Runtime ([INSTALLING.md](INSTALLING.md)).

### Installer tests

`installer/Test-Installer.ps1` runs on a fresh runner of each architecture (it installs software and
creates a local user, so never run it on your own PC):

1. **Install**: `msiexec /i … /qn` exits 0; the payload files are in `C:\Program Files\WinXCornersPlus`;
   the exe is a native image of the package's architecture; the Start-menu shortcut points to it; one
   Add/Remove Programs entry with the right version, publisher and link; no Run entry, no HKLM key, no
   `PATH` change, app not started.
2. **Protected folder** (the reason the installer exists): no write-type Allow rule for Everyone,
   Authenticated Users, Users or INTERACTIVE on the folder or the exe/dll; owner is Administrators,
   SYSTEM or TrustedInstaller; and the installed app's own `InstallLocationProbe.IsWritableByNonAdministrators`
   (called through a small helper that loads the installed assembly) returns `false` (and `true` for a
   control folder that Users may modify).
3. **Run**: the installed app starts as a standard user (it finds the .NET runtime), stays alive, runs
   natively, and exits with code 0 when sent what the installer sends.
4. **Upgrade while running**: a higher-version MSI installs over the first with the app running: exit 0
   (not 3010, no `ReplacedInUseFiles`), one entry with the new version, the old process is gone and was not
   restarted. (On the runner the app runs as a different user than `msiexec`, so it is the 10-second
   termination fallback that ends it there; step 3 shows that the close messages themselves work.)
5. **Downgrade**: installing the older MSI again fails (1603) with the downgrade message.
6. **Uninstall while running**: after creating `HKLM\SOFTWARE\WinXCornersPlus`, the Run value and a
   `settings.json`: exit 0; folder, shortcut, entry, HKLM key and Run value are gone; `settings.json` stays.
7. **No runtime**: an MSI whose prerequisite check asks for .NET 99 (standing in for a machine without
   .NET 10) stops with exit 1603 and the prerequisite message, and installs nothing.

The installer itself is `installer/Package.wxs` (WiX v6); its design is explained in comments there and
in [INSTALLING.md](INSTALLING.md). The `UpgradeCode` must never change.

## How to ...

### ... release

*Actions > Release > Run workflow*, branch `master`, untick **dry_run**, *Run workflow*. Watch the run;
the release appears under *Releases* when *Publish* is done. (Or turn on
[fully automatic releases](#how-to-turn-on-fully-automatic-releases).)

### ... force a minor or major bump

Either write the commit (or the squash-merge title) in Conventional Commits form:

* `feat: add per-monitor corners` -> minor
* `feat!: drop Windows 10 support`, or a body line `BREAKING CHANGE: settings format changed` -> major

or choose `bump` = `minor` or `major` in the manual run. To jump to a specific version (e.g. 2.0.0),
raise `<Version>` in the csproj to 2.0.0 and merge: the higher csproj version wins.

### ... cut a prerelease

Manual run on `master`, tick **prerelease**, untick **dry_run**. The result is e.g. `v1.6.0-rc.57`,
published as a GitHub *pre-release*. Prerelease tags never count as the "latest release".

### ... do a dry run

Push to a `hardening/**` branch, merge to `master` (without the opt-in), or run the workflow manually with
**dry_run** ticked (the default).

### ... verify a download (users)

The release notes list the SHA-256 of every file and `SHA256SUMS.txt` has them too. In PowerShell:

```powershell
(Get-FileHash .\WinXCornersPlus-1.5.0-x64.msi -Algorithm SHA256).Hash.ToLower()
```

must equal the published hash. To verify that a file was built by the release workflow of this repository
from a specific commit (build provenance, signed through Sigstore), with the GitHub CLI:

```
gh attestation verify WinXCornersPlus-1.5.0-x64.msi --repo Asteski/WinXCornersPlus
```

(The release notes print this command with the repository that actually built the release.)

## Code signing

Today the files are **not code-signed**: SmartScreen may warn about new releases and the UAC prompt shows
*Unknown publisher*. The checksums and attestations prove integrity and origin, but Windows does not check
them. The workflow already contains a complete, optional signing stage that switches itself on once the
repository is configured; until then every run says "Not code-signed" in its summary and release notes.

### How the signing stage works

* *Compute version* decides the provider (`Get-CodeSigningPlan` in `ReleaseTools.psm1`): signing happens
  only when the repository variable `CODE_SIGNING_PROVIDER` is `signpath` or `azure` **and** the run is a
  real release. Dry runs and pull requests never sign (the workflow has no pull-request trigger, and the
  signing jobs check for it as well). If a provider is selected but one of its settings is missing, a real
  release **fails** rather than silently shipping unsigned files.
* *Code signing - program files* signs `WinXCornersPlus.exe` and the DLLs **before** they are zipped or
  put into an MSI; *Code signing - installer* signs the two MSIs. Both run in their own jobs, so the
  signing credentials are never on a runner that restores packages or compiles. Each verifies that every
  `.exe`/`.dll`/`.msi` has a valid, timestamped Authenticode signature (for SignPath: issued to SignPath
  Foundation) and fails otherwise; the packaging job checks the program files again.
* Checksums and attestations are computed over the final, signed files, and the release notes then name
  the signer.
* `build/release/ReleaseWorkflow.Tests.ps1` statically checks this gating on every run.

### Option 1 (recommended): SignPath Foundation, free for open source

The SignPath Foundation issues a code-signing certificate to itself and signs, through SignPath.io, the
releases of open-source projects it has accepted. Cost: **free**. Publisher shown by Windows: **SignPath
Foundation** (not the project or the maintainer). Each release is approved by a project approver in
SignPath before it is signed.

Their conditions, roughly: an OSI-approved licence (MIT: yes), no proprietary components (none), the
applicants are the maintainers and owners of the repository, multi-factor authentication for everyone
involved, a fully automated build on GitHub-hosted runners that SignPath can tie to the repository (this
workflow), and a published code-signing policy ([CODE-SIGNING-POLICY.md](CODE-SIGNING-POLICY.md), which
already contains the required sentence "Free code signing provided by SignPath.io, certificate by SignPath
Foundation").

What the repository owner does once:

1. Fill in the `[placeholder]`s in [CODE-SIGNING-POLICY.md](CODE-SIGNING-POLICY.md) (committers and
   reviewers, approvers, a contact), remove its status note, and link the page from the README.
2. Turn on multi-factor authentication for every maintainer on GitHub.
3. Apply at <https://signpath.org/> (the SignPath Foundation's open-source programme) with the repository
   URL, and follow their on-boarding: install the SignPath GitHub App on the repository, create the project
   in SignPath with a signing policy (e.g. `release-signing`) whose approvers are those in the policy, and
   two artifact configurations from `build/release/signpath/`: `app-binaries.xml` (program files) and
   `installer.xml` (MSI).
4. In GitHub, *Settings > Secrets and variables > Actions*:

   | Kind | Name | Value |
   |---|---|---|
   | variable | `CODE_SIGNING_PROVIDER` | `signpath` |
   | variable | `SIGNPATH_ORGANIZATION_ID` | your SignPath organization ID |
   | variable | `SIGNPATH_PROJECT_SLUG` | the project slug |
   | variable | `SIGNPATH_SIGNING_POLICY_SLUG` | the signing policy slug |
   | variable | `SIGNPATH_APP_ARTIFACT_CONFIGURATION_SLUG` | the slug of the `app-binaries.xml` configuration |
   | variable | `SIGNPATH_INSTALLER_ARTIFACT_CONFIGURATION_SLUG` | the slug of the `installer.xml` configuration |
   | secret | `SIGNPATH_API_TOKEN` | an API token of a SignPath CI user that may submit to that policy |

5. Run a release. The signing jobs wait (up to 10 minutes each) for the approver to approve the request in
   SignPath.

### Option 2: Azure Artifact Signing (formerly Trusted Signing)

Microsoft's managed signing service. Cost: about **US$10 per month** (Basic tier). Publisher shown by
Windows: **the maintainer's own (verified) name** or organisation. Individual developers are eligible in
the USA and Canada (organisations in more countries); Microsoft verifies the identity.

What the repository owner does once:

1. Create an Azure subscription, an Artifact Signing account and an identity validation; when it is
   approved, create a certificate profile (Public Trust).
2. Create a Microsoft Entra app registration with a **federated credential** for GitHub Actions (entity
   type *Branch*, `master` of this repository, so no password is stored anywhere), and give it the
   *Certificate Profile Signer* role (named "Trusted Signing Certificate Profile Signer" or "Artifact Signing Certificate Profile Signer" depending on the portal) on the certificate profile.
3. In GitHub, *Settings > Secrets and variables > Actions > Variables*:
   `CODE_SIGNING_PROVIDER` = `azure`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_SUBSCRIPTION_ID`,
   `AZURE_SIGNING_ENDPOINT` (e.g. `https://eus.codesigning.azure.net/`), `AZURE_SIGNING_ACCOUNT_NAME`,
   `AZURE_CERTIFICATE_PROFILE_NAME`. No secrets are needed.
4. Run a release.

## One-time repository settings

* The publish job requests its own `contents: write`, so the repository's default workflow permission
  can stay read-only. If an organisation policy caps `GITHUB_TOKEN` at read-only, releases fail at Publish.
* GitHub-hosted `windows-11-arm` runners are free for public repositories. (A private copy of the
  repository would need them on its plan.)
* Artifact attestations are available for public repositories on every plan. No setting is needed.
* If tag protection or a ruleset restricts creating `v*` tags, allow GitHub Actions to create them.
* Optional: `RELEASE_ON_PUSH` ([above](#how-to-turn-on-fully-automatic-releases)) and the code-signing
  settings ([above](#code-signing)).

## Pinned actions

Every action in `release.yml` is pinned to a full commit SHA with its version in a comment
(`actions/checkout@3d3c42e... # v7.0.1`); the workflow test fails otherwise. To update one, look up the
commit of the new tag and replace both:

```
git ls-remote https://github.com/actions/checkout refs/tags/v7.0.1 'refs/tags/v7.0.1^{}'
```

(use the `^{}` line if present: it is the commit behind an annotated tag).

# Code signing policy

> **Status: not in effect yet.** WinXCornersPlus releases are currently **not code-signed**. This page is
> the policy that will apply once the project's code signing is set up (see
> [RELEASING.md, "Code signing"](RELEASING.md#code-signing)). Until then, verify downloads with the
> checksums and build-provenance attestations described in [INSTALLING.md](INSTALLING.md).
> Maintainer: replace every `[placeholder]` below, then delete this note.

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

## What is signed

Only files built by this repository's release workflow
([`.github/workflows/release.yml`](../.github/workflows/release.yml)) from the source code in this
repository, on GitHub-hosted runners:

* `WinXCornersPlus.exe` and the WinXCornersPlus DLLs, before they are packaged;
* the Windows Installer packages `WinXCornersPlus-<version>-x64.msi` and `-arm64.msi`.

Nothing is signed by hand, on a personal computer, or from outside this repository. Third-party files
are not signed with this project's certificate (the release contains none: the app is framework-dependent
and uses no third-party libraries). Signing happens only for real releases of the default branch, never
for pull requests or test builds, and every signing request is approved by an approver listed below.

## Team roles

| Role | People | Responsibility |
|---|---|---|
| Committers and reviewers | `[placeholder: GitHub user names]` | May change the source code. Every change by someone outside this list is reviewed by a committer before it is merged. |
| Approvers | `[placeholder: GitHub user names]` | Approve each signing request in SignPath, after checking that it comes from the release workflow for the intended version. |

All team members use multi-factor authentication for GitHub and for SignPath.

## Privacy

WinXCornersPlus makes **no network connections**. It does not collect, send or store any data about you
or your computer anywhere except on that computer: its settings are kept in
`%APPDATA%\WinXCornersPlus\settings.json`, approvals for administrator sessions in
`HKEY_LOCAL_MACHINE\SOFTWARE\WinXCornersPlus`, and a crash log, if one is ever written, in
`%LOCALAPPDATA%\WinXCornersPlus`. It opens a web address only when you click a link in its own windows
(About), which opens your browser, and it starts only the programs, links or commands that you assign to
a hot corner yourself.

The installer does not connect to the network either; it only checks whether the .NET Desktop Runtime is
installed on the computer.

## Reporting a problem

If you find a file signed with this project's certificate that you believe was not built from this
repository, or anything else wrong with a signed release, please open an issue at
<https://github.com/Asteski/WinXCornersPlus/issues> or contact `[placeholder: maintainer contact]`.

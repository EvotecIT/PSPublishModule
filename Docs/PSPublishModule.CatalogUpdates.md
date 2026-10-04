# MSI catalog updates

PowerForge prepares and submits WinGet and Microsoft Store updates from an
existing signed release. One product profile connects its release configuration,
Store identity and immutable public downloads. The Store first reservation and
submission, including age ratings, must be completed in Partner Center before
API updates. Associate an Entra application with the required Partner Center
Manager role and store credentials in environment variables, as described in
[Microsoft's API prerequisites](https://learn.microsoft.com/en-us/windows/apps/publish/store-submission-api).

```json
{
  "SchemaVersion": 1,
  "ReleaseConfigPath": "powerforge.release.json",
  "StoreConfigPath": "powerforge.store.submit.json",
  "StoreTargetName": "App.Windows",
  "StoreInstallerUrlTemplate": "https://downloads.example.com/releases/app/{releaseId}/{artifactKey}/download",
  "StoreArtifactKeys": { "x64": "windows-x64-msi", "arm64": "windows-arm64-msi" }
}
```

The release config supplies one enabled WinGet package with MSI selectors.
The Store config supplies a `DesktopInstaller` target and its seller/product
identity. Set `Authentication.TenantIdEnvVar`, `ClientIdEnvVar` and
`ClientSecretEnvVar`, or `AccessTokenEnvVar`. Inline Store secrets are rejected
by the catalog flow. The prepared packages override the target's package path.

Download the published MSI assets, release manifest and checksums on Windows.
Publish those same bytes through the product's existing artifact delivery flow
and record its public immutable release ID. Catalog submission does not upload
or promote artifact-service releases.

```powershell
powerforge release catalog prepare --config ./catalog.json --out ./catalog-1.2.3 `
    --manifest ./downloads/release-manifest.json --checksums ./downloads/SHA256SUMS.txt `
    --asset-root ./downloads --delivery-release '<public immutable release ID>'
powerforge release catalog submit --config ./catalog.json --out ./catalog-1.2.3
powerforge release catalog submit --config ./catalog.json --out ./catalog-1.2.3 --execute
powerforge release catalog status --config ./catalog.json --out ./catalog-1.2.3
```

`prepare` verifies local checksums, Authenticode trust, MSI identity/version and
architecture, maps Store URLs and streams them to check release hashes and
lengths with redirects disabled. WinGet URLs remain those from the release
configuration. `submit` defaults to preflight: it validates the manifests with
the installed `winget` executable and verifies each selected remote installer.
Add `--execute` to invoke the existing wingetcreate and Store API submission
owners. Select `--channel winget`, `store` or `all` (default). Use
`--store-config` to override the profile's Store configuration location.
Add `--require-authentication` to preflight credentials and read Store readiness
without sending packages. Live submission performs this check automatically and
refuses an active Store submission, even when its modules report ready.

Preparation requires a new directory; submission reuses it. Preserve
`catalog-update.json` and all generated files. Their configuration and input
hashes prevent using a changed profile or catalog set with an old receipt.
Completed channels are skipped independently on rerun. The receipt records PR
and Store submission references when available. `status` reads one snapshot of
the WinGet PR and Store submission; it does not submit, poll continuously or
rebuild installers. Submitted means the client accepted the update, rather than
proving catalog publication. Microsoft validation/certification still applies.

An `Attempting` receipt means the remote outcome may be uncertain. Check the
exact package version, URLs and remote submission before adopting a result:

```powershell
powerforge release catalog reconcile --config ./catalog.json --out ./catalog-1.2.3 `
    --channel winget --reference 'https://github.com/microsoft/winget-pkgs/pull/123' --confirm-reconciled
```

For Store reconciliation use its submission ID and the same Store configuration.
Only after confirming that no submission exists may an operator use
`--reference none --confirm-reconciled` to permit another attempt. Reconciliation
records an operator assertion; it does not prove remote installer qualification.
Never delete a receipt or prepare a second directory merely to bypass an
uncertain attempt. Changed listing, age ratings or app behavior requires a
separate listing review; this flow updates only package metadata.

## GitHub Actions

Call `.github/workflows/powerforge-catalog-update.yml` at a reviewed immutable
PowerForge commit from a product's `workflow_dispatch` workflow. Supply the
profile path, published release tag, delivery release ID, environment name and
optional metadata filenames. `execute: false` is the default. Configure the
environment's approval policy and the four credential secrets named above
(WinGet uses `WINGET_CREATE_GITHUB_TOKEN`). The workflow downloads pinned GitHub
CLI, WinGet and WinGetCreate releases into task scratch, checks their SHA-256
digests and execution, and removes them after the run. It does not register AppX
packages or change the runner's machine configuration. The Windows x64 runner
must support these tools' native and .NET runtime prerequisites.

Set `verify-authentication: true` with `execute: false` to check publishing
credentials and Store API readiness without creating a submission. This does
not confirm that a version has no previous submission or enable replay.

The workflow downloads MSI assets and metadata from the selected public GitHub
release on every run. It restores progress only from the same caller workflow,
repository, dispatch event and branch, verified through GitHub's run API.
Preparation rechecks signatures and release identity and requires restored
installer data and generated files to match the selected signed release.
Locally, use `prepare --resume-from <previous-directory>` with a new output
directory to perform the same requalification while preserving channel progress.
Before a
live command it reserves the selected channels and uploads that intent as a
separate artifact; final receipts never overwrite it. Concurrency queues updates
for the same product/tag and automatic replay is disabled after an interrupted
reservation. A reservation key is consumed before each channel mutation.
For an interrupted run, download the latest trusted receipt artifact and use
the local `reconcile`, `submit` and `status` commands against that directory.
Keep the reconciled receipt as the release record. The dispatch workflow stops
on an uncertain archived receipt; downloading and changing a local copy does
not update GitHub's immutable artifact. Complete that release through the local
commands rather than starting another workflow attempt with stale progress.

Receipt artifacts are retained for 90 days. Download and retain them durably
for long-term recovery. Expired or missing receipts require checking remote
state before a live rerun; artifact retention is not a substitute for a release
record. Preparing/signing the release, promoting its public delivery and
submitting catalog updates remain distinct operations with their own authority.

Without confirmed retained history, execution requires the caller's explicit
`confirm-no-prior-submission` input after checking both remote catalogs for the
exact version. This includes a release's first submission and deleted receipts;
a dry run never creates that confirmation. An expired receipt blocks restoration.
Recover and reconcile its progress first, or remove obsolete artifacts only
after remote reconciliation and use the explicit confirmation when no submission
exists. Preserve the latest receipt when a submission already exists.

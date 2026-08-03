# Apple App Release Helpers

PowerForge owns the repeatable Apple release path for native Apple projects:

1. create an `.xcarchive` with `xcodebuild archive`
2. upload that archive to App Store Connect with `xcodebuild -exportArchive`
3. wait for the exact build and report upload/processing failures with stable diagnostic codes
4. prepare metadata, app information, screenshots, TestFlight, review, and public release through explicit actions
5. notarize and assess Developer ID macOS artifacts when the product ships outside the Mac App Store

The helpers live in shared `PowerForge` services first and are exposed through thin
PowerShell cmdlets, so the same release logic can be reused by scripts, tests, CLI,
and future project release pipelines.

## Local Device Deployment

For developer-device smoke testing, use the local deployment cmdlets. They wrap
`xcodebuild build` and `xcrun devicectl` with structured arguments and typed results:

```powershell
Get-AppleDevice

Publish-AppleAppToDevice `
    -ProjectPath '.\Tactra.xcodeproj' `
    -Scheme 'Tactra' `
    -Configuration Debug `
    -Device 'EvoPhone' `
    -BundleIdentifier 'com.evotecit.tactra' `
    -UseBuildMirror `
    -Launch
```

`Publish-AppleAppToDevice` runs the full local loop:

1. optionally mirrors the project root to a local temp folder with `rsync`
2. builds the app with `xcodebuild build`
3. installs the generated `.app` bundle with `xcrun devicectl device install app`
4. optionally launches the bundle with `xcrun devicectl device process launch`

The mirror step is useful for workspaces stored in cloud/file-provider locations where
plain `xcodebuild` can stall. Use `-UseBuildMirror` for that path, or set
`-BuildMirrorPath` when you want a deterministic mirror directory.

The individual stages are also available when scripts need finer control:

```powershell
$build = New-AppleAppBuild `
    -ProjectPath '.\Tactra.xcodeproj' `
    -Scheme 'Tactra' `
    -Device 'EvoPhone' `
    -UseBuildMirror

$install = Install-AppleApp -AppPath $build.AppPath -Device 'EvoPhone'

$bundleId = $install.BundleIdentifier
if (-not $bundleId) { $bundleId = 'com.evotecit.tactra' }

Start-AppleApp `
    -BundleIdentifier $bundleId `
    -Device 'EvoPhone'
```

## Binary Upload Flow

```powershell
Import-Module PSPublishModule -Force

$archive = New-AppleAppArchive `
    -ProjectPath '.\Tactra.xcodeproj' `
    -Scheme 'Tactra' `
    -Configuration Release `
    -Platform iOS `
    -ArchiveRoot "$env:TEMP\tactra-archives"

Publish-AppleAppArchive `
    -ArchivePath $archive.ArchivePath `
    -TeamId '8ZPGZ79T7J' `
    -ExportPath "$env:TEMP\tactra-testflight-upload"
```

`New-AppleAppArchive` derives the generic destination from `-Platform` unless
`-Destination` is supplied. `iPadOS` intentionally maps to `generic/platform=iOS`
because Xcode archives universal iOS/iPadOS apps through the iOS destination.
For a Catalyst archive, use `-Platform macOS -ArchiveVariant MacCatalyst`;
PowerForge emits `generic/platform=macOS,variant=Mac Catalyst` while preserving
the macOS App Store Connect platform.

`Publish-AppleAppArchive` writes a temporary export options plist and passes it to
`xcodebuild -exportArchive` using:

- `destination = upload`
- `method = app-store-connect`
- `signingStyle = automatic`
- `uploadSymbols = true`
- `generateAppStoreInformation = true`

## Preflight Checks

Use the existing read-only App Store Connect cmdlets before or after upload:

```powershell
Get-AppStoreConnectApp -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath -AppId $appId
Get-AppStoreConnectVersion -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath -AppId $appId
Get-AppStoreConnectBuild -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath -AppId $appId
Test-AppleAppReleaseDrift -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath -Path '.\Tactra.xcodeproj' -AppId $appId -Platform iOS
Set-AppStoreConnectVersionBuild -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath -AppId $appId -VersionString '1.0.1' -BuildNumber '5' -Platform iOS
```

## Unified Release Flow

Apple targets live in the same `powerforge.release.json` used for the rest of a
PowerForge release. Keep action flags disabled in the committed file and select one
named action per run. This prevents a status check or metadata update from accidentally
submitting a version.

```json
{
  "$schema": "./Schemas/powerforge.release.schema.json",
  "SchemaVersion": 1,
  "AppleApps": {
    "ProjectRoot": ".",
    "Configuration": "Release",
    "ArchiveRoot": "Artifacts/Apple/Archives",
    "ExportRoot": "Artifacts/Apple/Exports",
    "TeamId": "8ZPGZ79T7J",
    "Archive": false,
    "Upload": false,
    "PrepareDistribution": false,
    "SyncMetadata": false,
    "SyncAppInfo": false,
    "SyncScreenshots": false,
    "ReplaceScreenshots": true,
    "CheckReleaseReadiness": false,
    "DistributeTestFlight": false,
    "SubmitTestFlightBetaReview": false,
    "SubmitForReview": false,
    "ReleaseApprovedVersion": false,
    "Automation": {
      "WriteReceipt": true,
      "ReceiptPath": "build/powerforge/apple/release-receipt.json",
      "PlanReceiptPath": "build/powerforge/apple/release-plan.json",
      "LockPath": "build/powerforge/apple/release.lock",
      "VersionSourcePath": "project.yml",
      "Resume": true,
      "WaitForProcessing": true,
      "ProcessingTimeoutSeconds": 1800,
      "PollIntervalSeconds": 20,
      "MinimumFreeSpaceGB": 20,
      "CleanupBeforeArchive": true,
      "CleanupAfterProcessing": true,
      "ArtifactRetentionDays": 7
    },
    "AppInfoConfigPath": "build/appstore-metadata/app-info.json",
    "GovernanceConfigPath": "build/appstore-governance.json",
    "CheckGovernance": true,
    "ScreenshotConfigPaths": [
      "build/appstore-screenshots/ios.json",
      "build/appstore-screenshots/macos.json"
    ],
    "AppStoreConnectApiKeyPath": ".appstoreconnect/private_keys/AuthKey_ABC123DEFG.p8",
    "AppStoreConnectApiKeyId": "ABC123DEFG",
    "AppStoreConnectApiIssuerId": "00000000-0000-0000-0000-000000000000",
    "Apps": [
      {
        "Name": "Example iOS and iPadOS",
        "BundleId": "com.example.product",
        "AppStoreConnectAppId": "1234567890",
        "Platform": "iOS",
        "ProjectPath": "Product.xcodeproj",
        "Scheme": "Product"
      },
      {
        "Name": "Example Mac Catalyst",
        "BundleId": "com.example.product",
        "AppStoreConnectAppId": "1234567890",
        "Platform": "macOS",
        "ArchiveVariant": "MacCatalyst",
        "ProjectPath": "Product.xcodeproj",
        "Scheme": "Product"
      }
    ]
  }
}
```

Start with a remote-read-only status receipt:

```text
powerforge apple-release Status --config powerforge.release.json --summary --output json
```

Use the same entry point for each transition:

| Action | Result |
| --- | --- |
| `Status` | Reads the exact configured version/build and recommends the next action; it may generate an explicitly configured missing Xcode project locally. |
| `Doctor` | Reads release state plus local topology, embedded-product evidence, metadata ownership, App Review details, age rating, pricing, availability, accessibility, encryption, monetization, webhook coverage, and TestFlight feedback. It does not mutate Apple state. |
| `Version` | Updates the configured XcodeGen version source and chooses one build number above both local state and every configured App Store Connect platform. |
| `Archive` | Creates signed archives without uploading. |
| `Upload` | Archives, uploads, waits for processing, and resumes an exact remote build when possible. |
| `UploadExisting` | Uploads existing archives and uses the same resume/wait behavior. |
| `Prepare` | Creates/updates versions, metadata, app information, build selection, and readiness. |
| `Screenshots` | Validates and syncs configured screenshot sets as a separate, deliberate transition. |
| `TestFlight` | Assigns the processed build to configured groups and testers. |
| `Advance` | Resumes versioning, archive, upload, preparation, metadata, readiness, configured TestFlight distribution, and screenshots only when `SyncScreenshots` is explicitly enabled; then stops before any review or public-release action. |
| `SubmitTestFlightReview` | Submits external TestFlight distribution for Beta App Review. |
| `SubmitAppReview` | Submits a ready App Store version for App Review. |
| `Release` | Publishes a version waiting for developer release. |
| `Cleanup` | Removes stale files only from configured archive/export roots. |

Plan a mutating action before running it:

```text
powerforge apple-release Upload --config powerforge.release.json --plan
powerforge apple-release Upload --config powerforge.release.json --summary --output json
```

For a routine release, set the new marketing version once and let PowerForge choose
the next remote-safe build number:

```text
powerforge apple-release Version --config powerforge.release.json --apple-version 1.6.0 --plan
powerforge apple-release Version --config powerforge.release.json --apple-version 1.6.0 --confirm-apple-action --summary --output json
powerforge apple-release Advance --config powerforge.release.json --plan
powerforge apple-release Advance --config powerforge.release.json --confirm-apple-action --summary --output json
```

`Advance` is intentionally safe to resume. It acquires the configured operation lock,
uses a separate plan receipt, checks the exact version/build remotely, and stops before
`SubmitTestFlightReview`, `SubmitAppReview`, or `Release`.
Screenshot replacement is opt-in during `Advance`. Keep `SyncScreenshots=false` when the
protected `powerforge-apple-screenshots.yml` lane owns capture, approval, and immediate sync.

## Commercial and compliance governance

Use one checked-in governance file per App Store Connect app. The file declares only
facts that a product owner has reviewed; PowerForge never derives prices, countries,
encryption answers, or accessibility claims from source code. The bundled schema gives
editors completion and catches misspelled fields before a release:

```json
{
  "$schema": "../../Schemas/appstore-connect-governance.schema.json",
  "schemaVersion": 1,
  "appId": "1234567890",
  "pricing": {
    "baseTerritoryId": "USA",
    "prices": [
      {
        "territoryId": "USA",
        "appPricePointId": "eyJzIjoi...",
        "startDate": "2026-08-01"
      }
    ]
  },
  "availability": {
    "availableInNewTerritories": false,
    "territories": [
      { "territoryId": "USA", "available": true },
      { "territoryId": "POL", "available": true }
    ]
  },
  "accessibility": [
    {
      "deviceFamily": "IPHONE",
      "supportsVoiceover": true,
      "supportsLargerText": true,
      "publish": true
    }
  ],
  "encryptionDeclarations": [
    {
      "appDescription": "Human-reviewed description of the app and its cryptography",
      "containsProprietaryCryptography": false,
      "containsThirdPartyCryptography": true,
      "availableOnFrenchStore": true
    }
  ],
  "subscriptionGroups": [
    {
      "referenceName": "Pro",
      "localizations": [
        { "locale": "en-US", "name": "Pro" }
      ],
      "subscriptions": [
        {
          "productId": "com.example.product.pro.monthly",
          "name": "Pro Monthly",
          "subscriptionPeriod": "ONE_MONTH",
          "groupLevel": 1,
          "localizations": [
            { "locale": "en-US", "name": "Pro Monthly", "description": "Monthly Pro access" }
          ],
          "prices": [
            {
              "territoryId": "USA",
              "subscriptionPricePointId": "eyJzIjoi...",
              "planType": "MONTHLY"
            }
          ],
          "introductoryOffers": [
            {
              "duration": "TWO_WEEKS",
              "offerMode": "FREE_TRIAL",
              "numberOfPeriods": 1,
              "territoriesFromPlanType": "MONTHLY"
            }
          ],
          "availabilities": [
            {
              "planType": "MONTHLY",
              "availableInNewTerritories": false,
              "territoryIds": [ "USA", "POL" ]
            }
          ]
        }
      ]
    }
  ]
}
```

Accessibility publication is always a distinct reviewed change, including when the
same apply operation creates or updates the declaration facts. A publish-enabled
request cannot make reviewed facts public without that publication effect appearing
in the plan and receipt.

Validate locally, read Apple and write a drift receipt, then apply only after review:

```text
powerforge apple-governance snapshot --app-id 1234567890 --out build/appstore-governance.json --release-config powerforge.release.json
powerforge apple-governance validate --config build/appstore-governance.json
powerforge apple-governance plan --config build/appstore-governance.json --release-config powerforge.release.json --receipt build/governance-plan.json --fail-on-drift --summary --output json
powerforge apple-governance apply --config build/appstore-governance.json --release-config powerforge.release.json --reviewed-plan build/governance-plan.json --confirm --summary --output json
```

`snapshot` bootstraps a declaration from existing Apple state and refuses to overwrite
reviewed configuration unless `--force` is supplied. Review it before committing.
`validate` needs no credentials. `plan` is read-only. `apply` requires `--confirm`
and the exact reviewed Plan receipt through `--reviewed-plan`. Before every mutation,
it regenerates the plan and stops if the remaining work no longer matches that receipt.
If a parent change exposes new child drift, those changes require a new reviewed Plan.
Apply converges one dependency-aware change at a time, replans after every Apple mutation,
and writes a compact receipt by default under `.powerforge/apple/`. It creates and
updates declared resources but never performs implicit deletions. A safety limit
prevents an unexpectedly large change set from running indefinitely.

Use `--summary` for automation and agent-facing output. It reports counts, grouped
resource types, at most ten representative changes or findings, and the full receipt
path; the complete machine-readable plan remains in that receipt for deliberate review.

Introductory offers may declare explicit `territoryIds`, or use
`territoriesFromPlanType` to reuse the same reviewed `MONTHLY` or `UPFRONT` plan
availability without repeating a large storefront list. The protected workflow allows
500 confirmed mutations by default so a first global trial rollout can converge in one
reviewed run; lower `maximum_changes` when a narrower safety boundary is appropriate.

The equivalent PowerShell surface is:

```powershell
Test-AppStoreConnectGovernanceConfig '.\build\appstore-governance.json'

Export-AppStoreConnectGovernance `
    -AppId '1234567890' -Path '.\build\appstore-governance.json' `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath

Get-AppStoreConnectGovernancePlan `
    -ConfigPath '.\build\appstore-governance.json' `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath

Sync-AppStoreConnectGovernance `
    -ConfigPath '.\build\appstore-governance.json' `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
    -Confirm
```

When `GovernanceConfigPath` or `GovernanceConfigPaths` is present, named `Doctor`,
`Prepare`, and `Advance` actions automatically enable `CheckGovernance`. Drift becomes
a stable `APPLE_GOVERNANCE_*` diagnostic in the normal Apple release receipt and blocks
the transition before review. A configured workflow can also set
`CheckGovernance=true` explicitly.

For GitHub-hosted control, call `powerforge-apple-governance.yml` with exact consumer
and PowerForge commit SHAs. `Plan` uses the normal `apple-release` environment and is
read-only. `Apply` always replans first, requires an authorized dispatcher, enters the
separate `apple-release-approval` protected environment, passes the explicit confirmation
flag, and uploads both compact receipts. App Store Connect secrets are declared
individually; the workflow does not inherit the caller's complete secret set.

```yaml
jobs:
  governance:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-apple-governance.yml@<exact-40-character-sha>
    with:
      operation: Plan # change to Apply only after reviewing the plan
      source_ref: ${{ github.sha }}
      powerforge_ref: <exact-40-character-sha>
      config_path: scripts/appstoreconnect-governance.json
      allowed_dispatchers_json: '["authorized-login"]'
```

Apple's API is intentionally asymmetric. PowerForge reports a blocked change when the
published API cannot safely mutate an existing property, such as
`availableInNewTerritories` on an already-created app availability resource. Resolve
that exact item in App Store Connect, then rerun the plan. A blocked receipt is never
reported as successful convergence.

## Reusable GitHub workflows

The reusable workflows build the exact 40-character `powerforge_ref` through the
canonical PowerForge release builder, so consumer repositories do not wait for package
publication or reimplement build logic. The composite action also supports a checked-in
tool lock when a repository prefers downloading a published standalone asset:

```json
{
  "$schema": "https://schemas.evotec.xyz/powerforge.tool.schema.json",
  "schemaVersion": 1,
  "repository": "EvotecIT/PSPublishModule",
  "version": "3.0.80",
  "releaseTag": "PowerForge-v3.0.80",
  "assets": {
    "osx-arm64": {
      "sha256": "<64-character release asset digest>"
    }
  }
}
```

`releaseTag` is optional for compatibility and otherwise defaults to `v<version>`.
Set it when the standalone CLI is published independently from the full module release.

The reusable workflow boundary mirrors the human approval boundary:

- `powerforge-apple-version-pr.yml` runs `Version`, stages only the configured
  version source, and opens a release-ready pull request. It never merges it.
- `powerforge-apple-advance.yml` runs a plan and confirmed resumable `Advance` from
  an exact merged commit. It stops before every review and public-release action.
  Consumers that need source-only dependencies can pass `source_bootstrap_script`;
  the workflow accepts only a tracked, unchanged, non-symlinked `.ps1` file beneath
  that exact checkout and runs it before both the plan and confirmed transition.
  The shared action then rejects an `AppleApps.ProjectRoot` or app project path outside
  that checkout, so bootstrap output cannot redirect protected release inputs to a
  persistent path on the runner.
- `powerforge-apple-approval.yml` accepts only `SubmitTestFlightReview`,
  `SubmitAppReview`, or `Release`, verifies an optional dispatcher allow-list, and first
  publishes a read-only plan from the non-approval environment. The protected job starts
  only after that plan can be inspected, replans after approval, and executes only when
  the exact source, action, observed Apple state, and App Review readiness evidence still
  produce the reviewed SHA-256. The reviewed hash is also passed into the release engine,
  which recomputes and rejects drift immediately before the first Apple mutation.
- `powerforge-apple-monitor.yml` runs scheduled `Doctor` on a trusted self-hosted macOS
  runner, reads its private `~/.appstoreconnect/env` profile only inside that action
  process, retains the compact receipt, and maintains one marker-owned GitHub incident
  until errors and warnings are cleared. The profile and referenced `.p8` file must be
  runner-owned regular files with one link, grant no group, other, or ACL access, and
  must not contain or traverse symbolic links. The monitor accepts only the exact
  default-branch commit that invoked it.
- `powerforge-apple-screenshots.yml` captures from an exact source commit, retains the
  PNG artifact for review, waits at a protected environment, binds the reviewer to
  exact image hashes, and then performs the confirmed screenshot sync.
- `powerforge-apple-screenshot-capture.yml` plus
  `powerforge-apple-screenshot-approve.yml` provide the same exact-byte boundary on
  repositories whose GitHub plan cannot enforce environment reviewers. Capture and
  approval are separate runs; the second run resolves the exact source commit from the
  retained capture artifact, verifies a compact provenance artifact against a successful
  dedicated default-branch capture workflow, rejects an optional mismatched source
  assertion, verifies an allow-listed GitHub actor, and binds both run IDs into the
  manifest before sync.

Callers must pass 40-character commit SHAs for `powerforge_ref` and release
`source_ref`; the workflows reject branches and tags and verify both checked-out
commits. Mutating reusable jobs select protected environments and require explicitly
supplied App Store Connect credentials. The monitor is intentionally different: its
read-only Doctor action uses only the fixed private profile on the self-hosted Mac and
never copies that credential tuple into GitHub secrets, inputs, outputs, artifacts, or
receipts. Runner-local credentials cannot be enabled for Version, Advance, governance,
screenshots, review submission, release, or any other mutating action.
Runner-local mode also rejects any `AppStoreConnectApiKeyPath`,
`AppStoreConnectApiKeyId`, or `AppStoreConnectApiIssuerId` in the tracked release
configuration so repository content cannot override the validated private profile.
Release configs and tool manifests must be tracked, unchanged files beneath the exact
checkout and must not traverse symbolic links or reparse points. Do not add
repository-wide `secrets: inherit`; that would weaken the environment boundary. When
explicit secret content is supplied, the composite action writes the private key to a
permission-restricted temporary file and removes it after every plan or action. Mutating
release workflows, including both screenshot
approve-and-sync variants, share one repository-scoped concurrency group. Monitoring
and screenshot capture have dedicated groups so a long capture cannot cancel a release,
two publication captures cannot overlap, and App Review never observes a partially
replaced screenshot set.

The workflows upload the exact configured plan and actual receipt paths and fail if
a required receipt is absent. Successful logs contain only the short step summary,
while failure logs retain only stable diagnostic codes and actions; private evidence
remains in the receipt artifact. A partially completed
version workflow reuses an identical remote release branch and creates or returns its
open pull request; it refuses to overwrite different remote content.

An iOS/iPadOS app, a companion Watch app, and CarPlay remain one iOS archive lane.
Add another workflow target only for a separately archived store platform, such as
Mac Catalyst or an independently distributed Watch app.

`SubmitTestFlightReview`, `SubmitAppReview`, and `Release` require
`--confirm-apple-action`. `Screenshots` also requires confirmation when
`ReplaceScreenshots=true`. The PowerShell surface uses the same actions:

```powershell
Invoke-PowerForgeRelease `
    -ConfigPath '.\powerforge.release.json' `
    -AppleAction SubmitAppReview `
    -ConfirmAppleAction
```

The receipt is the handoff between people, CI, and agents. It records the target,
version/build, processing and review states, selected-build state, performed/skipped
steps, bounded cleanup, stable diagnostics, the control-plane inventory, and
policy-aware next actions. `readinessChecked=false` means
the read-only status action did not query metadata or screenshot readiness; run
`Prepare` or `Screenshots` for those checks. The persisted receipt uses its configured
project-relative path and never contains API credentials. A rerun with `Resume=true`
checks the exact remote version/build before rebuilding or uploading.

Metadata and screenshot maps can set `UseReleaseVersion=true` instead of committing a
new App Store version id for every release. PowerForge resolves the editable version
for the current release and refuses to bind the map to a different version.

Projects generated by XcodeGen can set `GenerateProjectIfMissing` on a target. PowerForge
then runs `xcodegen generate` from the directory containing `project.yml` before
resolving the local version/build for any action, including `Status`. This keeps
project-generation logic out of consumer scripts while preserving remote read-only
status behavior.

For screenshot-enabled releases, run `Prepare` once after upload to create the
Distribution draft, run the separately reviewed `Screenshots` action, then run
`Prepare` again as the final readiness gate.

Screenshot configs can require an approval manifest. The manifest binds every upload
file to its SHA-256 digest, PNG dimensions, release version, source commit, locale,
device/runtime, appearance, scenario, approver, and approval time. Any changed or
unapproved image fails before App Store Connect mutation. Product repositories remain
responsible for deterministic UI navigation and capture because only the product knows
its useful screens; PowerForge owns validation, approval integrity, upload, delivery
state, and the receipt.

The reusable screenshot workflow standardizes that product-specific boundary. Its
`capture_script` is repository code pinned by `source_ref`; the shared workflow rejects
path traversal and linked paths, verifies that the script is tracked and unchanged at
the exact commit, requires PNG output, retains it before requesting environment approval,
and never accepts a branch or tag as capture evidence. This lets CasaRay keep its rich
simulator routes while Tactra, EasyControlX, and EmailIMO add small capture scripts with
the same approval and upload contract.

After reviewing the captured images, generate the manifest without hand-copying file
hashes or dimensions:

```text
powerforge apple-screenshots manifest \
  --config scripts/appstoreconnect-screenshots-ios.json \
  --capture-provenance build/appstore-screenshots/powerforge-apple-screenshot-provenance.json \
  --expected-repository EvotecIT/MyApp \
  --expected-workflow-ref EvotecIT/MyApp/.github/workflows/apple-screenshots.yml@refs/heads/main \
  --release-config powerforge.release.json \
  --target "Primary iOS App" \
  --approved-by release-owner \
  --allowed-root build/appstore-screenshots \
  --runtime "iOS 26.0" \
  --device "iPhone 17 Pro Max" \
  --theme light \
  --scenario app-store
```

`--capture-provenance` derives the marketing version, exact source commit, Xcode,
runtime, device, theme, scenario, and exact PNG byte inventory from the retained
capture artifact. The selected files must match its path, SHA-256, width, and
height inventory exactly. Explicit `--version`, `--source-commit`, or
capture-metadata options may still be supplied for recovery, but they must match
the provenance document exactly.

The capture workflow therefore requires the exact three-part marketing version;
blank or branch-relative capture evidence cannot be approved. The pinned helper
also requires the retained provenance for every local `Screenshots` action, for
an `Advance` action configured to synchronize screenshots, and for final review
submission or release when screenshot configs are present. Before any mutation, it
re-downloads that artifact and proves that the approval manifests name the same
run, repository, workflow, source commit, version, and complete path-bound PNG
byte and dimension inventory.

For local publication, invoke the command through the reviewed
`scripts/Invoke-PinnedPowerForge.ps1` helper. It requires the exact merged
PSPublishModule commit, a clean consumer `main` equal to `origin/main`, and the
expected GitHub repository. Use a fresh checkout or worktree. Modified, untracked,
and ignored files are rejected because Xcode projects and build phases can otherwise
consume bytes that are absent from the reviewed commit. The only exceptions are
individual screenshot PNGs and provenance files whose paths and bytes match the
retained capture artifact, plus approval manifests whose identity and complete image
inventory validate against that provenance. An unrelated file beside that evidence
still fails closed. Git replacement refs are
also rejected. Tracked symbolic links must resolve through relative targets entirely
inside the consumer checkout, and Git submodules are rejected because their live
worktree bytes are not contained by the consumer commit. Cleanup alone permits an app project already removed by the completed
release while continuing to validate every remaining tracked release input. The helper
re-downloads the named provenance artifact from the successful source-bound GitHub run and compares its exact bytes before
building the CLI into an isolated temporary artifacts directory. This prevents
an editable local provenance copy, a stale ignored binary, or another checkout
from becoming the publication authority. Capture callers must supply the exact
platform runtime represented by their images; simulator captures must name the
simulator runtime rather than the macOS host. The pinned helper intentionally
rejects `UploadExisting` because an ignored prebuilt archive has no reviewed
source or byte provenance; use `Upload` to build and upload from the bound source.
It restores locked packages into a fresh private NuGet cache, builds from the
pinned PSPublishModule commit's tracked-only archive so ignored or untracked files
cannot enter the executable, uses that archive's `global.json` to select the SDK,
and only then executes the built CLI with the consumer as its working directory.
It launches the CLI with a minimal environment, fixed Apple tool resolution, and
only the validated local credential tuple; inherited .NET hooks, profilers, loader
variables, tool overrides, and other process-injection settings do not cross that
boundary. Apple credentials are suspended before any Git or build command runs.
Tracked `DirectDistribution.KeychainProfile` overrides are rejected so `notarytool`
cannot select credentials outside this boundary. The local credential profile, key, and every key-path directory must be owned by
the operator and grant no group, other, ACL, link, or hard-link access. Targeted
commands validate only screenshot maps matching the selected release targets.
GitHub CLI is required only when a retained capture-provenance artifact is used.

In GitHub Actions, `ApprovedBy` names the protected environment boundary—not the
workflow initiator. `InitiatedBy` records who started the run, while
`ApprovalEvidence` links to GitHub's deployment record where the actual environment
review decision is retained. This avoids incorrectly crediting the initiator as the reviewer.

The command writes `Quality.ApprovalManifestPath` when configured, or a sibling
`*.approval.json` file. It runs the same local image quality gates used by upload,
then binds only the selected files. Approval manifest schema 2 also binds the
review to the exact App Store Connect app id and platform, so identical images
cannot be replayed to another product or store destination.
When a reusable screenshot map intentionally leaves `AppId` blank, `--release-config`
resolves and binds exactly one enabled destination for the map platform; `--target`
disambiguates products with multiple destinations. `--app-id` remains available for
recovery tooling that already resolved the destination explicitly.
Every upload also supplies the expected release source commit and requires it to
match the manifest. Direct `Sync-AppStoreConnectScreenshots` recovery runs must pass
that exact commit through `-SourceCommit` when approval manifests are required.

## Apple target modeling

Configure store targets by archive and App Store Connect platform, not by every device
that can run the product:

- A universal iOS archive covers both iPhone and iPad. Do not add a second iPad target
  merely because the app has iPad screenshots.
- Mac Catalyst is a macOS App Store Connect target with
  `ArchiveVariant=MacCatalyst`.
- A companion Watch app shipped inside the iOS archive stays with that iOS target.
- An independently archived Watch app can use a `watchOS` archive target, but
  PowerForge maps it to Apple's `IOS` App Store Connect platform because Apple does
  not expose a separate `WATCH_OS` store-platform value.
- CarPlay is an iOS capability and entitlement, not a separate App Store platform.
  Its scenes and entitlement checks belong in the iOS build and validation lane.

Model every product in `AppleApps.Apps`, including surfaces that are not independent
uploads. The routing fields prevent a widget, Watch app, helper, or development probe
from becoming an accidental release lane:

| Field | Purpose |
| --- | --- |
| `DistributionRoute` | `AppStore`, `TestFlightOnly`, `DirectNotarized`, `EmbeddedCompanion`, or `DevelopmentOnly`. |
| `ProductRole` | Primary app, companion app, extension, embedded executable, or capability-only surface. |
| `ParentTarget` | Names the archive owner for an embedded or development surface. |
| `RequiredEmbeddedBundleIds` | Fails Doctor when the parent project no longer contains a required companion/helper bundle. |
| `Capabilities` | Open-ended product facts such as `Widgets`, `Watch`, `CarPlay`, `AppIntents`, `LiveActivities`, or `AppleIntelligence`. |
| `TestFlightPolicy` | Disables TestFlight or limits a target to internal/external distribution. `Automatic` preserves legacy distribution behavior, but the protected `SubmitTestFlightReview` mutation requires explicit `External` intent. |

Apple does not permit app-record creation through the App Store Connect API. If
`AppStoreConnectAppId` is omitted, PowerForge looks up the exact bundle identifier and
persists the discovered ID in the receipt. Zero matches becomes an onboarding
diagnostic in `Doctor`; mutating actions stop until the app is created in the App Store
Connect website.

## Direct macOS distribution

Set `DistributionRoute` to `DirectNotarized` for a Developer ID lane. `Upload` and
`Advance` then export with `method=developer-id`, submit the one exported `.app`,
`.dmg`, or signed flat `.pkg` with `notarytool --wait`, staple and validate the accepted
ticket, and run the artifact-appropriate `spctl` Gatekeeper assessment. ZIP is not
accepted as the distribution artifact because Apple cannot staple a ticket directly to
a ZIP; `.app` bundles are zipped internally only for submission. The receipt records the
submission ID, notarization state, stapling result, and Gatekeeper result. If submission
is accepted but stapling or assessment fails, a retry reuses the accepted submission and
reruns only local post-processing. Resume requires that exact target to have failed,
matches version and build, and verifies a deterministic SHA-256 of the original artifact;
a successful sibling or changed artifact is never reused. Authentication can use
`DirectDistribution.KeychainProfile` or the same App Store Connect API key supplied to
the release.

Keep privileged helpers and embedded executables as `EmbeddedCompanion` entries under
the direct parent, then list their bundle identifiers in the parent
`RequiredEmbeddedBundleIds`.

## Proactive monitoring

Run `Doctor` on a schedule as well as before a release. The reusable
`powerforge-apple-monitor.yml` workflow checks out exact 40-character source and
PowerForge commits, builds that exact shared source, and runs Doctor on a trusted
self-hosted macOS runner. The action uses only canonical variables from the runner's
permission-restricted `~/.appstoreconnect/env`; optional `ASC_*` compatibility entries
must be exact references to their canonical values. It requires the `.p8` file to remain inside
that same private directory, validates an unencrypted PKCS#8 PEM shape, and keeps all
values process-local. It accepts only the exact default-branch commit that invoked the
workflow and executes only a `powerforge_ref` already merged into PSPublishModule's
default branch, then retains the compact
receipt and maintains one stable GitHub incident. Errors
and warnings open the issue; a clean later run closes only incidents carrying the
PowerForge monitor ownership marker, never an unrelated same-title issue. This catches upload/build
state, review, metadata, screenshot, compliance, observability, and TestFlight feedback
gaps without waiting for an Apple email.

App Store Connect webhooks complement scheduled polling. `AppStoreConnectClient` can
list, create, update, and ping webhooks, while `AppStoreConnectWebhookVerifier`
validates Apple's `x-apple-signature` with fixed-time HMAC-SHA256 comparison before
parsing the event. A receiver should refresh the compact release state for version,
build-upload, Beta App Review, and feedback events; scheduled Doctor remains the
fallback for missed delivery or receiver downtime.

Doctor intentionally reports human attestations instead of guessing them. Age-rating
answers, export-compliance facts, accessibility claims, reviewer credentials, pricing,
territory availability, subscription/IAP policy, and privacy declarations must be
reviewed by the product owner. PowerForge can detect missing or drifting state and stop
publication, but it must not invent legal, commercial, or accessibility claims.

All configured store targets share the marketing version chosen by `Version`. The
build-number resolver checks every configured remote platform before assigning the
next number, so retries do not reuse a build already uploaded by another lane.

## Current Boundary

PowerForge owns archive/upload, direct notarization, bounded build processing waits,
Distribution preparation, metadata and screenshot sync, build selection, TestFlight
distribution, review submission, approved-version release, compact diagnostics, and
local artifact cleanup. Declarative governance adds plan/diff/apply control for app
pricing schedules, territory availability, accessibility declarations, export-
compliance declarations, subscription groups and products, localizations, prices,
introductory offers, and plan availability. Doctor also reads phased release, webhooks, customer reviews, and
beta feedback. Apple still owns processing and review decisions, and a person must
supply and approve every legal, commercial, and accessibility fact before apply.

Keep every mutating action flag disabled in committed consumer configuration. Named
actions override those flags for one run, and the three review/release transitions stay
behind explicit confirmation.

## App Information Metadata

App-level metadata is separate from version localizations in App Store Connect. Use it
for the localized app name, subtitle, and privacy policy URL:

```json
{
  "appId": "6775426723",
  "locale": "en-US",
  "metadata": {
    "name": "Tactra Remote",
    "subtitle": "Premium Home Assistant remote",
    "privacyPolicyUrl": "https://tactra.dev/privacy/"
  }
}
```

Inspect and sync the editable App Information resource with:

```powershell
Get-AppStoreConnectAppInformation `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
    -AppId $appId

Sync-AppStoreConnectAppInfoMetadata `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
    -ConfigPath '.\appstoreconnect-app-info.json'
```

Apple locks App Information for a version that is already Ready for Distribution. Create the
next editable App Store version first; the sync service selects its editable App
Information resource and refuses to silently update a locked resource. Every App Information
config must declare `appId`, so a config can never be applied to another app by accident.
App Information-only runs do not require a version or build number because these fields belong
to the app-level resource rather than an App Store version. Use one config per app and locale;
the unified release applies every matching locale once per unique app id even when iOS and macOS
targets share that app, and fails when a selected app has no matching config.

## Screenshot Upload Flow

Screenshot upload uses App Store Connect's asset reservation flow:

1. find the App Store version localization
2. find or create a screenshot set for a display type
3. reserve the screenshot asset
4. upload each asset operation returned by Apple
5. commit the screenshot checksum

```powershell
$version = Get-AppStoreConnectVersion `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
    -AppId $appId -VersionString '1.0.0' -Platform iOS |
    Select-Object -First 1

$localization = Get-AppStoreConnectVersionLocalization `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
    -VersionId $version.Id -Locale 'en-US' |
    Select-Object -First 1

$set = Get-AppStoreConnectScreenshotSet `
    -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
    -VersionLocalizationId $localization.Id |
    Where-Object ScreenshotDisplayType -eq 'APP_IPHONE_65' |
    Select-Object -First 1

if (-not $set) {
    $set = New-AppStoreConnectScreenshotSet `
        -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
        -VersionLocalizationId $localization.Id `
        -ScreenshotDisplayType 'APP_IPHONE_65'
}

Get-ChildItem '.\build\appstore-screenshots\upload\iphone-6-5' -Filter *.png |
    Publish-AppStoreConnectScreenshot `
        -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath $keyPath `
        -ScreenshotSetId $set.Id
```

Use the config-driven sync below for normal releases. The lower-level commands remain
available for recovery of one set or file.

## Config-Driven Screenshot Sync

For repeatable releases, define the folder-to-display-type mapping in JSON:

```json
{
  "appId": "6775426723",
  "versionString": "1.0.0",
  "versionId": null,
  "platform": "iOS",
  "locale": "en-US",
  "screenshotSets": [
    {
      "screenshotDisplayType": "APP_IPHONE_65",
      "path": "upload/iphone-6-5",
      "filter": "*.png"
    },
    {
      "screenshotDisplayType": "APP_IPAD_PRO_129",
      "path": "upload/ipad-13-2048x2732",
      "filter": "*.png"
    },
    {
      "screenshotDisplayType": "APP_IPAD_PRO_3GEN_129",
      "path": "upload/ipad-13-2064x2752",
      "filter": "*.png"
    },
    {
      "screenshotDisplayType": "APP_DESKTOP",
      "path": "upload/macos-16x10",
      "filter": "*.png"
    }
  ]
}
```

Then sync it:

```powershell
Test-AppStoreConnectScreenshotSyncConfig -ConfigPath '.\appstore-screenshots.json' -PassThru

Sync-AppStoreConnectScreenshots `
    -IssuerId $issuerId `
    -KeyId $keyId `
    -PrivateKeyPath $keyPath `
    -ConfigPath '.\appstore-screenshots.json' `
    -ReplaceExisting
```

Relative paths are resolved from the directory containing the JSON file. `-ReplaceExisting`
deletes existing screenshots in each matched screenshot set before uploading the local files.
If the visible App Store Connect version string differs from the build marketing version,
set `versionId` to the App Store version id to bypass version lookup.

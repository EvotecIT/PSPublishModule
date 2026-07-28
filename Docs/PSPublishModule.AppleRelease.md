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
- `powerforge-apple-approval.yml` accepts only `SubmitTestFlightReview`,
  `SubmitAppReview`, or `Release`, verifies an optional approver allow-list, and runs
  inside a protected GitHub environment when the repository plan supports reviewers.
- `powerforge-apple-monitor.yml` runs scheduled `Doctor`, retains the compact receipt,
  and maintains one GitHub incident until errors and warnings are cleared.
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
commits. The reusable jobs select the caller repository's protected environment and
resolve its environment-scoped App Store Connect secrets there. Do not add
repository-wide `secrets: inherit`; that would weaken the environment boundary. The composite
action writes the private key to a permission-restricted temporary file and removes
it after every plan or action. Mutating release workflows, including both screenshot
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
path traversal, requires PNG output, retains it before requesting environment approval,
and never accepts a branch or tag as capture evidence. This lets CasaRay keep its rich
simulator routes while Tactra, EasyControlX, and EmailIMO add small capture scripts with
the same approval and upload contract.

After reviewing the captured images, generate the manifest without hand-copying file
hashes or dimensions:

```text
powerforge apple-screenshots manifest \
  --config scripts/appstoreconnect-screenshots-ios.json \
  --release-config powerforge.release.json \
  --target "Primary iOS App" \
  --version 1.6.0 \
  --source-commit 0123456789abcdef0123456789abcdef01234567 \
  --approved-by release-owner \
  --allowed-root build/appstore-screenshots \
  --runtime "iOS 26.0" \
  --device "iPhone 17 Pro Max" \
  --theme light \
  --scenario app-store
```

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
| `TestFlightPolicy` | Disables TestFlight or limits a target to internal/external distribution without inferring intent from beta groups. |

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
PowerForge commits, builds that exact shared source, runs Doctor on a trusted Apple
runner, retains the compact receipt, and maintains one stable GitHub incident. Errors
and warnings open the issue; a clean later run closes it. This catches upload/build
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
local artifact cleanup. It reads pricing, availability, phased-release, monetization,
compliance, accessibility, webhook, customer-review, and beta-feedback state for
Doctor. Apple still owns processing and review decisions, and human-reviewed
commercial or compliance changes remain explicit App Store Connect operations.

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

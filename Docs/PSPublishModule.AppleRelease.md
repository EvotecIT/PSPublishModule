# Apple App Release Helpers

PowerForge includes reusable Apple app release helpers for native Apple projects.
The first supported slice wraps the signed binary path that Apple expects:

1. create an `.xcarchive` with `xcodebuild archive`
2. upload that archive to App Store Connect with `xcodebuild -exportArchive`
3. use existing App Store Connect read-only cmdlets to verify app, version, and build state

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
        "Name": "Example Mac",
        "BundleId": "com.example.product",
        "AppStoreConnectAppId": "1234567890",
        "Platform": "macOS",
        "ProjectPath": "Product.xcodeproj",
        "Scheme": "ProductMac"
      }
    ]
  }
}
```

Start with a read-only status receipt:

```text
powerforge release --config powerforge.release.json --apple-action Status --summary --output json
```

Use the same entry point for each transition:

| Action | Result |
| --- | --- |
| `Status` | Reads the exact configured version/build and recommends the next action. |
| `Archive` | Creates signed archives without uploading. |
| `Upload` | Archives, uploads, waits for processing, and resumes an exact remote build when possible. |
| `UploadExisting` | Uploads existing archives and uses the same resume/wait behavior. |
| `Prepare` | Creates/updates versions, metadata, app information, build selection, and readiness. |
| `Screenshots` | Validates and syncs configured screenshot sets as a separate, deliberate transition. |
| `TestFlight` | Assigns the processed build to configured groups and testers. |
| `SubmitTestFlightReview` | Submits external TestFlight distribution for Beta App Review. |
| `SubmitAppReview` | Submits a ready App Store version for App Review. |
| `Release` | Publishes a version waiting for developer release. |
| `Cleanup` | Removes stale files only from configured archive/export roots. |

Plan a mutating action before running it:

```text
powerforge release --config powerforge.release.json --apple-action Upload --plan
powerforge release --config powerforge.release.json --apple-action Upload --summary --output json
```

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
steps, bounded cleanup, and policy-aware next actions. `readinessChecked=false` means
the read-only status action did not query metadata or screenshot readiness; run
`Prepare` or `Screenshots` for those checks. The persisted receipt uses its configured
project-relative path and never contains API credentials. A rerun with `Resume=true`
checks the exact remote version/build before rebuilding or uploading.

Projects generated by XcodeGen can set `GenerateProjectIfMissing` on a target. PowerForge
then runs `xcodegen generate` from the directory containing `project.yml` before an
archive action. This keeps project-generation logic out of consumer scripts.

## Current Boundary

PowerForge owns archive/upload, bounded build processing waits, Distribution
preparation, metadata and screenshot sync, build selection, TestFlight distribution,
review submission, approved-version release, status receipts, and local artifact
cleanup. Apple still owns processing and review decisions. Pricing and phased-release
configuration remain explicit App Store Connect operations.

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

The current screenshot commands intentionally work at the set/file level. A higher-level
folder sync command can layer on top once each app defines its folder-to-display-type map.

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

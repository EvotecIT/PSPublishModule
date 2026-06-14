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
```

## Unified Release Flow

Apple app archive/upload targets can also live in the same `powerforge.release.json`
used for modules, NuGet packages, downloadable tools, Winget manifests, and GitHub
release assets. This is the preferred shape for apps such as Tactra or BayManager
that need one release file to describe iPhone, iPad, macOS, and store-delivery lanes.

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
    "Upload": false,
    "ScreenshotConfigPaths": [
      "scripts/appstoreconnect-screenshots-ios.json",
      "scripts/appstoreconnect-screenshots-macos.json"
    ],
    "Apps": [
      {
        "Name": "Tactra iPhone",
        "BundleId": "com.evotecit.tactra",
        "Platform": "iOS",
        "ProjectPath": "Tactra.xcodeproj",
        "Scheme": "Tactra"
      },
      {
        "Name": "Tactra iPad",
        "BundleId": "com.evotecit.tactra",
        "Platform": "iPadOS",
        "ProjectPath": "Tactra.xcodeproj",
        "Scheme": "Tactra"
      },
      {
        "Name": "Tactra Mac",
        "BundleId": "com.evotecit.tactra.mac",
        "Platform": "macOS",
        "ProjectPath": "Tactra.xcodeproj",
        "Scheme": "TactraMac"
      }
    ]
  }
}
```

Plan first:

```powershell
Invoke-PowerForgeRelease -ConfigPath '.\powerforge.release.json' -ToolsOnly -Plan
```

Archive without uploading:

```powershell
Invoke-PowerForgeRelease -ConfigPath '.\powerforge.release.json' -ToolsOnly
```

Upload to App Store Connect by setting `AppleApps.Upload` to `true`, or by keeping a
separate release config for the store lane. The unified flow reuses the same archive
and `xcodebuild -exportArchive` helpers as `New-AppleAppArchive` and
`Publish-AppleAppArchive`; it does not duplicate signing or upload behavior.

## Current Boundary

These helpers automate signed binary archive/upload and provide low-level screenshot
upload primitives. App Store localized text metadata, build selection for review, and
release submission still need dedicated App Store Connect write support before they
should be automated here.

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

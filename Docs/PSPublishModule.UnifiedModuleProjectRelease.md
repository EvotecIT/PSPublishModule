# Unified Module And Project Releases

Use this workflow when one repository publishes a PowerShell module and one or more NuGet packages from the same build:

- the PowerShell module through `Build/Build-Module.ps1`
- one or more NuGet packages through the project build pipeline
- PowerShell Gallery publishing
- NuGet publishing
- a single GitHub release containing the module, NuGet packages, checksums, and manifest

The important design point is that this is one PowerForge release runtime, not `Build-Module.ps1`
manually calling `Build-Project.ps1`.

In this document, "one package" means one coordinated release transaction and one staged GitHub asset set.
NuGet and PowerShell Gallery still receive their native package formats because those ecosystems have different
contracts.

## When To Use It

Repositories such as PSParseHTML commonly have two cleanly separated entrypoints:

- `Build/Build-Module.ps1` declares module manifest, merge, binary module build, signing, and module artifacts through the existing `Build-Module` / `New-Configuration*` DSL.
- `Build/Build-Project.ps1` is a thin wrapper over `Invoke-ProjectBuild -ConfigPath Build/project.build.json`.
- `Build/project.build.json` owns the HtmlTinkerX NuGet package release settings, NuGet.org publishing, and GitHub project-release settings.

Keep that ownership split. The coordinated module pipeline provides one release-day entrypoint:

> Build everything for this version, publish packages to NuGet, publish the module to PSGallery, and upload the final release set to GitHub.

## Public Surface

The existing `Build-Module {}` / `New-Configuration*` authoring model can declare package builds, release staging,
version coordination, and publishing intent in the same settings block.

The operator runs the familiar command:

```powershell
.\Build\Build-Module.ps1
```

or selects the wrapper's publish gate when explicit gate controls are exposed:

```powershell
.\Build\Build-Module.ps1 -ConfigurationGateMode Publish
```

Internally, `Build-Module {}` keeps using the module pipeline and carries package and release coordination
segments in the same runtime. This keeps the user-facing DSL in one place without turning the module script into
a second package engine.

Repositories that also ship native CLI archives can keep those targets in the broader
`Invoke-PowerForgeRelease` configuration. Their publish wrapper should select the module `Publish` gate and the
tool GitHub publisher in the same engine invocation. The module pipeline still owns NuGet, PowerShell Gallery,
and the coordinated module GitHub asset set; the tool lane owns runtime-specific Windows, Linux, and macOS
archives. This is one operator command with one release owner, even when independent product version lines require
separate GitHub tags.

## Recommended Build-Module Shape

The Build-Module-first workflow supports both JSON-backed and PowerShell-authored package configuration.

### JSON-backed package configuration

This is the lowest-risk bridge for existing repositories. It preserves every option already supported by
`project.build.json` and keeps CI/schema-driven configuration stable.

```powershell
Build-Module -ModuleName 'PSParseHTML' {
    New-ConfigurationManifest @Manifest
    New-ConfigurationBuild @ModuleBuild
    New-ConfigurationArtefact @PackedModule

    New-ConfigurationProjectBuild `
        -Name 'HtmlTinkerX' `
        -ConfigPath '.\Build\project.build.json' `
        -BuildBeforeModule `
        -UseAsReleaseVersionSource `
        -ProvideLocalNuGetFeed `
        -PublishNuget

    New-ConfigurationRelease `
        -StageRoot '.\Artefacts\UploadReady' `
        -VersionSource ProjectBuild `
        -PrimaryProject 'HtmlTinkerX' `
        -SynchronizeModuleVersion `
        -PublishOrder NuGet, PowerShellGallery, GitHub

    New-ConfigurationPublish -Type PowerShellGallery -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' -Enabled
    New-ConfigurationPublish -Type GitHub `
        -FilePath 'C:\Support\Important\GithubAPI.txt' `
        -UserName 'EvotecIT' `
        -RepositoryName 'PSParseHTML' `
        -OverwriteTagName 'PSParseHTML-v<ResolvedReleaseVersion>' `
        -GenerateReleaseNotes `
        -Enabled
} -ExitCode
```

NuGet package publishing stays in `New-ConfigurationProjectBuild` / `New-ConfigurationPackageBuild` because
that is package-lane behavior. PowerShell Gallery and top-level GitHub publishing stay with module/release
publish configuration.

### PowerShell-authored package configuration

For modules where you want the whole release in PowerShell, declare the package config inline. It maps to
`ProjectBuildConfiguration`, exports to JSON, and runs through the same engine.

```powershell
Build-Module -ModuleName 'PSParseHTML' {
    New-ConfigurationManifest @Manifest
    New-ConfigurationBuild @ModuleBuild
    New-ConfigurationArtefact @PackedModule

    New-ConfigurationPackageBuild `
        -Name 'HtmlTinkerX' `
        -RootPath '.\Sources' `
        -ExpectedVersionMap @{ HtmlTinkerX = '2.0.X' } `
        -ExpectedVersionMapAsInclude `
        -Configuration Release `
        -StagingPath '.\Artefacts\ProjectBuild' `
        -CleanStaging `
        -CreateReleaseZip `
        -CertificateThumbprint 'YOUR_CERTIFICATE_THUMBPRINT' `
        -CertificateStore CurrentUser `
        -PublishNuget `
        -PublishSource 'https://api.nuget.org/v3/index.json' `
        -PublishApiKeyFilePath 'C:\Support\Important\NugetOrgEvotec.txt' `
        -BuildBeforeModule `
        -ProvideLocalNuGetFeed `
        -UseAsReleaseVersionSource

    New-ConfigurationRelease `
        -StageRoot '.\Artefacts\UploadReady' `
        -VersionSource PackageBuild `
        -PrimaryProject 'HtmlTinkerX' `
        -SynchronizeModuleVersion `
        -PublishOrder NuGet, PowerShellGallery, GitHub

    New-ConfigurationPublish `
        -Type PowerShellGallery `
        -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' `
        -Enabled

    New-ConfigurationPublish `
        -Type GitHub `
        -UserName 'EvotecIT' `
        -RepositoryName 'PSParseHTML' `
        -FilePath 'C:\Support\Important\GithubAPI.txt' `
        -OverwriteTagName 'PSParseHTML-v<ResolvedReleaseVersion>' `
        -GenerateReleaseNotes `
        -Enabled
} -ExitCode
```

The direct PowerShell route covers the project-build schema with first-class parameters and the `-Options`
hashtable for additional fields.

## How Version Coordination Works

`-SynchronizeModuleVersion` coordinates the module with `-PrimaryProject`. It does not, by itself, assign one version
to every NuGet package in the repository.

PowerForge makes the version decision in this order:

1. Resolve the next available module version from the module X-pattern and module history.
2. Use that version as a floor for `-PrimaryProject` in the one package lane marked `-UseAsReleaseVersionSource`.
3. Resolve the primary project's normal candidate from its X-pattern and NuGet history.
4. Select the higher numeric version. At the same numeric version, a stable X-pattern candidate does not erase a
   configured module prerelease; explicit prerelease versions retain normal semantic-version ordering.
5. If `AlignPackageVersions` is enabled, apply the final version to every project in the primary project's matching
   ordinary X-pattern group or named `VersionTrack` alignment group.
6. Synchronize the module manifest, artifacts, staging metadata, and publish operations to the final primary-project
   version.

### What AlignPackageVersions changes

`AlignPackageVersions` defaults to `false`. Omitting it from `project.build.json` has the same behavior as setting it
to `false`: the module and `PrimaryProject` coordinate, while other packages follow their normal project-build version
policy. Ordinary X-pattern entries resolve independently; named `VersionTracks` already coordinate their members.

| Module synchronization | Package alignment | Result |
| --- | --- | --- |
| Disabled | Disabled | The module uses module history. Ordinary package X-patterns resolve independently; each `VersionTrack` applies its configured exact version or anchor-derived X-pattern version to all members. |
| Disabled | Enabled | The module still uses module history. Ordinary packages with the same X-pattern align; every named X-pattern `VersionTrack` remains a separate alignment group, while exact-version tracks remain exact. |
| Enabled | Disabled | The module matches `PrimaryProject`. Other ordinary package X-patterns resolve independently; `VersionTracks` continue coordinating their own members. |
| Enabled | Enabled | The module matches `PrimaryProject`. The primary project's ordinary X-pattern group or named X-pattern `VersionTrack` receives the coordinated floor; exact-version tracks remain exact and must be compatible with that floor. Other groups remain independent. |

For ordinary `ExpectedVersion` and `ExpectedVersionMap` entries, `AlignPackageVersions` groups projects by their
configured X-pattern. All ordinary projects configured with `2.1.X` form one group, while ordinary projects configured
with `1.4.X` form another. Each group is stepped from the highest NuGet version found for a package in that group.
The module floor is applied only to the group containing `PrimaryProject`.

`VersionTracks` are explicit release trains and define their own boundaries:

- With alignment disabled, each track applies one version to all members: either its configured exact version or an
  X-pattern version resolved from its configured anchor.
- With alignment enabled, each X-pattern track becomes its own alignment group. Two named X-pattern tracks using the
  same pattern do not merge with each other or with ordinary projects using that pattern.
- An exact-version track never becomes an alignment group. Its configured exact version is still applied to all members.
- When `PrimaryProject` belongs to an aligned track, the module floor applies to the whole track.
- When alignment is disabled, an anchor-derived track version below the module floor is rejected instead of splitting
  the track by raising only the primary project.
- When `PrimaryProject` belongs to an exact-version track, an exact version below the module floor is rejected; a
  compatible exact version remains exact and is shared by the module and every track member.

Ordinary exact versions outside `VersionTracks` do not participate in X-pattern alignment. Such an exact version remains
exact unless it belongs to the primary project and is below the coordinated module floor, in which case validation fails
before project files change.

### Example: alignment disabled

Assume this illustrative package configuration:

```json
{
  "ExpectedVersionMap": {
    "Mailozaurr": "2.1.X",
    "Mailozaurr.Application": "2.1.X",
    "Mailozaurr.Cli": "2.1.X"
  },
  "ExpectedVersionMapAsInclude": true,
  "AlignPackageVersions": false
}
```

and these histories:

| Deliverable | Current published version | Normal next candidate |
| --- | ---: | ---: |
| Mailozaurr module | `2.1.6` | module floor `2.1.7` |
| Mailozaurr package | `2.0.12` | `2.1.0` |
| Mailozaurr.Application | `2.1.5` | `2.1.6` |
| Mailozaurr.Cli | not published | `2.1.0` |

The resolved release is:

| Deliverable | Resolved version | Reason |
| --- | ---: | --- |
| Mailozaurr module | `2.1.7` | Synchronized to the primary project |
| Mailozaurr package | `2.1.7` | Raised from `2.1.0` to the module floor |
| Mailozaurr.Application | `2.1.6` | Keeps its independent NuGet candidate |
| Mailozaurr.Cli | `2.1.0` | Keeps its independent NuGet candidate |

This is the correct choice when companion packages have independent release histories. The combined GitHub release may
contain packages with different versions; only the module and primary package are guaranteed to match. If the repository
uses `VersionTracks`, members of each track still share their anchor-derived version even though alignment is disabled.

### Example: alignment enabled

Using the same histories, change the JSON setting to:

```json
{
  "ExpectedVersionMap": {
    "Mailozaurr": "2.1.X",
    "Mailozaurr.Application": "2.1.X",
    "Mailozaurr.Cli": "2.1.X"
  },
  "ExpectedVersionMapAsInclude": true,
  "AlignPackageVersions": true
}
```

The three packages share `2.1.X`, so they form one aligned group. Their NuGet-derived group candidate is `2.1.6`,
while the module floor is `2.1.7`. The module floor wins and the complete release uses:

| Deliverable | Resolved version |
| --- | ---: |
| Mailozaurr module | `2.1.7` |
| Mailozaurr package | `2.1.7` |
| Mailozaurr.Application | `2.1.7` |
| Mailozaurr.Cli | `2.1.7` |

Use alignment when those packages are one release train and should always carry the same version.

### Example: NuGet history is ahead

If the module floor is `2.1.7` but a package in the aligned `2.1.X` group is already published as `2.1.9`, the group
candidate becomes `2.1.10`. That higher numeric candidate wins:

| Deliverable | Resolved version |
| --- | ---: |
| Module | `2.1.10` |
| Primary package | `2.1.10` |
| Other packages in the `2.1.X` group | `2.1.10` |

Synchronization therefore protects both histories: module history prevents the package release from moving backward,
and NuGet history can move the module release forward.

### Example: different version groups

Consider this configuration:

```json
{
  "ExpectedVersionMap": {
    "Suite.Core": "2.1.X",
    "Suite.Rendering": "2.1.X",
    "Suite.LegacyBridge": "1.4.X",
    "Suite.Protocol": "3.0.5"
  },
  "ExpectedVersionMapAsInclude": true,
  "AlignPackageVersions": true
}
```

If `Suite.Core` is `PrimaryProject`, the module floor applies to the `2.1.X` group containing `Suite.Core` and
`Suite.Rendering`. `Suite.LegacyBridge` aligns only within the separate `1.4.X` group, and the exact
`Suite.Protocol` version remains `3.0.5`. Enabling alignment does not force these four packages onto one universal
version.

### Prerelease example

Suppose the configured module prerelease produces a floor of `2.1.7-beta.2`, while the package X-pattern produces the
stable numeric candidate `2.1.7`. PowerForge keeps `2.1.7-beta.2`; the generated stable X-pattern candidate cannot
silently turn a prerelease run into a stable release.

Explicit versions retain normal semantic-version ordering. For example:

```text
2.1.7-beta.1 < 2.1.7-beta.2 < 2.1.7-rc.1 < 2.1.7
```

A genuinely higher numeric candidate such as `2.1.8` still wins and becomes the final module/package version.

### Invalid configurations

PowerForge rejects coordinated versioning before changing project files when:

- `PrimaryProject` is missing or does not match a packable project name or package ID.
- More than one active lane is marked `UseAsReleaseVersionSource`.
- The selected lane runs after the module.
- The module and primary package use incompatible version lines, such as module `2.1.X` and package `2.0.X`.
- The primary package uses an exact version below the module floor.
- Version updates are disabled for the selected package lane.

`BuildBeforeModule` is enough to express dependency order for a single package lane. Use `Release.BuildOrder` only when
several lanes need an explicit order.

### Choosing whether to align

Enable `AlignPackageVersions` when:

- the packages are released and supported as one versioned product;
- the module consumes those packages as one coordinated dependency set;
- users expect every package asset in the release to carry the same version.

Leave it disabled when:

- companion packages have independent compatibility or release policies;
- a repository intentionally contains several product/version lines;
- only the module and `PrimaryProject` need to match.

## One GitHub Release

Choose one GitHub publisher. The normal unified shape is:

- package lane: `-PublishNuget`
- module publish segment: `-Type GitHub`
- release order: `NuGet`, `PowerShellGallery`, `GitHub`

The module GitHub operation uploads the staged module and package assets together. Do not also set `-PublishGitHub`
on `New-ConfigurationProjectBuild` or `New-ConfigurationPackageBuild` unless two separate GitHub operations are
intentional.

## JSON Or Direct PowerShell

Both forms use the same model.

JSON remains the canonical interchange format for CI, schema validation, generated configs, and exact
round-tripping:

```powershell
Build-Module -ModuleName 'PSParseHTML' {
    New-ConfigurationProjectBuild -ConfigPath '.\Build\project.build.json' -BuildBeforeModule
}
```

PowerShell is the maintainer authoring experience for module repositories:

```powershell
Build-Module -ModuleName 'PSParseHTML' {
    New-ConfigurationPackageBuild -RootPath '.\Sources' -ExpectedVersionMap @{ HtmlTinkerX = '2.0.X' }
}
```

The same model supports:

- `Invoke-ModuleBuild -JsonOnly -JsonPath .\Artefacts\powerforge.release.json`
- import/export of the package/release section
- generated `Build/release.json` for CI if the repo wants checked-in JSON
- pure `Build-Module.ps1` for repos that prefer PowerShell DSL only

## Naming

Use "release" only for the coordinated top-level artifact set, usually the thing that gets a GitHub tag,
manifest, checksums, and user-facing release notes.

For .NET/NuGet work, prefer package-oriented names:

- `New-ConfigurationProjectBuild` for the JSON-backed bridge to the existing project build engine.
- `New-ConfigurationPackageBuild` for inline package discovery, versioning, pack, sign, and package-publish defaults.
- `New-ConfigurationPackageVersionTrack` if the inline DSL needs a focused helper for `VersionTracks`.
- `New-ConfigurationPublish` or `New-ConfigurationReleasePublish` for destinations such as NuGet, PowerShell Gallery, and GitHub.
- `New-ConfigurationRelease` only for cross-output coordination: staged root, manifest, checksums, tag template, shared version source, build order, and publish order.

That avoids describing `.nupkg` output as a "release" before it actually participates in the repo-level release.

## Non-Goals

- Do not make `Build-Module.ps1` responsible for NuGet package publishing mechanics.
- Do not make `Build-Project.ps1` know module artifact layout or PSGallery publishing.
- Do not duplicate signing, GitHub release, tag conflict, checksums, or staging logic in consumer repos.
- Do not require publishing intermediate NuGet packages before the module can build.

## Recommended Engine Model

Keep three layers:

1. Module lane
   - Existing `Invoke-ModuleBuild` / `Build-Module` DSL stays the source for module build details.
   - Module publish settings can stay as `New-ConfigurationPublish -Type PowerShellGallery` for direct module builds.
   - Unified release may override whether the module is built, signed, published, or only staged.

2. Package lane
   - Existing `Invoke-ProjectBuild` / `project.build.json` remains the source for NuGet package discovery, versioning, packing, signing, and NuGet.org publishing.
   - In unified mode, disable package GitHub publishing when top-level GitHub publishing is active, so assets are uploaded once.

3. Release lane
   - `New-ConfigurationRelease` owns the module-pipeline coordination slice: staged root and the single top-level GitHub asset set.
   - The broader `Invoke-PowerForgeRelease` engine remains the long-term owner for checksums, release manifests, and richer graph planning.
   - `BuildBeforeModule` and optional `Release.BuildOrder` declare phase order instead of relying on script call order.

## Release Phases

The release runtime executes these phases:

1. Load and validate every section.
2. Resolve one release version from `VersionTracks`, `ExpectedVersionMap`, explicit module version, or a declared primary package.
3. Plan packages and module before doing any destructive or publishing work.
4. If the module needs packages from this same release, build packages to a local staging feed first.
5. Build the module against either project references or the staged local package feed.
6. Validate module import and package outputs.
7. Publish NuGet packages.
8. Publish the module to PSGallery or configured private gallery.
9. Stage all release assets into one upload-ready folder.
10. Write `release-manifest.json` and `SHA256SUMS.txt`.
11. Publish one GitHub release from the staged asset list.

This keeps publication fail-fast while avoiding a half-published release where NuGet is updated before the
module can even be built.

## Build-Module Runtime Behavior

`Build-Module {}` keeps its current module-only behavior when no project/release segments are present.
When `New-ConfigurationProjectBuild`, `New-ConfigurationPackageBuild`, or `New-ConfigurationRelease` is
present, `Invoke-ModuleBuild` now keeps those segments in the module pipeline plan:

1. collect normal module segments into the existing module pipeline spec
2. collect project/release segments into package/release plan sections
3. export those segments with the module JSON plan when `-JsonOnly` is used
4. execute `BuildBeforeModule` package lanes before module staging
5. inject package output folders as additional NuGet restore sources when `ProvideLocalNuGetFeed` is enabled
6. stage module and package file assets when `Release.StageRoot` is configured
7. publish one top-level GitHub release from the staged asset list when a GitHub `Publish` segment is enabled

That means consumer repos can stay with one script and one DSL block, while PowerForge still keeps one reusable
implementation for package build, module build, and GitHub publishing.

## Runtime Contract

- `New-ConfigurationProjectBuild` references existing `project.build.json` files from `Build-Module {}`.
- `New-ConfigurationPackageBuild` declares inline package build settings with first-class parameters for the
  current project-build options and `-Options` for additional fields.
- `New-ConfigurationRelease` declares repo-level coordination settings such as stage root, version source,
  primary project, build order, and publish order.
- `ModulePipelineSpec` JSON serialization and deserialization preserve these segments.
- `ModuleBuildPreparationService` resolves and exports package/release paths relative to the module project
  root while leaving secret-file paths untouched.
- `ModulePipelinePlan` keeps enabled package/release segments explicitly instead of silently dropping them.
- `ModulePipelineRunner` runs enabled `ProjectBuild` and `PackageBuild` lanes with `BuildBeforeModule` before
  module staging/build starts, and exposes the resulting `ProjectBuildHostExecutionResult` values in
  `ModulePipelineResult.ProjectBuildResults`.
- Inline `PackageBuild` settings map back into the existing shared `ProjectBuildConfiguration` and execute
  through `ProjectBuildHostService`; cmdlets remain thin DSL emitters.
- `ProvideLocalNuGetFeed` now requires built `.nupkg` outputs and appends their directories to
  `ModuleBuildSpec.NuGetRestoreSources`.
- Module binary publish uses `RestoreAdditionalProjectSources` so release-local packages are added without
  discarding normal project or NuGet.config sources.
- A `Release` segment plus an enabled GitHub `Publish` segment stages module artifacts and `.nupkg` outputs
  under `StageRoot` (`modules` and `nuget`) and publishes one GitHub release from that combined asset list.
- Release staging writes `metadata/release-manifest.json` and `metadata/SHA256SUMS.txt` and includes them in
  the top-level GitHub asset list.
- `ModulePipelineResult.ReleaseCoordinationResult` reports the staged module/package assets and GitHub result.
- `Release.VersionSource`, `PrimaryProject`, lane-level `UseAsReleaseVersionSource`, and
  `SynchronizeModuleVersion` produce one module/package version decision.
- Publish runs preflight the synchronized module version before the first remote package publish.
- A credential-free checkpoint records exact project versions and completed destinations so retries do not
  step versions again or repeat completed publishes.

## Validation Contract

Use gate modes for the same wrapper in CI and at release time:

- `Manifest`: refresh module metadata without package builds or publishing.
- `Build`: resolve coordinated versions, build packages, feed them to the module build, and stage artifacts without publishing.
- `Publish`: rebuild or reuse the exact coordinated package state and publish in `Release.PublishOrder`.

For a wrapper exposing `-ConfigurationGateMode`, the practical sequence is:

```powershell
.\Build\Build-Module.ps1 -ConfigurationGateMode Manifest
.\Build\Build-Module.ps1 -ConfigurationGateMode Build
.\Build\Build-Module.ps1 -ConfigurationGateMode Publish
```

The Build gate should report the same resolved version for the primary package and module, list the local NuGet
source added for the module build, and show the combined module/package assets under `Release.StageRoot`.

## Recommendation

Make `Build-Module {}` the primary authoring surface for module repositories, and let it declare project
packages, release staging, and publish targets through proper `New-Configuration*` segments.

The important boundary is internal: those segments should translate into the shared unified release model
instead of making module build reimplement NuGet, PSGallery, and GitHub release mechanics.

That gives PSParseHTML the one-button release you want while keeping:

- module build rules in the module DSL
- NuGet package rules in project build
- shared release/version/publish behavior in PowerForge
- GitHub release upload as one final staged asset transaction

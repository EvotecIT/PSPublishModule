# Unified Module And Project Release Proposal

This proposal covers repositories such as PSParseHTML where one release should build:

- the PowerShell module through `Build/Build-Module.ps1`
- one or more NuGet packages through the project build pipeline
- PowerShell Gallery publishing
- NuGet publishing
- a single GitHub release containing the module, NuGet packages, checksums, and manifest

The important design point is that this should be one PowerForge release runtime, not `Build-Module.ps1`
manually calling `Build-Project.ps1`.

In this document, "one package" means one coordinated release transaction and one staged GitHub asset set.
NuGet and PowerShell Gallery still receive their native package formats because those ecosystems have different
contracts.

## Current Shape In PSParseHTML

PSParseHTML currently has two cleanly separated entrypoints:

- `Build/Build-Module.ps1` declares module manifest, merge, binary module build, signing, and module artifacts through the existing `Build-Module` / `New-Configuration*` DSL.
- `Build/Build-Project.ps1` is a thin wrapper over `Invoke-ProjectBuild -ConfigPath Build/project.build.json`.
- `Build/project.build.json` owns the HtmlTinkerX NuGet package release settings, NuGet.org publishing, and GitHub project-release settings.

That split is good for ownership, but awkward for release day because the operator wants one answer:

> Build everything for this version, publish packages to NuGet, publish the module to PSGallery, and upload the final release set to GitHub.

## Goal

Extend the existing `Build-Module {}` / `New-Configuration*` authoring model so the module build script can
declare package builds, release staging, and publishing intent in the same settings block.

The operator should still run the familiar command:

```powershell
.\Build\Build-Module.ps1
```

or, when explicit publish controls are needed:

```powershell
.\Build\Build-Module.ps1 -PublishNuget -PublishGallery -PublishGitHub
```

Internally, `Build-Module {}` keeps using the module pipeline but now carries package and release coordination
segments in the same runtime. This keeps the user-facing DSL in one place without turning the module script into
a second package engine.

## Recommended Build-Module Shape

The Build-Module-first version should support both JSON-backed and PowerShell-authored package configuration.

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
        -ProvideLocalNuGetFeed

    New-ConfigurationRelease `
        -StageRoot '.\Artefacts\UploadReady' `
        -VersionSource ProjectBuild `
        -PrimaryProject 'HtmlTinkerX' `
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

For modules where you want the whole release in PowerShell, the package config can be declared inline. This
should map one-for-one to `ProjectBuildConfiguration` so it can later export to JSON and run through the same
engine.

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
        -CertificateThumbprint '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703' `
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

The direct PowerShell route should be complete, not a toy subset. If the equivalent JSON field exists in
`project.build.schema.json`, there should be a PowerShell way to set it, either as a first-class parameter or
as a hashtable escape hatch while the surface matures.

## JSON Or Direct PowerShell

Support both.

JSON should remain the canonical interchange format for CI, schema validation, generated configs, and exact
round-tripping:

```powershell
Build-Module -ModuleName 'PSParseHTML' {
    New-ConfigurationProjectBuild -ConfigPath '.\Build\project.build.json' -BuildBeforeModule
}
```

PowerShell should be the canonical maintainer authoring experience for module repositories:

```powershell
Build-Module -ModuleName 'PSParseHTML' {
    New-ConfigurationPackageBuild -RootPath '.\Sources' -ExpectedVersionMap @{ HtmlTinkerX = '2.0.X' }
}
```

The same model should support:

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
   - In unified mode, package GitHub publishing should be disabled when top-level GitHub publishing is active, so assets are uploaded once.

3. Release lane
   - `New-ConfigurationRelease` owns the module-pipeline coordination slice: staged root and the single top-level GitHub asset set.
   - The broader `Invoke-PowerForgeRelease` engine remains the long-term owner for checksums, release manifests, and richer graph planning.
   - Phase order should come from declared dependencies instead of relying on script call order.

## Required Release Phases

The release runtime should execute these phases:

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

## JSON Shape

The existing `powerforge.release.schema.json` is already close. It has top-level `Module`, `Packages`,
`Outputs`, and `GitHub` sections. For PSParseHTML-style flows it needs a small extension for graph/order
and package-to-module dependency injection.

Proposed additive shape:

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.release.schema.json",
  "SchemaVersion": 1,
  "Release": {
    "VersionSource": "Packages",
    "PrimaryProject": "HtmlTinkerX",
    "BuildOrder": [ "Packages", "Module" ],
    "PublishOrder": [ "NuGet", "PowerShellGallery", "GitHub" ]
  },
  "Module": {
    "RepositoryRoot": ".",
    "ScriptPath": "Build/Build-Module.ps1",
    "ArtifactPaths": [
      "Artefacts/Packed"
    ],
    "UseResolvedReleaseVersion": true,
    "PackageDependencies": [
      {
        "PackageId": "HtmlTinkerX",
        "Source": "Packages",
        "Injection": "LocalNuGetFeed"
      }
    ],
    "Publish": {
      "PowerShellGallery": true
    }
  },
  "Packages": {
    "RootPath": "Sources",
    "ExpectedVersionMap": {
      "HtmlTinkerX": "2.0.X"
    },
    "ExpectedVersionMapAsInclude": true,
    "StagingPath": "Artefacts/ProjectBuild",
    "PublishNuget": true,
    "PublishGitHub": false
  },
  "Outputs": {
    "Staging": {
      "RootPath": "Artefacts/UploadReady",
      "ModulesPath": "PowerShellGallery",
      "PackagesPath": "NuGet",
      "MetadataPath": "Metadata",
      "ModulesNameTemplate": "{FileName}",
      "PackagesNameTemplate": "{PackageId}.{Version}{Extension}"
    }
  },
  "GitHub": {
    "Publish": true,
    "Owner": "EvotecIT",
    "Repository": "PSParseHTML",
    "TokenFilePath": "C:\\Support\\Important\\GithubAPI.txt",
    "TagTemplate": "{Repository}-v{Version}",
    "ReleaseNameTemplate": "{Repository} {Version}"
  }
}
```

The current schema can already express most of this, but not `Release.BuildOrder`,
`Release.PublishOrder`, `Module.UseResolvedReleaseVersion`, `Module.PackageDependencies`, or
`Module.Publish`.

## Build-Module Runtime Behavior

`Build-Module {}` should keep its current module-only behavior when no project/release segments are present.
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

## Implementation Slices

### Implemented slices

The first slices add the authoring, interchange, planning, and package-before-module execution contract:

- `New-ConfigurationProjectBuild` references existing `project.build.json` files from `Build-Module {}`.
- `New-ConfigurationPackageBuild` declares inline package build settings with first-class parameters for the
  current project-build options and `-Options` for future fields.
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
- `Release.VersionSource` and lane-level `UseAsReleaseVersionSource` can flow a project/package build version
  into the module build spec, manifest refresh, stage-root tokens, metadata, and GitHub tag.

1. Schema and model slice
   - Extend the native `PowerForgeReleaseSpec` when the broader release engine is ready to own this same flow.
   - Add module package dependency declarations.
   - Add gallery publish options to the native module release section.

2. Planner slice
   - Make `Invoke-PowerForgeRelease -Plan` produce a combined graph:
     packages, module, publish targets, staged assets, and GitHub assets.
   - Detect impossible graph states before building.
   - Refuse `PublishGitHub` at both package and top-level release scopes unless explicitly allowed.

3. Module publish slice
   - Teach native module release mode how to publish to PSGallery/private gallery from staged module artifacts.
   - Reuse existing `New-ConfigurationPublish` / repository profile resolution where possible.

4. Additional asset slice
   - Include optional package release zips when configured.

5. PSParseHTML proof
   - Add package/release segments to `Build/Build-Module.ps1`.
   - Optionally export `Build/release.json` from the same DSL for CI.
   - Keep `Build/Build-Project.ps1` thin.
   - Keep module-only builds working when publish/release segments are absent or disabled.
   - Validate with plan mode, local build mode, and publish dry-run/preflight.

## Validation Contract

Focused tests should prove contracts users care about:

- release planning orders package-before-module when module package dependencies are declared
- plan mode does not require already-created artifacts
- top-level GitHub publishing uses the combined staged module/package asset list
- package-level GitHub publishing is disabled in package configuration when top-level GitHub publishing is intended
- staged assets include module and NuGet outputs with category metadata
- resolved package version can flow into module version when requested
- local package feed injection is visible to the module build host without leaking into repo config

For PSParseHTML proof, the practical command sequence should be:

```powershell
.\Build\Build-Module.ps1 -Plan
.\Build\Build-Module.ps1 -PublishNuget:$false -PublishGallery:$false -PublishGitHub:$false
.\Build\Build-Module.ps1 -Validate
```

Only after that should a real publish run be attempted.

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

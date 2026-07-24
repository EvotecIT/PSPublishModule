# Project Build (Repository Pipeline)

This document describes the JSON configuration consumed by `Invoke-ProjectBuild` and the behavior it drives.
For the unified repo-level entrypoint that combines package and downloadable tool releases in one file,
see `Build/release.json` and `powerforge release`.
For module-plus-NuGet repository releases such as PSParseHTML, where one run should publish packages,
the PowerShell module, and one GitHub asset set, see
[Unified Module And Project Releases](PSPublishModule.UnifiedModuleProjectRelease.md).
For a PowerShell-first authoring layer proposal that keeps the same engine but avoids raw CLI argument shaping,
see `Docs/PSPublishModule.ProjectBuild.DslProposal.md`.

PowerShell-authored project release objects
- A first PowerShell-first slice is now available through:
  - `New-ConfigurationProjectRelease`
  - `New-ConfigurationProjectTarget`
  - `New-ConfigurationProjectSigning`
  - `New-ConfigurationProjectWorkspace`
  - `New-ConfigurationProjectOutput`
  - `New-ConfigurationProjectInstaller`
  - `New-ConfigurationProject`
  - `New-ProjectReleaseConfig`
  - `Export-ConfigurationProject`
  - `Import-ConfigurationProject`
  - `Invoke-ProjectRelease`
  - `Invoke-PowerForgeRelease -Project <ConfigurationProject>`
- This stays on the same unified release engine used by `powerforge release` and `Invoke-PowerForgeRelease -ConfigPath ...`.
- Relative target and installer paths are resolved from `ConfigurationProject.ProjectRoot` when provided.
- `New-ConfigurationProjectRelease` can now also carry default release intent such as:
  - `-PublishToolGitHub`
  - `-SkipRestore`
  - `-SkipBuild`
  - `-ToolOutput <Tool|Portable|Installer|Store>`
  - `-SkipToolOutput <...>`
- Project objects can now round-trip through JSON:
  - `Export-ConfigurationProject -Project $project -OutputPath '.\Build\project.release.json'`
  - `Import-ConfigurationProject -Path '.\Build\project.release.json'`
- Starter JSON can now be scaffolded directly:
  - `New-ProjectReleaseConfig -ProjectRoot '.' -PassThru`
  - `New-ProjectReleaseConfig -Project '.\src\App\App.csproj' -Portable -Force`
- In the current first slice, tool/app targets should still declare an explicit runtime for DotNetPublish-backed plan/build flows.

Example:

```powershell
Import-Module PSPublishModule -Force

$release = New-ConfigurationProjectRelease -Configuration Release
$signing = New-ConfigurationProjectSigning -Mode OnDemand
$output = New-ConfigurationProjectOutput -StageRoot '.\Artifacts\DslSmoke'
$target = New-ConfigurationProjectTarget `
    -Name 'PowerForgeCli' `
    -ProjectPath '.\PowerForge.Cli\PowerForge.Cli.csproj' `
    -Runtime 'win-x64' `
    -Framework 'net10.0' `
    -Style PortableCompat `
    -OutputType Tool, Portable

$project = New-ConfigurationProject `
    -Name 'PSPublishModule' `
    -ProjectRoot (Get-Location).Path `
    -Release $release `
    -Signing $signing `
    -Output $output `
    -Target $target

Invoke-ProjectRelease -Project $project -Plan
```

For module help/docs generation workflow (`Invoke-ModuleBuild`, `New-ConfigurationDocumentation`, `about_*` topics),
see `Docs/PSPublishModule.ModuleDocumentation.md`.
For project-specific actions that run at stable module pipeline stages, see
`Docs/PSPublishModule.ModuleLifecycleActions.md`.

Schema
- Location: `Schemas/project.build.schema.json`

Unified release entrypoint
- Schema: `Schemas/powerforge.release.schema.json`
- Scaffolder: `New-PowerForgeReleaseConfig -ProjectRoot . -PassThru`
- PowerShell cmdlet: `Invoke-PowerForgeRelease -ConfigPath .\Build\release.json`
- Wrapper: `Build/Build-Project.ps1`
- Transitional top-level wrapper: `Build/Build-Release.ps1`
- Preview tool wrapper: `Build/Build-ToolsPreview.ps1`
- CLI: `powerforge release --config .\Build\release.json`
- Packages continue to use `project.build.json` / `Invoke-ProjectBuild`.
- Tools/apps can now use either legacy `Tools.Targets` or the richer `Tools.DotNetPublish` / `Tools.DotNetPublishConfigPath`
  path backed by `Schemas/powerforge.dotnetpublish.schema.json`.
- This repo now also includes a focused preview-binary config in `Build/release.tools-preview.json`.
  Use it when you want to publish `PowerForge` / `PowerForgeWeb` executables without also touching module/package release flow.
  The preview config intentionally:
  - limits release scope to tools only
  - stages into `Artifacts/Preview`
  - marks GitHub releases as prerelease
  - uses stable `-preview` tags per tool/version so reruns can reuse the same release and resume missing asset uploads
  - keeps the existing local `TokenFilePath` pattern from `Build/release.json`; replace that path if you run the preview flow from another machine or CI environment
- Module release can now be declared directly in `release.json` through the top-level `Module` section.
  In this repo that section shells out to `Module/Build/Build-Module.ps1` and stages the declared artefact folders.
- Repositories that need one version across NuGet packages, a PowerShell module, and executable outputs can set
  `Module.SynchronizeVersionWithPackages: true` and identify `Module.VersionPrimaryProject`.
  Keep `Module.IncludesPackages: false` so the package lane has one owner. The package plan receives the module's
  next version as a floor, resolves the configured primary package project, and then passes that shared version to
  the module and executable lanes. During a real release this first pass is plan-only; NuGet execution is deferred
  until the module and executable builds succeed. Use `Packages.AlignPackageVersions: true` when all package projects
  must share the version.
- Interactive PowerShell and CLI runs render the same phase plan, live elapsed time, and final release summary.
  Redirected, verbose, quiet, no-color, and JSON runs keep line-oriented or structured output for CI and agents.
- `Build/Build-Release.ps1` still supports bridge mode for repos that have not adopted a native `Module` section yet,
  but it automatically defers to `release.json` when `Module` is present.
- When native `Module` mode is active, the same day-to-day overrides from `Build/Build-Release.ps1` can still flow into
  the module script:
  - `-NoDotnetBuild`
  - `-ModuleVersion <version>`
  - `-PreReleaseTag <tag>`
  - `-NoSign`
  - `-SignModule`
- Unified release can also declare a reusable workspace preflight via `WorkspaceValidation`
  backed by `workspace.validation.json` and `powerforge workspace validate`.
- Common release-time overrides:
  - `--configuration Debug|Release`
  - `--module-no-dotnet-build`, `--module-version`, `--module-prerelease-tag`, `--module-no-sign`, `--module-sign`
  - `--skip-workspace-validation`, `--workspace-config`, `--workspace-profile`
  - `--workspace-enable-feature`, `--workspace-disable-feature`, `--workspace-external-root <name=path>`
  - `--target`, `--rid`, `--framework`, `--style`
  - `--tool-output <Tool|Portable|Installer|Store>` and `--skip-tool-output <...>` when the unified release should keep the high-level release intent simple while PowerForge decides which internal DotNetPublish steps still need to run
  - `--skip-restore` and `--skip-build` for DotNetPublish-backed tool/app flows
  - `--output-root <path>` to remap DotNetPublish tool/app artefacts, manifests, bundle outputs, and installer staging under a different root
  - `--stage-root <path>` to copy unified release assets into a categorized release folder (`modules`, `nuget`, `portable`, `installer`, `tools`, `metadata`) and write `release-manifest.json` / `SHA256SUMS.txt` there by default
  - `Outputs.Staging` in `release.json` for default folder names when you want the same categorized layout without repeating CLI switches
  - `Outputs.Staging.*Path` values may point multiple categories at the same folder when you want a flat `UploadReady` layout such as `NuGet` + `GitHub`
  - `Outputs.Staging.*NameTemplate` values let the staged copies use release-facing names instead of raw internal build names
- `Winget` in `release.json` when you want PowerForge to emit portable/signed release manifests from the same staged assets
  - set `Winget.Submit: true` (or pass `--submit-winget`) to submit the generated manifests with `wingetcreate` after the release assets are available
- top-level `GitHub` in `release.json` when you want the unified staged release itself uploaded as one repo release instead of using package-host or per-target tool release publishing
  - `--keep-symbols` for symbol-preserving tool/app outputs
  - `--skip-release-checksums` when you want the staged release folder but do not want a top-level `SHA256SUMS.txt`
  - `--sign`, `--sign-profile`, and raw overrides such as `--sign-thumbprint`, `--sign-subject-name`, `--sign-timestamp-url`, `--sign-tool-path`
  - `--sign-on-missing-tool` and `--sign-on-failure` (`Warn|Fail|Skip`) for shared signing policy control
  - `--package-sign-thumbprint`, `--package-sign-store`, and `--package-sign-timestamp-url` for package-signing overrides without editing `release.json`
  - signing now emits a heuristic interaction hint when PowerForge can infer the certificate provider:
    likely hardware-token/smart-card vs likely local software-backed certificate

Starter flow
- Generate a unified release config from existing repo configs:

```powershell
New-PowerForgeReleaseConfig -ProjectRoot . -PassThru
```

- The generated `release.json` now includes:
  - a native `Module` section pointing at `Module/Build/Build-Module.ps1`
  - default module artefact roots for `Packed`, `PackedWithModules`, and `Unpacked`
  - the existing package/tool sections when matching source configs are present
- The `Module` section can optionally carry module-specific defaults too:
  - `NoDotnetBuild`
  - `ModuleVersion`
  - `PreReleaseTag`
  - `NoSign`
  - `SignModule`

- Plan a release with the generated config:

```powershell
Invoke-PowerForgeRelease -ConfigPath .\Build\release.json -Plan
```

Example staging layout for release uploads:

```json
"Outputs": {
  "Staging": {
    "RootPath": "Artifacts/UploadReady",
    "PackagesPath": "NuGet",
    "PortablePath": "GitHub",
    "InstallerPath": "GitHub",
    "PackagesNameTemplate": "{PackageId}.{Version}{Extension}",
    "PortableNameTemplate": "{Target}-{Version}-{Runtime}-portable{Extension}",
    "InstallerNameTemplate": "{Target}-{Version}-{Runtime}-installer{Extension}"
  }
}
```

- checksums written for staged releases now follow the staged filenames/paths, not the raw internal build outputs

Example Winget generation from staged assets:

```json
"Winget": {
  "Enabled": true,
  "OutputPath": "Artifacts/UploadReady/Winget",
  "InstallerUrlTemplate": "https://github.com/ExampleOrg/ExampleApp/releases/download/v{PackageVersion}/{FileName}",
  "Submit": true,
  "Submission": {
    "Mode": "Manifest",
    "TokenEnvName": "WINGET_CREATE_GITHUB_TOKEN",
    "PullRequestTitle": "Submit {PackageIdentifier} {PackageVersion}",
    "NoOpen": true
  },
  "Packages": [
    {
      "PackageIdentifier": "ExampleOrg.ExampleApp.Tray",
      "PackageVersion": "1.0.0",
      "Publisher": "ExampleOrg",
      "PackageName": "ExampleApp Tray",
      "License": "MIT",
      "ShortDescription": "Windows tray app for ExampleApp.",
      "Installers": [
        {
          "Category": "Portable",
          "Target": "ExampleApp.Tray",
          "Runtime": "win-x64",
          "InstallerType": "zip",
          "NestedInstallerType": "portable",
          "RelativeFilePath": "ExampleApp.Tray.exe"
        }
      ]
    }
  ]
}
```

- `InstallerUrlTemplate` tokens are URL-encoded automatically
- `{FileName}` resolves to the staged filename after any `*NameTemplate` rewrite, not the raw internal artifact filename
- when `--stage-root` (or `Outputs.Staging.RootPath`) is active, a relative `Winget.OutputPath` is resolved under that active staged release root so per-run `UploadReady\<release-id>\Winget` layouts work without hard-coded absolute paths
- set `NestedInstallerType` explicitly for archive-based installers when you want a reusable config that is not implicitly “portable zip only”
- `Winget.Submission.Mode: "Manifest"` submits the generated YAML via `wingetcreate submit <manifest>`, while `"Update"` calls `wingetcreate update <PackageIdentifier> --urls ... --version ... --submit` for existing Winget packages
- `Winget.Submission.TokenEnvName` defaults to `WINGET_CREATE_GITHUB_TOKEN`; `TokenFilePath` is also supported, and inline `Token` exists only for temporary/manual use because command-line token arguments can be logged by external tooling
- `Winget.Submission.NoOpen` defaults to `true` so CI runs do not try to open a browser; pass `--winget-open-browser` for interactive desktop runs
- command-line overrides include `--submit-winget`, `--skip-winget-submit`, `--winget-submit-mode Manifest|Update`, `--winget-tool-path`, `--winget-token-env`, `--winget-token-file`, `--winget-pr-title`, `--winget-replace [version]`, `--winget-open-browser`, and `--winget-allow-interactive-auth`

Example unified GitHub release publishing from staged assets:

```json
"GitHub": {
  "Publish": true,
  "TagTemplate": "v{Version}",
  "ReleaseNameTemplate": "{Repository} {Version}"
}
```

- use `--publish-project-github` (or set `GitHub.Publish: true`) to upload the unified staged release as one repo release
- when top-level `GitHub` is active, package-host GitHub publishing is suppressed and the staged release assets are uploaded instead
- uploaded assets include staged `NuGet`, `Portable`, `Installer`, `Tool`, metadata files, top-level `release-manifest.json` / `SHA256SUMS.txt`, and any generated Winget manifests

- Plan or build preview executables only:

```powershell
.\Build\Build-ToolsPreview.ps1 -Plan
.\Build\Build-ToolsPreview.ps1 -Runtime win-x64
.\Build\Build-ToolsPreview.ps1 -Runtime win-x64,linux-x64
```

- Publish preview executable releases to GitHub:

```powershell
.\Build\Build-ToolsPreview.ps1 -PublishGitHub
```

- Preview publish reruns are intentionally idempotent for the same tool version:
  - tags use the `-preview` suffix instead of a timestamp
  - GitHub release publish reuses the existing tag/release and skips assets that were already uploaded
  - `Build-ToolsPreview.ps1 -PublishGitHub` enables verbose output automatically so long multi-runtime uploads are visible

Overview
- The build pipeline discovers .NET projects, resolves versions, optionally updates csproj files,
  packs and signs NuGet packages, and can publish to NuGet and GitHub.
- A plan-only run can be produced with `PlanOnly` or `-Plan`, which writes the plan JSON without changing files.
- `Invoke-ProjectBuild` now treats publish checks from the plan pass as blocking preflight.
  NuGet and GitHub prechecks are evaluated before any real publish starts.

Example configuration
```
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/project.build.schema.json",
  "RootPath": "..",
  "VersionTracks": {
    "ExampleSuite": {
      "ExpectedVersion": "1.0.X",
      "AnchorProject": "ExampleSuite.Core",
      "Projects": [
        "ExampleSuite.Csv",
        "ExampleSuite.Excel",
        "ExampleSuite.Markdown"
      ]
    }
  },
  "ExpectedVersionMapAsInclude": true,
  "ExpectedVersionMapUseWildcards": false,
  "ExcludeProjects": [ "ExampleSuite.Legacy", "ExampleSuite.Experimental" ],
  "Configuration": "Release",
  "StagingPath": "Artefacts/ProjectBuild",
  "CleanStaging": true,
  "PlanOutputPath": "Artefacts/ProjectBuild/project.build.plan.json",
  "UpdateVersions": true,
  "Build": true,
  "PackStrategy": "MSBuild",
  "IncludeSymbols": true,
  "PublishNuget": true,
  "PublishGitHub": true,
  "CreateReleaseZip": true,
  "CertificateThumbprint": "THUMBPRINT",
  "CertificateStore": "CurrentUser",
  "PublishSource": "https://api.nuget.org/v3/index.json",
  "PublishApiKeyFilePath": "C:\\path\\to\\nuget-api-key.txt",
  "SkipDuplicate": true,
  "PublishFailFast": true,
  "GitHubAccessTokenFilePath": "C:\\path\\to\\github-token.txt",
  "GitHubUsername": "ExampleOrg",
  "GitHubRepositoryName": "ExampleSuite",
  "GitHubReleaseMode": "Single",
  "GitHubPrimaryProject": "ExampleSuite.Core",
  "GitHubTagTemplate": "{Repo}-v{Version}",
  "GitHubGenerateReleaseNotes": true
}
```

Discovery and selection
- `IncludeProjects`: only process named projects.
- `ExcludeProjects`: skip named projects even if discovered.
- `ExcludeDirectories`: skip project discovery under these directory names.
- Project discovery automatically stops at nested Git repository/worktree roots (for example review clones or `.claude/worktrees` entries inside the current repo).
- `ExpectedVersionMapAsInclude`: if true, only projects matching the map are included.
- `ExpectedVersionMapUseWildcards`: allows `*` and `?` in map keys.

Versioning
- `ExpectedVersion`: global version or X-pattern (e.g. `1.2.X`).
- `ExpectedVersionMap`: per-project overrides (`ProjectName` -> version/X-pattern).
- `VersionTracks`: anchor-driven version trains. Each track resolves one version from an anchor package/project and applies it to every project in the track.
- `VersionTracks.<Name>.AnchorProject`: project whose package identity is used as the default version source for the track.
- `VersionTracks.<Name>.AnchorPackageId`: optional explicit package identity when it differs from the project name.
- `VersionTracks.<Name>.Projects`: sibling projects that should be stamped to the same resolved version. `AnchorProject` is included automatically.
- When `AnchorPackageId` is used, also set `AnchorProject` so the anchor project itself is stamped automatically.
- `AlignPackageVersions`: defaults to false. When true, ordinary projects using the same X-pattern are stepped from the highest current package version found for any project in that group. For example, if one `2.0.X` package is at `2.0.5` and another is new, both plan `2.0.6`. Ordinary exact-version overrides outside a version track remain exact. `VersionTracks` already coordinate their members when alignment is false and remain explicit, separate group boundaries when it is true. This does not assign one universal version to every project. See [Unified Module And Project Releases](PSPublishModule.UnifiedModuleProjectRelease.md) for complete aligned, non-aligned, and version-track behavior.
- In a module pipeline with `Release.SynchronizeModuleVersion`, the next available module version is also a floor for `Release.PrimaryProject`. If that project belongs to an aligned X-pattern group, the whole group is raised to the floor. A higher numeric NuGet-derived package candidate still wins. At the same numeric version, a stable X-pattern candidate does not erase the configured module prerelease; explicit prerelease versions retain normal semantic-version ordering. The module and primary package patterns must describe the same version line.
- When no expected version is provided for a project, the existing csproj version is used.
- When both `VersionTracks` and `ExpectedVersionMap` are present, the explicit map wins for matching projects.
- `UpdateVersions`: when false, csproj files are not updated.
- Version source resolution can use `NugetSource` (v3 index URL or local folder) with optional credentials.

To keep an existing package family and newly added packages on one release version, enable alignment alongside the shared X-pattern:

```json
{
  "ExpectedVersion": "2.0.X",
  "ExpectedVersionMap": {
    "ExampleSuite.Core": "2.0.X",
    "ExampleSuite.Excel": "2.0.X",
    "ExampleSuite.NewPackage": "2.0.X"
  },
  "ExpectedVersionMapAsInclude": true,
  "AlignPackageVersions": true
}
```

- `PackStrategy`: optional packing strategy. `PerProject` runs a non-incremental build followed by `dotnet pack --no-build` for each project. `MSBuild` (alias `Batch`) requires `OutputPath` or `StagingPath`, generates a temporary traversal project, and runs `Restore;Rebuild;Pack` for selected projects in parallel. If no package output path is available, it logs a warning and falls back to `PerProject`. Both strategies verify primary managed assemblies under `lib/<tfm>`, `runtimes/<rid>/lib/<tfm>`, and `tools/<tfm>/any` against exact fresh build target directories before package signing or publishing. Native runtime assets and metadata-only packages are not assembly-hash targets. Private feed credentials must already be available to `dotnet`/MSBuild restore. Batch mode stops on the first project failure and treats the whole failed batch as failed, so already-produced packages from that batch are not signed or published. Projects without a resolved version are reported as failed skipped projects.

Staging and outputs
- `StagingPath`: root directory for pipeline outputs (recommended).
  - Packages go to `<StagingPath>\packages` when `OutputPath` is not set.
  - Release zips go to `<StagingPath>\releases` when `ReleaseZipOutputPath` is not set.
- When a project defines `<PackageId>`, project-build uses that package identity for NuGet version lookup,
  planned `.nupkg` names, and release zip names. Otherwise it falls back to the csproj file name.
- `CleanStaging`: if true, deletes the staging directory before a run. It does not clean project `bin`/`obj` directories; package correctness comes from the release rebuild and package-payload provenance check.
- `PlanOutputPath`: optional file path for a JSON plan output.
- `IncludeSymbols`: when true, produces portable `.snupkg` files for every packable project. Plan output includes their expected paths, and build output discovery rejects stale symbol packages just as it does stale primary packages.

NuGet publishing
- `PublishNuget`: enable `dotnet nuget push`.
- Portable `.snupkg` files produced by the same build are published immediately after their primary `.nupkg` and are included in unified staged release assets and manifests.
- `PublishApiKey` / `PublishApiKeyFilePath` / `PublishApiKeyEnvName`: API key sources.
- `PublishFailFast`: stop on first publish/signing error.
- `SkipDuplicate`: pass `--skip-duplicate` to `dotnet nuget push`.

GitHub releases
- `PublishGitHub`: enable GitHub release publishing.
- `GitHubReleaseMode`:
  - `Single`: one release with all project zips attached.
  - `PerProject`: one release per project.
- `GitHubPrimaryProject`: in single mode, chooses the version used in the tag/name.
  If multiple project versions exist and no primary is available, the tag uses the current date.
- `GitHubTagTemplate` and `GitHubReleaseName` support tokens:
  - `{Project}`, `{Version}`, `{PrimaryProject}`, `{PrimaryVersion}`, `{Repo}`, `{Repository}`, `{Date}`, `{UtcDate}`, `{DateTime}`, `{UtcDateTime}`, `{Timestamp}`, `{UtcTimestamp}`
  - `{Date}` and `{UtcDate}` are formatted `yyyy.MM.dd`.
- `GitHubTagConflictPolicy`:
  - `Reuse` (default): idempotent, reuse existing release/tag when it already exists.
    In `Single` mode, project-build now performs a GitHub precheck before any real publish.
    If the computed tag already exists and the planned asset set differs for a mixed-version package group,
    the run stops with an advisory instead of attaching new assets to the old release.
  - `Fail`: fail if tag already exists.
  - `AppendUtcTimestamp`: append `-yyyyMMddHHmmss` UTC suffix to computed tags.
- For mixed-version repositories, prefer one of these patterns:
  - `GitHubReleaseMode: "PerProject"` for one release per package version.
  - `GitHubTagConflictPolicy: "Fail"` or `AppendUtcTimestamp` to avoid silent reuse.
  - `GitHubTagTemplate: "{Repo}-v{UtcTimestamp}"` for unique per-run tags.

Signing
- `CertificateThumbprint`, `CertificateStore`, `TimeStampServer` control package signing.
- If a certificate cannot be found, the run fails before publishing.
- DotNetPublish-based unified release also emits a best-effort signing interaction note.
  It can often detect likely hardware-token providers such as SafeNet/eToken versus local software-backed certs,
  but it remains heuristic because final prompt behavior still depends on middleware and machine policy.

Plan mode
- `PlanOnly` (config) or `-Plan` (cmdlet): compute a plan without modifying files or publishing.
- The plan JSON includes resolved versions, packages that would be created, and publish decisions.
- In plan mode, publish preflight does not require package files to already exist on disk.

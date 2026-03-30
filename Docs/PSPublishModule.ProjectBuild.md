# Project Build (Repository Pipeline)

This document describes the JSON configuration consumed by `Invoke-ProjectBuild` and the behavior it drives.
For the unified repo-level entrypoint that combines package and downloadable tool releases in one file,
see `Build/release.json` and `powerforge release`.
For a PowerShell-first authoring layer proposal that keeps the same engine but avoids raw CLI argument shaping,
see `Docs/PSPublishModule.ProjectBuild.DslProposal.md`.

For module help/docs generation workflow (`Invoke-ModuleBuild`, `New-ConfigurationDocumentation`, `about_*` topics),
see `Docs/PSPublishModule.ModuleDocumentation.md`.

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
  - `--workspace-enable-feature`, `--workspace-disable-feature`, `--workspace-testimox-root`
  - `--target`, `--rid`, `--framework`, `--style`
  - `--tool-output <Tool|Portable|Installer|Store>` and `--skip-tool-output <...>` when the unified release should keep the high-level release intent simple while PowerForge decides which internal DotNetPublish steps still need to run
  - `--skip-restore` and `--skip-build` for DotNetPublish-backed tool/app flows
  - `--output-root <path>` to remap DotNetPublish tool/app artefacts, manifests, bundle outputs, and installer staging under a different root
  - `--stage-root <path>` to copy unified release assets into a categorized release folder (`modules`, `nuget`, `portable`, `installer`, `tools`, `metadata`) and write `release-manifest.json` / `SHA256SUMS.txt` there by default
  - `Outputs.Staging` in `release.json` for default folder names when you want the same categorized layout without repeating CLI switches
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
    "OfficeIMO": {
      "ExpectedVersion": "1.0.X",
      "AnchorProject": "OfficeIMO.Word",
      "Projects": [
        "OfficeIMO.CSV",
        "OfficeIMO.Excel",
        "OfficeIMO.Markdown"
      ]
    }
  },
  "ExpectedVersionMapAsInclude": true,
  "ExpectedVersionMapUseWildcards": false,
  "ExcludeProjects": [ "OfficeIMO.Visio", "OfficeIMO.Project" ],
  "Configuration": "Release",
  "StagingPath": "Artefacts/ProjectBuild",
  "CleanStaging": true,
  "PlanOutputPath": "Artefacts/ProjectBuild/project.build.plan.json",
  "UpdateVersions": true,
  "Build": true,
  "PublishNuget": true,
  "PublishGitHub": true,
  "CreateReleaseZip": true,
  "CertificateThumbprint": "THUMBPRINT",
  "CertificateStore": "CurrentUser",
  "PublishSource": "https://api.nuget.org/v3/index.json",
  "PublishApiKeyFilePath": "C:\\Support\\Important\\NugetOrgEvotec.txt",
  "SkipDuplicate": true,
  "PublishFailFast": true,
  "GitHubAccessTokenFilePath": "C:\\Support\\Important\\GithubAPI.txt",
  "GitHubUsername": "EvotecIT",
  "GitHubRepositoryName": "OfficeIMO",
  "GitHubReleaseMode": "Single",
  "GitHubPrimaryProject": "OfficeIMO.Word",
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
- When no expected version is provided for a project, the existing csproj version is used.
- When both `VersionTracks` and `ExpectedVersionMap` are present, the explicit map wins for matching projects.
- `UpdateVersions`: when false, csproj files are not updated.
- Version source resolution can use `NugetSource` (v3 index URL or local folder) with optional credentials.

Staging and outputs
- `StagingPath`: root directory for pipeline outputs (recommended).
  - Packages go to `<StagingPath>\packages` when `OutputPath` is not set.
  - Release zips go to `<StagingPath>\releases` when `ReleaseZipOutputPath` is not set.
- When a project defines `<PackageId>`, project-build uses that package identity for NuGet version lookup,
  planned `.nupkg` names, and release zip names. Otherwise it falls back to the csproj file name.
- `CleanStaging`: if true, deletes the staging directory before a run.
- `PlanOutputPath`: optional file path for a JSON plan output.

NuGet publishing
- `PublishNuget`: enable `dotnet nuget push`.
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
  - `GitHubTagTemplate: "{Repo}-v{UtcTimestamp}"` for unique per-run tags (OfficeIMO-style).

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

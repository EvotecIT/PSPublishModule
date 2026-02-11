# Project Build (Repository Pipeline)

This document describes the JSON configuration consumed by `Invoke-ProjectBuild` and the behavior it drives.

Schema
- Location: `schemas/project.build.schema.json`

Overview
- The build pipeline discovers .NET projects, resolves versions, optionally updates csproj files,
  packs and signs NuGet packages, and can publish to NuGet and GitHub.
- A plan-only run can be produced with `PlanOnly` or `-Plan`, which writes the plan JSON without changing files.

Example configuration
```
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/schemas/project.build.schema.json",
  "RootPath": "..",
  "ExpectedVersionMap": {
    "OfficeIMO.CSV": "0.1.X",
    "OfficeIMO.Excel": "0.6.X",
    "OfficeIMO.Markdown": "0.5.X",
    "OfficeIMO.Word": "1.0.X"
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
- `ExpectedVersionMapAsInclude`: if true, only projects matching the map are included.
- `ExpectedVersionMapUseWildcards`: allows `*` and `?` in map keys.

Versioning
- `ExpectedVersion`: global version or X-pattern (e.g. `1.2.X`).
- `ExpectedVersionMap`: per-project overrides (`ProjectName` -> version/X-pattern).
- When no expected version is provided for a project, the existing csproj version is used.
- `UpdateVersions`: when false, csproj files are not updated.
- Version source resolution can use `NugetSource` (v3 index URL or local folder) with optional credentials.

Staging and outputs
- `StagingPath`: root directory for pipeline outputs (recommended).
  - Packages go to `<StagingPath>\packages` when `OutputPath` is not set.
  - Release zips go to `<StagingPath>\releases` when `ReleaseZipOutputPath` is not set.
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
  - `Fail`: fail if tag already exists.
  - `AppendUtcTimestamp`: append `-yyyyMMddHHmmss` UTC suffix to computed tags.

Signing
- `CertificateThumbprint`, `CertificateStore`, `TimeStampServer` control package signing.
- If a certificate cannot be found, the run fails before publishing.

Plan mode
- `PlanOnly` (config) or `-Plan` (cmdlet): compute a plan without modifying files or publishing.
- The plan JSON includes resolved versions, packages that would be created, and publish decisions.
- In plan mode, publish preflight does not require package files to already exist on disk.

# Module Lifecycle Actions

Module lifecycle actions let a build run project-specific PowerShell at stable points in the module pipeline.
Use them when a project needs a small preparation, validation, generated-file update, or release guard that belongs
inside the build flow but does not belong in the reusable PowerForge engine.

Actions are declared with `New-ConfigurationExecute` in the same `Invoke-ModuleBuild` / `Build-Module` settings block as
the rest of the module configuration. The physical order of DSL statements does not decide when actions run. The `-At`
stage decides the order.

## When To Use

Good uses:

- create or update project-local generated files after staging
- inspect the staged module before manifest refresh
- run a repository release guard before publish
- write additional diagnostics after packaging or publishing
- run advisory checks with `-ContinueOnError`

Prefer a reusable PowerForge feature instead of a lifecycle action when the behavior should be shared by multiple
repositories, affects module dependency semantics, owns packaging/signing/versioning rules, or needs stable typed result
contracts beyond the action result.

## Basic Inline Action

```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {
    New-ConfigurationBuild -Enable -MergeModuleOnBuild

    New-ConfigurationExecute `
        -Name 'Inspect staged module' `
        -At AfterStaging `
        -InlineScript @'
$ctx = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT | ConvertFrom-Json
Get-ChildItem -LiteralPath $ctx.ModuleRoot -Recurse |
    Select-Object -First 10 FullName
'@
}
```

## Script File Action

```powershell
Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {
    New-ConfigurationBuild -Enable -MergeModuleOnBuild

    New-ConfigurationExecute `
        -Name 'Release guard' `
        -At BeforePublish `
        -FilePath '.\Build\Test-ReleaseReady.ps1' `
        -TimeoutSeconds 120
}
```

Relative `-FilePath` and `-WorkingDirectory` values resolve from the module project root.

## Context Contract

Before each action runs, PowerForge writes a JSON context file and sets `POWERFORGE_CONTEXT` to that path. Scripts should
read the context file instead of inferring paths from the current directory.

Common fields:

- `SchemaVersion`
- `Stage`
- `ActionName`
- `ModuleName`
- `ProjectRoot`
- `ExpectedVersion`
- `ResolvedVersion`
- `PreRelease`
- `StagingPath`
- `ManifestPath`
- `ModuleRoot`
- `DocumentationPath`
- `DocumentationReadmePath`
- `ArtefactPaths`
- `PublishDestinations`
- `ContextPath`

PowerForge also passes these environment variables:

- `POWERFORGE_CONTEXT`
- `POWERFORGE_ACTION_STAGE`
- `POWERFORGE_ACTION_NAME`
- `POWERFORGE_MODULE_NAME`
- `POWERFORGE_PROJECT_ROOT`
- `POWERFORGE_STAGING_PATH`
- `POWERFORGE_MANIFEST_PATH`
- `POWERFORGE_RESOLVED_VERSION`

Configured environment variables from `-Environment` are added before those PowerForge variables, so the built-in
context variables remain authoritative.

## Stages

Actions can run at these lifecycle points:

- `BeforeDependencies`, `AfterDependencies`
- `BeforeVersioning`, `AfterVersioning`
- `BeforeStaging`, `AfterStaging`
- `BeforeBuild`, `AfterBuild`
- `BeforeManifest`, `AfterManifest`
- `BeforeDocumentation`, `AfterDocumentation`
- `BeforeFormatting`, `AfterFormatting`
- `BeforeValidation`, `AfterValidation`
- `BeforeTests`, `AfterTests`
- `BeforeSigning`, `AfterSigning`
- `BeforeArtefacts`, `AfterArtefacts`
- `BeforePublish`, `AfterPublish`
- `BeforeInstall`, `AfterInstall`

Choose the earliest stage that has the context your script needs. For example, `AfterStaging` has `StagingPath` and
`ModuleRoot`, while `AfterManifest` also has `ManifestPath`.

## Advisory Actions

By default, a failed action fails the build. Use `-ContinueOnError` for checks that should be reported but should not
block the pipeline yet.

```powershell
New-ConfigurationExecute `
    -Name 'Advisory docs check' `
    -At AfterDocumentation `
    -FilePath '.\Build\Test-DocsShape.ps1' `
    -ContinueOnError
```

The pipeline result exposes `ActionResults`, including exit code, stdout, stderr, context path, and whether the pipeline
continued after a failure.

## How To: Update A Generated File In Staging

```powershell
New-ConfigurationExecute `
    -Name 'Write build stamp' `
    -At AfterStaging `
    -InlineScript @'
$ctx = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT | ConvertFrom-Json
$stampPath = Join-Path $ctx.ModuleRoot 'BuildStamp.txt'
"$($ctx.ModuleName) $($ctx.ResolvedVersion)" | Set-Content -LiteralPath $stampPath -Encoding UTF8
'@
```

## How To: Block Publish Unless A Release File Exists

```powershell
New-ConfigurationExecute `
    -Name 'Release notes guard' `
    -At BeforePublish `
    -InlineScript @'
$ctx = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT | ConvertFrom-Json
$releaseNotes = Join-Path $ctx.ProjectRoot 'ReleaseNotes.md'
if (-not (Test-Path -LiteralPath $releaseNotes)) {
    throw "ReleaseNotes.md is required before publishing $($ctx.ModuleName) $($ctx.ResolvedVersion)."
}
'@
```

## Design Notes

Lifecycle actions are intentionally contextual rather than positional. Multiple actions may target the same stage; they
run in the order they appear in the configuration after planning filters out disabled actions.

Keep action scripts small and project-owned. If an action starts to own shared packaging, dependency, signing, versioning,
or documentation behavior, move that behavior into PowerForge and leave the action as a thin project-specific call.

# PowerShell-First Project Build DSL Proposal

This proposal describes a PowerShell-first authoring layer for project and application builds that feels closer to `Invoke-ModuleBuild` / `New-Configuration*` than to raw CLI argument assembly.

The goal is not to invent a custom scripting language. The goal is to let repositories describe project/release intent in ordinary PowerShell cmdlets and typed objects while PowerForge continues to own the real execution engine.

## Goal

Provide a project-build authoring experience that:

- feels native to PowerShell
- stays close to the existing `New-Configuration*` model in `PSPublishModule`
- maps to the same PowerForge release and DotNetPublish models already used by JSON config
- reduces thin-wrapper noise such as local `Add-Flag` / `Add-Option` helpers

## Current Status

The first implementation slice is now in the module.

Available cmdlets:

- `New-ConfigurationProjectRelease`
- `New-ConfigurationProjectTarget`
- `New-ConfigurationProjectSigning`
- `New-ConfigurationProjectWorkspace`
- `New-ConfigurationProjectOutput`
- `New-ConfigurationProjectInstaller`
- `New-ConfigurationProject`
- `Invoke-ProjectRelease`
- `Invoke-PowerForgeRelease -Project <ConfigurationProject>`

Current scope:

- tool/app release flows backed by the unified PowerForge release engine
- PowerShell-authored object composition instead of raw CLI argument shaping
- path resolution relative to `ProjectRoot`
- installer entries that map onto the existing DotNetPublish installer flow
- release-level defaults for `PublishToolGitHub`, `SkipRestore`, `SkipBuild`, `ToolOutput`, and `SkipToolOutput`

Current intentional limits:

- this first slice does not add a new standalone `Invoke-ProjectBuild` cmdlet yet
- workspace validation still needs an explicit config path when the repo wants that lane
- tool/app targets should currently declare an explicit runtime

## Non-Goals

- no bare-keyword DSL like `Tool {}` / `Signing {}`
- no second execution engine written in PowerShell
- no repo-specific product policy in the shared engine
- no requirement to replace JSON-based config for CI or headless use

## Problem

Today many repos still use thin PowerShell wrappers that manually assemble `powerforge release` CLI arguments.

That has a few drawbacks:

- repo scripts still own too much command-shaping detail
- optional parameters are easy to forward incorrectly
- users see plumbing details like `--sign-thumbprint` rather than domain concepts
- the experience is less declarative than module build configuration

By contrast, `Invoke-ModuleBuild` works well because users mostly declare intent through `New-Configuration*` cmdlets and let the module handle the mechanics.

## Design Principles

1. PowerShell-first, not language-like

- Use normal cmdlet names and typed objects.
- Prefer explicit nouns like `ProjectTarget` or `ProjectSigning`.
- Avoid custom keywords or magical parser behavior.

2. C# remains the source of truth

- The PowerShell layer should produce the same internal request/config models used by JSON and CLI flows.
- PowerShell should not become a separate planner or runner.

3. JSON and PowerShell should coexist

- PowerShell is the friendly authoring surface.
- JSON remains valid for CI, generated configs, and schema-driven tooling.
- Either representation should be able to map to the same internal shape.

4. Reusable mechanics only

- Shared concepts belong in PowerForge / PSPublishModule.
- Product policy stays in each consuming repo.

## Proposed Shape

### Minimal set

The smallest useful command set is:

- `New-ConfigurationProjectRelease`
- `New-ConfigurationProjectTarget`
- `New-ConfigurationProjectSigning`
- `New-ConfigurationProject`
- `Invoke-ProjectRelease`
- `Invoke-PowerForgeRelease -Project`

Example:

```powershell
Import-Module PSPublishModule -Force

$release = New-ConfigurationProjectRelease `
    -Configuration Release `
    -SkipRestore `
    -ToolOutput Portable

$target = New-ConfigurationProjectTarget `
    -Name 'ChatApp' `
    -ProjectPath '.\IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj' `
    -Runtime 'win-x64' `
    -Framework 'net8.0-windows10.0.26100.0' `
    -Style 'PortableCompat' `
    -OutputType Portable, Installer

$signing = New-ConfigurationProjectSigning `
    -Mode OnDemand `
    -TimestampUrl 'http://timestamp.digicert.com' `
    -Description 'IntelligenceX Chat'

$project = New-ConfigurationProject `
    -Name 'IntelligenceX' `
    -Release $release `
    -Target $target `
    -Signing $signing

Invoke-ProjectRelease -Project $project -Plan
```

This is intentionally plain PowerShell:

- no custom keywords
- no freeform nested DSL syntax
- easy to debug with normal variables and splats

### Recommended medium set

The more practical version adds a small number of composition cmdlets:

- `New-ConfigurationProject`
- `New-ConfigurationProjectRelease`
- `New-ConfigurationProjectTarget`
- `New-ConfigurationProjectSigning`
- `New-ConfigurationProjectWorkspace`
- `New-ConfigurationProjectOutput`
- `New-ConfigurationProjectInstaller`
- `Invoke-ProjectRelease`
- `Invoke-PowerForgeRelease -Project`

Example:

```powershell
$workspace = New-ConfigurationProjectWorkspace `
    -Profile oss `
    -ConfigPath '.\Build\workspace.validation.json'

$output = New-ConfigurationProjectOutput `
    -StageRoot '.\Artifacts\Release'

$target = New-ConfigurationProjectTarget `
    -Name 'ChatApp' `
    -ProjectPath '.\IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj' `
    -Runtime 'win-x64' `
    -Framework 'net8.0-windows10.0.26100.0' `
    -Style 'PortableCompat' `
    -OutputType Portable, Installer

$signing = New-ConfigurationProjectSigning `
    -Mode OnDemand `
    -TimestampUrl 'http://timestamp.digicert.com'

$project = New-ConfigurationProject `
    -Name 'IntelligenceX' `
    -Release (New-ConfigurationProjectRelease -Configuration Release) `
    -Workspace $workspace `
    -Output $output `
    -Target $target `
    -Signing $signing

Invoke-ProjectRelease -Project $project -Plan
```

This is likely the best balance between:

- reuse
- readability
- PowerShell ergonomics
- alignment with current `PSPublishModule` patterns

## Suggested Command Responsibilities

### `New-ConfigurationProjectRelease`

Current first-slice responsibility:

- `Configuration`
- `PublishToolGitHub`
- `SkipRestore`
- `SkipBuild`
- `ToolOutput`
- `SkipToolOutput`

Future expansion could add run-level choices such as:

- `PackagesOnly`
- `ToolsOnly`
- `WorkspaceProfile`
- `PublishNuget`
- `PublishProjectGitHub`
- `PublishToolGitHub`

### `New-ConfigurationProjectTarget`

Owns per-target build intent such as:

- `Name`
- `ProjectPath`
- `Runtime`
- `Framework`
- `Style`
- `OutputType`
- `KeepSymbols`
- `KeepDocs`
- `ReadyToRun`

### `New-ConfigurationProjectSigning`

Owns signing policy, not certificate mechanics leakage by default:

- `Mode` (`Disabled`, `OnDemand`, `Enabled`)
- `Profile`
- `Thumbprint`
- `SubjectName`
- `TimestampUrl`
- `Description`
- `OnMissingTool`
- `OnFailure`

The DSL should keep signing high-level by default. Raw sign-tool fields can still exist, but they should be treated as advanced overrides.

### `New-ConfigurationProjectWorkspace`

Owns repo preflight and workspace validation intent:

- `Profile`
- `SkipValidation`
- `EnableFeature`
- `DisableFeature`
- `ConfigPath`

Future expansion could add broader preflight choices when there is a reusable engine contract for them.

### `New-ConfigurationProjectOutput`

Owns staged outputs and layout:

- `OutputRoot`
- `StageRoot`
- `ManifestJsonPath`
- `ChecksumsPath`
- `NoChecksums`

### `Invoke-PowerForgeRelease -Project`

Consumes the typed objects and translates them into the engine request model.

It should:

- support `-Plan`
- support `-Validate`
- support a direct `-Project` object
- optionally accept a `-ConfigPath` bridge for JSON interop

It should not:

- become another custom orchestration language
- reimplement PowerForge planning logic in PowerShell

### `Invoke-ProjectRelease`

This is the friendlier top-level wrapper for the same object model.

It should:

- feel closer to `Invoke-ModuleBuild`
- keep project-object authoring front and center
- stay thin over the same PowerForge release engine

## Mapping to Existing Engine

The proposal should map onto existing PowerForge concepts rather than introduce new execution concepts.

Likely mapping:

- `New-ConfigurationProject*` objects map to `PowerForgeReleaseRequest` plus `PowerForgeReleaseSpec`-like structures
- `ProjectTarget` maps to the current DotNetPublish-backed release target shape
- `ProjectSigning` maps to the existing signing override model
- `ProjectWorkspace` maps to the current workspace validation and feature-toggle inputs
- JSON export/import can later target the same release schema or a closely aligned project-build schema

## Why Not a "Real DSL"

Historically, deeply nested PowerShell DSLs often look elegant to authors but become harder for other contributors to:

- autocomplete
- debug
- inspect
- refactor
- reason about without reading framework internals

That is why this proposal prefers:

- `New-Configuration*` cmdlets
- plain variables
- splatting
- normal PowerShell object flow

This keeps the authoring model declarative without feeling alien.

## Migration Strategy

1. Introduce the minimal typed objects and `Invoke-PowerForgeRelease -Project`.
2. Make them translate to the existing PowerForge release engine.
3. Keep JSON fully supported.
4. Migrate one real repo wrapper from manual CLI arg assembly to the new object model.
5. Only expand the DSL surface after a second repo confirms the shape is reusable.

## Recommended First Slice

Start with:

- `New-ConfigurationProjectRelease`
- `New-ConfigurationProjectTarget`
- `New-ConfigurationProjectSigning`
- `New-ConfigurationProjectWorkspace`
- `New-ConfigurationProject`
- `Invoke-PowerForgeRelease -Project`

That is enough to make project builds feel more like `Invoke-ModuleBuild` without over-designing the surface too early.

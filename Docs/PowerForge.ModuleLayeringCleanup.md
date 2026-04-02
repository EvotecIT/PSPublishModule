# PowerForge Module Layering Cleanup

Last updated: 2026-04-02

## Goal

Clean up the current `PowerForge` vs `PowerForge.PowerShell` ownership split so that:

- `PowerForge` owns reusable contracts, models, planners, filesystem/process orchestration, and host-neutral build/publish logic.
- `PowerForge.PowerShell` owns only behavior that truly depends on PowerShell runtime concepts, SMA AST/runspaces, `ScriptBlock`, `PSObject`, `Get-Help`, `Invoke-Pester`, PowerShell repository tooling, or Authenticode cmdlets.
- `PSPublishModule`, `PowerForge.Cli`, and PowerForge Studio can reuse the same core contracts without needing to take an unnecessary dependency on `PowerForge.PowerShell`.

## Current State

- `PowerForge/PowerForge.csproj` removes the module pipeline files from the core assembly with `Compile Remove`.
- `PowerForge.PowerShell/PowerForge.PowerShell.csproj` then compiles those same files via linked includes from `..\PowerForge\...`.
- `PowerForge.Cli/PowerForge.Cli.csproj` references both `PowerForge` and `PowerForge.PowerShell`, which means the CLI currently needs the PowerShell-owned assembly for build/pipeline features.
- `PowerForgeStudio.Orchestrator/PowerForgeStudio.Orchestrator.csproj` references `PowerForge` only. That is the strongest reason to make the reusable pipeline surface live in core again.

## Classification

### Core Now

These either have no real PowerShell runtime dependency, or are plain contracts/results that should be shareable immediately.

| File(s) | Target | Why | First move |
|---|---|---|---|
| `Models\ArtefactBuildResult.cs` | `PowerForge` | Plain result DTO used by CLI JSON output. | Move ownership back to core unchanged. |
| `Models\ModuleInformation.cs` | `PowerForge` | Plain manifest/module metadata DTO. | Move unchanged. |
| `Models\ModulePipelineDiagnosticsPolicyException.cs` | `PowerForge` | Exception wrapper over core pipeline result types. | Move unchanged. |
| `Models\ModulePipelinePlan.cs` | `PowerForge` | Core planning contract already serialized by CLI. | Move unchanged. |
| `Models\ModulePipelineResult.cs` | `PowerForge` | Core execution result contract consumed by CLI/UI layers. | Move unchanged. |
| `Models\ModulePipelineStep.cs` | `PowerForge` | Planning/progress contract, no SMA coupling. | Move unchanged. |
| `Models\ModulePublishResult.cs` | `PowerForge` | Publish result DTO; host-neutral shape. | Move unchanged. |
| `Models\ModuleTestSuiteResult.cs` | `PowerForge` | Plain execution result DTO even if produced by PowerShell-backed runners. | Move unchanged. |
| `Models\ProjectCleanupOutput.cs` | `PowerForge` | Plain cleanup result DTO. | Move unchanged. |
| `Models\ProjectCleanupSpec.cs` | `PowerForge` | Plain cleanup request DTO. | Move unchanged. |
| `Models\ProjectCleanupSummary.cs` | `PowerForge` | Plain cleanup summary DTO. | Move unchanged. |
| `Models\ProjectDeleteMethod.cs` | `PowerForge` | Plain enum used by cleanup workflow. | Move unchanged. |
| `Services\BinaryConflictDetectionService.cs` | `PowerForge` | Assembly comparison/reporting logic, no PowerShell runtime dependency. | Move unchanged. |
| `Services\BuildDiagnosticsFactory.cs` | `PowerForge` | Diagnostic shaping for pipeline outputs; reusable by CLI/Studio. | Move unchanged. |
| `Services\IModulePipelineProgressReporter.cs` | `PowerForge` | Core progress callback contract. | Move unchanged. |
| `Services\ArtefactBuilder*.cs` | `PowerForge` | Packaging/copy logic is reusable once path token handling is neutralized. | Move after replacing `BuildServices` token helpers with core helpers. |
| `Services\ModuleInformationReader.cs` | `PowerForge` | Manifest discovery/basic metadata reading is reusable once it stops depending on AST-only helpers. | Move after adding a neutral manifest text parser. |
| `Services\ModuleInstaller.cs` | `PowerForge` | Install workflow is host-neutral once manifest metadata access stops depending on AST helpers. | Move after switching to neutral manifest metadata reader. |
| `Services\ModuleVersionStepper.cs` | `PowerForge` | Version stepping logic is reusable once manifest reads stop depending on AST helpers. | Move after switching to neutral manifest metadata reader. |
| `Services\ProjectCleanupService.cs` | `PowerForge` | Filesystem cleanup workflow had only incidental PowerShell wildcard matching. | Move after replacing `WildcardPattern` with a neutral matcher. |

### PowerShell Only

These directly depend on SMA/runspaces/PowerShell cmdlets or are explicitly PowerShell-host convenience adapters.

| File(s) | Target | Why | Keep / change |
|---|---|---|---|
| `BuildServices.cs` | `PowerForge.PowerShell` | Explicitly designed for PowerShell build scripts and uses `PowerShellRunner`/PSSA helpers. | Keep in `.PowerShell`; consider renaming namespace/type later so ownership is obvious. |
| `Models\Analysis\MissingFunctionCommand.cs` | `PowerForge.PowerShell` | Carries `ScriptBlock`, so it is a PowerShell runtime model today. | Keep until analysis models are redesigned. |
| `Models\Analysis\MissingFunctionsReport.cs` | `PowerForge.PowerShell` | Report shape is tied to `MissingFunctionCommand`. | Keep until analysis models are redesigned. |
| `Services\AuthenticodeSigningService.cs` | `PowerForge.PowerShell` | Uses `PSObject`, runspaces, and PowerShell signing commands. | Keep in `.PowerShell`. |
| `Services\DocumentationEngine.cs` | `PowerForge.PowerShell` | Uses `Get-Help` extraction and PowerShell import/help semantics through `IPowerShellRunner`. | Keep in `.PowerShell` for now. |
| `Services\LegacySegmentAdapter.cs` | `PowerForge.PowerShell` | Consumes `ScriptBlock`, `PSObject`, and legacy DSL outputs from PowerShell. | Keep in `.PowerShell`. |
| `Services\MissingFunctionsAnalyzer.cs` | `PowerForge.PowerShell` | Uses PowerShell AST, `CommandInfo`, runspaces, and script blocks. | Keep in `.PowerShell`. |
| `Services\ModuleTestSuiteService.cs` | `PowerForge.PowerShell` | Runs Pester/import flow through child PowerShell. | Keep in `.PowerShell`. |

### Mixed, Needs Refactor

These mostly represent reusable workflow, but they currently depend on PowerShell-specific helpers or bundle both host-neutral and PowerShell-hosted behavior in one type.

| File(s) | Target after split | Why it is mixed today | First split |
|---|---|---|---|
| `Services\ManifestEditor*.cs` | Mostly `PowerForge.PowerShell`, with a future neutral manifest abstraction in `PowerForge` | Uses `System.Management.Automation.Language` AST editing. The behavior is reusable, but the implementation is PowerShell-language-specific. | Introduce a neutral manifest mutator contract in core. Keep AST-backed implementation in `.PowerShell`. |
| `Services\ExportDetector.cs` | Split: binary metadata scanning in `PowerForge`, script-function detection in `PowerForge.PowerShell` | Binary cmdlet/alias scanning is metadata-only, but script export detection uses PowerShell AST. | Split into `BinaryExportDetector` and `ScriptExportDetector`. |
| `Services\ModuleBuilder.cs` | `PowerForge` | Most of the workflow is core build/publish/copy logic, but export detection and manifest patching currently come from PowerShell-bound helpers. | Extract manifest/export operations behind interfaces; then move builder back to core. |
| `Services\ModuleBuildPipeline.cs` | `PowerForge` | Core staging/install orchestration, but depends on `ModuleBuilder` and `ManifestEditor`-backed version patching. | Move after `ModuleBuilder` and manifest abstractions are split. |
| `Services\BinaryDependencyPreflightService.cs` | `PowerForge` | Assembly analysis is core, but host assembly discovery currently relies on PowerShell runtime knowledge and `PSObject` assembly location. | Separate pure assembly graph scan from edition-specific host assembly catalogs. |
| `Services\ModuleTestFailureAnalyzer.cs` | Split: XML/core in `PowerForge`, Pester-object analysis in `PowerForge.PowerShell` | NUnit XML analysis is core, but Pester result-object inspection uses `PSObject`. | Split analyzers by input source. |
| `Services\ModuleTestFailureWorkflowService.cs` | `PowerForge` after analyzer split | Workflow/path resolution is core, but it currently depends on the mixed analyzer. | Move after `ModuleTestFailureAnalyzer` is split. |
| `Services\ModuleValidationService*.cs` | Split: validation coordinator in `PowerForge`, PowerShell validators in `PowerForge.PowerShell` | The coordinator is reusable, but script parsing/PSSA/import validation rely on PowerShell concepts. | Break into neutral validation pipeline plus PowerShell-backed validation adapters. |
| `Services\ModulePublisher.cs` | Split: publish coordinator in `PowerForge`, PowerShell repository publishers in `PowerForge.PowerShell` | GitHub release publishing is host-neutral, but PowerShell repository publishing uses `PSResourceGet` / `PowerShellGet` clients. | Introduce publisher interfaces and separate repository-specific implementations. |
| `Services\ModulePipelineRunner*.cs` | Split: planning/result assembly in `PowerForge`, PowerShell-backed execution in `PowerForge.PowerShell` | The plan/result contracts are core, but execution currently mixes staging, manifest refresh, module import, dependency install, documentation, validation, and PowerShell-runner concerns. | First extract a core planner and immutable execution context, then keep a PowerShell execution adapter on top. |

## Recommended Extraction Order

### Phase 1: Contracts and obvious core services

Move these back to `PowerForge` first:

- `Models\ArtefactBuildResult.cs`
- `Models\ModuleInformation.cs`
- `Models\ModulePipelineDiagnosticsPolicyException.cs`
- `Models\ModulePipelinePlan.cs`
- `Models\ModulePipelineResult.cs`
- `Models\ModulePipelineStep.cs`
- `Models\ModulePublishResult.cs`
- `Models\ModuleTestSuiteResult.cs`
- `Models\ProjectCleanupOutput.cs`
- `Models\ProjectCleanupSpec.cs`
- `Models\ProjectCleanupSummary.cs`
- `Models\ProjectDeleteMethod.cs`
- `Services\BinaryConflictDetectionService.cs`
- `Services\BuildDiagnosticsFactory.cs`
- `Services\IModulePipelineProgressReporter.cs`

This phase is the fastest way to reduce `PowerForge.Cli`'s dependency pressure on `PowerForge.PowerShell` without changing user-visible behavior.

Completed in follow-up slices:

- `Services\ArtefactBuilder*.cs`
- `Services\ModuleInformationReader.cs`
- `Services\ModuleInstaller.cs`
- `Services\ModuleVersionStepper.cs`
- `Services\ProjectCleanupService.cs`

### Phase 2: Split the mixed helpers that block the main pipeline move

Do these before touching `ModulePipelineRunner` ownership:

- Split `ManifestEditor` into core contracts plus `.PowerShell` AST implementation.
- Split `ExportDetector` into binary/core and script/PowerShell parts.
- Split `ModuleTestFailureAnalyzer` into XML/core and Pester-object/PowerShell parts.
- Separate reusable manifest mutation from AST-backed manifest editing.

Current status:

- `IModuleManifestMutator` now exists in `PowerForge`.
- `AstModuleManifestMutator` now lives in `PowerForge.PowerShell`.
- `ModuleBuilder` now uses the manifest mutator seam and the core `ModuleInstaller`.
- The remaining blocker for moving `ModuleBuilder` into core is export detection, especially `DetectScriptFunctions`.

### Phase 3: Move the reusable pipeline engine back to core

After the helper splits:

- Move `ModuleInformationReader`
- Move `ModuleBuilder`
- Move `ModuleBuildPipeline`
- Move the plan/result portions of `ModulePipelineRunner`
- Keep PowerShell execution adapters, dependency install, help extraction, Pester execution, and signing in `PowerForge.PowerShell`

### Phase 4: Publish and validation decomposition

These are worth doing separately because they mix reusable orchestration with PowerShell-specific implementations:

- `ModulePublisher`
- `ModuleValidationService*`
- `BinaryDependencyPreflightService`

## First Concrete Slice

The safest first PR after this document is:

1. Move the plain DTOs and `IModulePipelineProgressReporter` back into `PowerForge`.
2. Move `BuildDiagnosticsFactory` and `BinaryConflictDetectionService` back into `PowerForge`.
3. Extract neutral helpers so `ArtefactBuilder*`, `ModuleInformationReader`, `ModuleInstaller`, `ModuleVersionStepper`, and `ProjectCleanupService` can also live in core.
4. Update `PowerForge.PowerShell.csproj` to stop linking files that now belong to core.
5. Keep namespaces stable in these early slices to avoid unnecessary downstream churn.

That gives us an immediate architecture improvement without forcing the big refactor in the same change. The next boundary pressure is now concentrated around export detection, validation, and PowerShell-hosted execution in `ModulePipelineRunner`.

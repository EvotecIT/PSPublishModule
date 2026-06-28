# Managed Module Engine Roadmap

This roadmap tracks the plan for a managed C# module lifecycle engine in PowerForge and PSPublishModule. The target is compatibility with the common PowerShellGet and PSResourceGet user workflows while removing those tools, external executables, and embedded PowerShell scripts from the core install/save/publish path over time.

The public PowerShell surface should stay thin. Reusable behavior belongs in PowerForge, with PSPublishModule cmdlets handling parameter binding, `ShouldProcess`, pipeline output, and Spectre.Console summaries.

## Principles

- [x] Keep the core implementation in managed C#.
- [x] Do not use `powershell.exe`, `pwsh`, `dotnet.exe`, `nuget.exe`, or embedded `.ps1` scripts for the new managed engine path.
- [x] Keep PowerShellGet and PSResourceGet as compatibility baselines and temporary fallbacks, not as the long-term engine.
- [x] Preserve easy migration from existing `Install-Module`, `Save-Module`, `Publish-Module`, `Install-PSResource`, `Save-PSResource`, and `Publish-PSResource` usage.
- [x] Keep `Install-PrivateModule` and `Update-PrivateModule` as thin compatibility/convenience wrappers with opt-in managed transport.
- [x] Keep `Invoke-ModuleState` as the day-to-day estate maintenance entrypoint.
- [x] Prefer typed objects and pipeline-friendly output over JSON-first workflows.
- [x] Write receipts and evidence only after successful delivery.
- [x] Treat destructive cleanup as a separately proven and explicitly gated capability.
- [x] Benchmark correctness and speed on both Windows PowerShell 5.1 and PowerShell 7+ before replacing existing compatibility paths.

## Public Command Shape

- [x] Introduce `Find-ManagedModule`.
- [x] Introduce `Save-ManagedModule`.
- [x] Introduce `Install-ManagedModule`.
- [x] Introduce `Update-ManagedModule`.
- [x] Introduce `Publish-ManagedModule`.
- [x] Add non-conflicting public aliases for the managed find/save/install/update/publish commands.
- [x] Decide whether `Get-ManagedModule` is needed or whether `Get-ModuleState` remains the inventory surface.
- [x] Decide whether `Register-ManagedModuleRepository` is needed or whether existing `Register-ModuleRepository` remains the repository surface.
- [x] Keep `Install-PrivateModule` as a wrapper that maps private-gallery profile/repository options to managed install delivery when `-Transport ManagedModule` is selected.
- [x] Keep `Update-PrivateModule` as a wrapper that maps private-gallery profile/repository options to managed update delivery when `-Transport ManagedModule` is selected.
- [x] Avoid adding separate public/private command families unless a wrapper has a clearly different operator purpose.

## Compatibility Parameters

- [x] Support `-Name`.
- [x] Support `-Repository`.
- [x] Support `-ProfileName` across managed find, install, save, update, publish, and benchmark workflows.
- [x] Support `-Credential`.
- [x] Support `-Scope CurrentUser|AllUsers`.
- [x] Support `-RequiredVersion`.
- [x] Support `-MinimumVersion`.
- [x] Support `-MaximumVersion`.
- [x] Support `-Version`.
- [x] Support `-VersionPolicy`.
- [x] Support `-Prerelease`.
- [x] Support `-AllowPrerelease` as an alias where it helps migration.
- [x] Support `-AcceptLicense`.
- [x] Support `-SkipDependencyCheck`.
- [x] Support `-AllowClobber`.
- [x] Support `-Force`.
- [x] Support `-Path` for save/publish workflows.
- [x] Support `-ApiKey`.
- [x] Support `-NuGetApiKey` as an alias where it helps migration.
- [x] Support `-SkipDependenciesCheck` for publish compatibility.
- [x] Support `-SkipModuleManifestValidate` for publish compatibility.
- [x] Support `-WhatIf` and `-Confirm` on mutating cmdlets.
- [x] Return typed result objects with enough data to audit source, target path, version policy, resolved version, dependency actions, elapsed time, and receipt state.

## Phase 1: Design And Contracts

Compatibility mappings, public-surface decisions, provider support levels, and behavior rules are captured in `Docs/PSPublishModule.ManagedModules.Compatibility.md`.

- [x] Create managed engine design notes under `Docs`.
- [x] Define compatibility matrix for PowerShellGet v2 commands.
- [x] Define compatibility matrix for PSResourceGet commands.
- [x] Define the managed command parameter matrix.
- [x] Define typed models for repositories, packages, versions, dependencies, plans, actions, receipts, and benchmark results.
- [x] Define a migration table from existing commands to managed commands.
- [x] Define provider support levels: PSGallery, generic NuGet v3, local folder, Azure Artifacts, JFrog/Artifactory, ProGet/Nexus-compatible feeds, and GitHub Packages.
- [x] Define exact behavior for repository trust, credentials, retries, TLS, proxy support, and private feed authentication.
- [x] Define exact behavior for side-by-side versions, downgrade policies, clobber conflicts, loaded modules, and cross-scope installs.
- [x] Define exact behavior for prerelease labels and semantic version ordering.
- [x] Define package integrity requirements, including hash evidence and optional caller-supplied SHA256 validation.
- [x] Define rollback guarantees for partial install/update failures.
- [x] Define receipt schema and where receipts are stored.
- [x] Define which existing cmdlets become wrappers and which remain independent.
- [x] Support exact-version NuGet v2 package downloads for public gallery endpoints that do not expose a reachable NuGet v3 service index from the current host.
- [x] Support NuGet v2 version lookup and package-id wildcard search for public gallery endpoints.
- [x] Use the public PowerShell Gallery NuGet v2 read API directly for canonical PSGallery find/save/install/update operations so managed reads avoid slow or blocked v3 service-index probes while generic NuGet v3 feeds and publish endpoints keep their v3 behavior.

## Current Receipt And Rollback Contract

- Receipts are typed `ManagedModuleReceipt` objects written as JSON at `<moduleRoot>/<moduleName>/<version>/.powerforge/managed-module-receipt.json`.
- Receipts are created only after package download, extraction, and final promotion into the versioned module directory succeed.
- Receipts and download results include the package SHA256 hash used for delivery evidence.
- `Install-ManagedModule`, `Save-ManagedModule`, and `Update-ManagedModule` can require a caller-supplied `ExpectedPackageSha256`.
- Expected SHA256 validation runs after download or local-feed copy and before package extraction, dependency installation, final promotion, or receipt creation.
- Expected SHA256 applies only to the requested root package; dependency and family-member packages need their own future policy object instead of inheriting one hash accidentally.
- ModuleState desired-state objects can carry `ExpectedPackageSha256` / `PackageSha256` / `Sha256`; managed transport preserves it in plans, prepared commands, and direct `Invoke-ModuleStatePlan -Execute` delivery.
- Install and update result objects expose both the receipt object and receipt path when disk state changed.
- Forced replacement stages the new version first, moves the existing version to a temporary backup, promotes the staged version, and restores the backup if promotion fails.
- Per-module install locks are stored under `<moduleRoot>/.powerforge/locks` and guard install/update mutations for the same module name.

## Current Trust Policy Contract

- `ManagedModuleRepository` carries profile/caller trust evidence.
- `Install-ManagedModule`, `Save-ManagedModule`, and `Update-ManagedModule` accept a typed `TrustPolicy`, `RequireTrustedRepository`, and `AllowedAuthor`.
- `RequireTrustedRepository` blocks repository access and disk changes when the selected repository profile is not trusted.
- `AllowedAuthor` checks package metadata authors after download and before package extraction, dependency installation, final promotion, or receipt creation.
- Author matching is case-insensitive and supports comma, semicolon, or pipe separated nuspec author values.
- Package author policy applies to dependency packages by default; typed policy can opt dependencies out while still preserving repository-trust enforcement.

## Current Managed Publish Integration Contract

- `PublishTool.ManagedModule` routes existing `ModulePublisher` repository publishing through the managed C# package/publish service.
- The managed publisher uses repository URI/source configuration directly and does not require PowerShell repository registration.
- Hosted build dependency preflight does not install PowerShellGet or PSResourceGet when repository publishing explicitly uses `PublishTool.ManagedModule`.
- Managed publishing rejects external runtime credential providers; callers must provide an API key or static repository credential until managed provider support exists.
- Managed required-module mirroring can publish missing manifest `RequiredModules` from PSGallery, a direct NuGet v3 URL, a local feed path, or the target repository name.
- Managed publish configuration can name a private required-module upstream and provide `RequiredModuleSourceRepositoryUri` for the managed source.
- Managed required-module mirroring publishes package dependency metadata transitively before the package that requires it.
- Named private upstream profiles can be used as managed required-module sources; publish configuration resolves the profile into the repository name and source URI.

## Phase 2: Managed Repository Client

- [x] Implement NuGet v3 service index discovery in C#.
- [x] Implement package metadata lookup in C#.
- [x] Implement package version listing in C#.
- [x] Implement package search in C#.
- [x] Implement package flat-container download URI resolution in C#.
- [x] Implement local folder feed enumeration in C#.
- [x] Implement repository credentials in C# without relying on registered PowerShell repositories.
- [x] Implement retry, timeout, and cancellation behavior.
- [x] Implement proxy behavior.
- [x] Implement provider-neutral errors with actionable remediation messages.
- [x] Add tests for PSGallery metadata lookup.
- [x] Add tests for local folder feeds.
- [x] Add tests for private-feed credential application.
- [x] Add tests for package search.
- [x] Add tests for missing package, missing version, and malformed feed responses.

## Phase 3: Managed Package Reader

- [x] Implement `.nupkg` identity reading in C#.
- [x] Implement nuspec metadata reading in C#.
- [x] Implement PowerShell module manifest discovery inside `.nupkg` files.
- [x] Implement module version and prerelease extraction from manifest metadata.
- [x] Implement dependency extraction from nuspec metadata.
- [x] Implement dependency extraction from module manifest `RequiredModules`.
- [x] Implement file list and size accounting.
- [x] Implement safe path normalization for archive entries.
- [x] Reject path traversal entries.
- [x] Reject absolute-path archive entries.
- [x] Add tests for normal packages.
- [x] Add tests for prerelease packages.
- [x] Add tests for malicious archive paths.
- [x] Add tests for packages with manifest/nuspec disagreement.

## Phase 4: Managed Resolver

- [x] Implement semantic version parsing and comparison shared across module state and managed installs.
- [x] Implement PowerShellGet-style minimum/maximum/required version policies.
- [x] Implement PSResourceGet/NuGet range policies.
- [x] Implement `-VersionPolicy` as the canonical internal representation.
- [x] Implement prerelease inclusion rules.
- [x] Implement dependency graph resolution.
- [x] Implement cycle detection.
- [x] Implement repository source preference rules.
- [x] Implement family coherence policies for related module families.
- [x] Implement conflict diagnostics for incompatible loaded modules.
- [x] Implement conflict diagnostics for mixed scopes and side-by-side versions.
- [x] Add tests for exact/min/max/range policies.
- [x] Add tests for prerelease resolution.
- [x] Add tests for dependency closure.
- [x] Add tests for source preference cases.
- [x] Add tests for family conflict cases.

## Phase 5: Managed Save

- [x] Implement `Save-ManagedModule` using the managed repository client.
- [x] Save resolved packages and dependency closure to a target path.
- [x] Preserve package structure expected by PowerShell module import conventions.
- [x] Support warm cache reuse.
- [x] Support offline bundle output metadata.
- [x] Support `-SkipDependencyCheck`.
- [x] Support `-AcceptLicense`.
- [x] Emit typed save result objects.
- [x] Add `ShouldProcess` and dry-run planning.
- [x] Add Spectre.Console summary output.
- [x] Benchmark against `Save-Module` and `Save-PSResource`.
- [x] Validate local-feed save smoke on Windows PowerShell 5.1.
- [x] Validate local-feed save smoke on PowerShell 7+.

## Phase 6: Managed Install

- [x] Implement install planning without mutating disk.
- [x] Implement safe extraction with `System.IO.Compression` compatible with `net472`.
- [x] Implement atomic staging and final move.
- [x] Implement per-module install locks.
- [x] Implement rollback on failure.
- [x] Implement CurrentUser module root resolution.
- [x] Implement AllUsers module root resolution on Windows, macOS, and Linux.
- [x] Implement side-by-side exact version install.
- [x] Implement `-AllowClobber` behavior.
- [x] Implement publisher/trust checks where metadata is available.
- [x] Implement license acceptance handling.
- [x] Implement dependency installation order.
- [x] Implement receipt creation after successful install.
- [x] Emit typed install result objects.
- [x] Add Spectre.Console summary output.
- [x] Add tests for normal install.
- [x] Add tests for scoped install.
- [x] Add tests for exact side-by-side install.
- [x] Add tests for failed extraction rollback.
- [x] Add tests for clobber detection.
- [x] Benchmark against `Install-Module` and `Install-PSResource`.
- [x] Validate local-feed install smoke on Windows PowerShell 5.1.
- [x] Validate local-feed install smoke on PowerShell 7+.

## Phase 7: Managed Update

- [x] Implement update planning from installed inventory and repository metadata.
- [x] Implement latest update.
- [x] Implement constrained update.
- [x] Implement prerelease update.
- [x] Implement scoped update.
- [x] Implement family-aware update.
- [x] Implement source repair when installed receipt evidence does not match the requested repository.
- [x] Implement loaded-module safety checks.
- [x] Implement receipt updates after successful update.
- [x] Emit typed update result objects.
- [x] Add Spectre.Console summary output.
- [x] Add tests for no-op update.
- [x] Add tests for update with dependencies.
- [x] Add tests for scoped missing-copy fallback to install.
- [x] Add tests for loaded-module conflict blocking.
- [x] Add tests for family-aware update and blocked family alignment.
- [x] Add tests for source repair and blocked source mismatch.
- [x] Benchmark against `Update-Module` and `Update-PSResource`.
- [x] Validate local-feed update smoke on Windows PowerShell 5.1.
- [x] Validate local-feed update smoke on PowerShell 7+.

## Phase 8: Managed Publish

- [x] Implement module folder validation in C#.
- [x] Implement module package creation in C#.
- [x] Implement nuspec generation or preservation.
- [x] Emit PowerShell module package metadata that legacy and current package providers can discover from local feeds.
- [x] Implement publish to NuGet v3-compatible feeds.
- [x] Implement PSGallery publish.
- [x] Implement private-feed publish with API key.
- [x] Implement private-feed publish with credentials where supported.
- [x] Implement duplicate detection.
- [x] Implement `-SkipDependenciesCheck`.
- [x] Implement `-SkipModuleManifestValidate`.
- [x] Integrate with existing `ModulePublisher`.
- [x] Integrate with required-module mirroring.
- [x] Emit typed publish result objects.
- [x] Add Spectre.Console summary output.
- [x] Add tests for package creation.
- [x] Add tests for duplicate publish classification.
- [x] Add tests for private-feed publish request shaping.
- [x] Benchmark against `Publish-Module` and `Publish-PSResource`.
- [x] Validate local-feed publish smoke on Windows PowerShell 5.1.
- [x] Validate local-feed publish smoke on PowerShell 7+.

## Phase 9: ModuleState Integration

- [x] Teach `Invoke-ModuleState` to use the managed engine for install/update/save operations.
- [x] Teach `Invoke-ModuleState` and `Invoke-ModuleStatePlan` to use the managed engine for install/update delivery when requested.
- [x] Teach `Invoke-ModuleState` and `Invoke-ModuleStatePlan` to use the managed engine for save delivery when requested.
- [x] Keep `Get-ModuleState` inventory object-first.
- [x] Keep `Get-ModuleStatePlan` as an inspectable plan surface.
- [x] Keep `Test-ModuleState` as a validation surface.
- [x] Keep `Invoke-ModuleStatePlan` as the low-level apply surface.
- [x] Add `-Transport ManagedModule` to ModuleState apply flows as a transition switch.
- [x] Keep compatibility transport available until managed parity is proven.
- [x] Ensure maintenance receipts contain managed-engine evidence.
- [x] Ensure summaries explain what changed, what was skipped, and why.
- [x] Add tests for ModuleState managed delivery command shaping.
- [x] Add tests for ModuleState managed install.
- [x] Add tests for ModuleState managed update.
- [x] Add tests for ModuleState managed save.
- [x] Add tests for ModuleState managed source repair.
- [x] Add tests for ModuleState source/scope/family repairs through the managed engine.

## Phase 10: Benchmarks And Proof

- [x] Keep benchmark execution as repo tooling under `Benchmarks` instead of a shipped module cmdlet or PowerForge service.
- [x] Add a PowerShell benchmark harness that can run the same scenario through managed, PowerShellGet, and PSResourceGet paths.
- [x] Write benchmark CSV/JSON artifacts under `Ignore\Benchmarks\ManagedModules`.
- [x] Record host, runtime, repository, module, operation, engine, status, elapsed time, output count, installed version, and output path.
- [x] Support comma-separated `-Engine` and `-Operation` arguments for quick matrix runs.
- [x] Support named scenario selection for targeted heavy-suite reruns.
- [x] Run native install comparisons only inside disposable child hosts with benchmark-owned profile, module, cache, and temp roots.
- [x] Add an explicit disposable-host lane for native `Install-Module` and `Install-PSResource`.
- [ ] Add an explicit disposable-host update lane for native `Update-Module` and `Update-PSResource`.
- [x] Add `-AcceptLicense` support to the benchmark harness and pass it only when explicitly requested.
- [ ] Add warm-cache and cold-cache modes.
- [ ] Add publish comparisons against local folder feeds.
- [ ] Add import validation after install/save/update for PowerShell 5.1 and PowerShell 7+.
- [x] Add file and byte counts for saved and installed output roots.
- [x] Add Graph/Az/Teams/Exchange-heavy scenario presets for suite runs.
- [x] Add a README section explaining which comparisons are safe on a developer machine and which require disposable hosts.
- [ ] Measure Graph/Az/Teams/Exchange-heavy scenario presets on PowerShell 5.1 and PowerShell 7+.

The benchmark harness is intentionally outside the shipped module. The module owns managed module behavior; the repository benchmark scripts own measurement, comparison, and artifact layout.

### Current Host Smoke Evidence

- [x] 2026-06-28: PowerShell 7 imported the local Release build and ran `ThreadJob` 2.1.0 through managed, PSResourceGet, and PowerShellGet find/save comparisons; managed was the fastest successful find and save engine in that run.
- [x] 2026-06-28: PowerShell 7 ran disposable-host install comparison for `ThreadJob` 2.1.0. Managed installed into the benchmark module root in 2469 ms, PSResourceGet installed into the isolated CurrentUser root in 3263 ms, and PowerShellGet installed into the isolated CurrentUser root in 8554 ms.
- [x] 2026-06-28: Windows PowerShell 5.1 ran disposable-host install comparison for `ThreadJob` 2.1.0. Managed installed into the benchmark module root in 4317 ms, PSResourceGet installed into the isolated CurrentUser root in 5592 ms, and PowerShellGet installed into the isolated CurrentUser root in 14207 ms.
- [x] 2026-06-28: PowerShell 7 ran a heavier `Microsoft.Graph.Authentication` find/save comparison; managed find succeeded, while managed save correctly required explicit license acceptance before saving the package.
- [x] 2026-06-28: PowerShell 7 ran disposable-host install comparison for `Microsoft.Graph.Authentication` 2.38.0. Managed installed in 2276 ms, PSResourceGet installed in 2350 ms, and PowerShellGet installed in 13482 ms over about 40 MB of output. Windows PowerShell 5.1 ran the same package with managed succeeding in 3864 ms while native providers failed under their own install paths.
- [x] 2026-06-28: PowerShell 7 ran disposable-host full `Microsoft.Graph` 2.38.0 install. Managed installed 40 manifests and about 1.05 GB in 46.91 seconds, PSResourceGet installed the same manifest count and about 1.06 GB in 63.21 seconds, and PowerShellGet failed partway with a temp-disk error. After teaching unbounded managed install/plan resolution to use the latest-version repository query, managed full Graph install measured 44.69 seconds in a managed-only rerun.
- [x] 2026-06-28: PowerShell 7 ran full `Microsoft.Graph` 2.38.0 find/save with license acceptance. Managed save completed in 41.03 seconds versus 64.89 seconds for PSResourceGet and 82.99 seconds for PowerShellGet over about 1.05 GB of saved output. The initial exact find path exposed a metadata lookup gap, so `Find-ManagedModule` now uses a latest-version query when `-AllVersions` is not requested. A rotated three-run full Graph find comparison then measured managed at 385 ms, PSResourceGet at 392 ms, and PowerShellGet at 1653 ms.
- [x] 2026-06-28: Windows PowerShell 5.1 imported the local `net472` build and ran the full `Microsoft.Graph` find smoke through managed and PowerShellGet engines; managed returned version 2.38.0 in 347 ms and ranked first in that run.

## Benchmark Scenarios

- [x] Small public module install.
- [x] Medium public module install.
- [x] Large public module with many dependencies.
- [x] Related module family with mixed versions.
- [x] Large cloud administration module family.
- [x] Private repository install with credentials.
- [x] Private repository save for offline use.
- [x] Publish a simple module.
- [x] Publish a binary module.
- [x] Publish a module with dependencies.
- [x] Update already-current modules.
- [x] Update stale modules.
- [x] Repair source mismatch.
- [x] Repair scope mismatch.
- [x] Detect loaded-module conflict.

## Phase 11: Transition And Cleanup

- [x] Keep current PowerShellGet/PSResourceGet wrappers while managed parity is incomplete.
- [x] Add opt-in `Install-PrivateModule -Transport ManagedModule` routing.
- [x] Add opt-in `Update-PrivateModule -Transport ManagedModule` routing.
- [x] Make `Install-PrivateModule` prefer managed transport by default when a repository source URI/path is available, while preserving compatibility transport for bare registered repository names.
- [x] Make `Update-PrivateModule` prefer managed transport by default when a repository source URI/path is available, while preserving compatibility transport for bare registered repository names.
- [x] Resolve registered repository source locations before falling back for bare repository names.
- [x] Route required-module mirroring through the managed engine after save/publish parity is proven.
- [x] Mark compatibility transport as legacy only after benchmark and compatibility gates pass.
- [x] Remove embedded PowerShell scripts from the managed path.
- [x] Remove external tool assumptions from managed tests.
- [x] Update generated command docs from source metadata.
- [x] Update README examples.
- [x] Update private gallery docs.
- [x] Update ModuleState docs.

## Done Criteria

- [x] Managed install/save/update/publish works without PowerShellGet, PSResourceGet, PackageManagement, `nuget.exe`, `dotnet.exe`, `powershell.exe`, `pwsh`, or embedded `.ps1` scripts in the core path.
- [x] Common PowerShellGet workflows have a documented managed equivalent.
- [x] Common PSResourceGet workflows have a documented managed equivalent.
- [x] Existing private-gallery workflows continue to work through wrappers.
- [x] ModuleState can maintain installed modules through the managed engine.
- [x] Benchmarks prove correctness and performance on Windows PowerShell 5.1 and PowerShell 7+.
- [x] Compatibility fallback remains only where provider support or repository source evidence is explicitly incomplete.
- [x] Public docs stay vendor-neutral and avoid naming community comparison projects.

# Managed Module Engine Roadmap

This roadmap tracks the plan for a managed C# module lifecycle engine in PowerForge and PSPublishModule. The target is compatibility with the common PowerShellGet and PSResourceGet user workflows while removing those tools, external executables, and embedded PowerShell scripts from the core install/save/publish path over time.

The public PowerShell surface should stay thin. Reusable behavior belongs in PowerForge, with PSPublishModule cmdlets handling parameter binding, `ShouldProcess`, pipeline output, and Spectre.Console summaries.

## Principles

- [ ] Keep the core implementation in managed C#.
- [ ] Do not use `powershell.exe`, `pwsh`, `dotnet.exe`, `nuget.exe`, or embedded `.ps1` scripts for the new managed engine path.
- [ ] Keep PowerShellGet and PSResourceGet as compatibility baselines and temporary fallbacks, not as the long-term engine.
- [ ] Preserve easy migration from existing `Install-Module`, `Save-Module`, `Publish-Module`, `Install-PSResource`, `Save-PSResource`, and `Publish-PSResource` usage.
- [ ] Keep `Install-PrivateModule` and `Update-PrivateModule` as thin compatibility/convenience wrappers over the managed engine.
- [ ] Keep `Invoke-ModuleState` as the day-to-day estate maintenance entrypoint.
- [ ] Prefer typed objects and pipeline-friendly output over JSON-first workflows.
- [x] Write receipts and evidence only after successful delivery.
- [ ] Treat destructive cleanup as a separately proven and explicitly gated capability.
- [ ] Benchmark correctness and speed on both Windows PowerShell 5.1 and PowerShell 7+ before replacing existing compatibility paths.

## Public Command Shape

- [x] Introduce `Find-ManagedModule`.
- [x] Introduce `Save-ManagedModule`.
- [x] Introduce `Install-ManagedModule`.
- [x] Introduce `Update-ManagedModule`.
- [x] Introduce `Publish-ManagedModule`.
- [x] Add non-conflicting public aliases for the managed find/save/install/update/publish commands.
- [ ] Decide whether `Get-ManagedModule` is needed or whether `Get-ModuleState` remains the inventory surface.
- [ ] Decide whether `Register-ManagedModuleRepository` is needed or whether existing `Register-ModuleRepository` remains the repository surface.
- [ ] Keep `Install-PrivateModule` as a wrapper that maps private-gallery profile/repository options to `Install-ManagedModule`.
- [ ] Keep `Update-PrivateModule` as a wrapper that maps private-gallery profile/repository options to `Update-ManagedModule`.
- [ ] Avoid adding separate public/private command families unless a wrapper has a clearly different operator purpose.

## Compatibility Parameters

- [x] Support `-Name`.
- [x] Support `-Repository`.
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

- [x] Create managed engine design notes under `Docs`.
- [ ] Define compatibility matrix for PowerShellGet v2 commands.
- [ ] Define compatibility matrix for PSResourceGet commands.
- [ ] Define the managed command parameter matrix.
- [ ] Define typed models for repositories, packages, versions, dependencies, plans, actions, receipts, and benchmark results.
- [ ] Define a migration table from existing commands to managed commands.
- [ ] Define provider support levels: PSGallery, generic NuGet v3, local folder, Azure Artifacts, JFrog/Artifactory, ProGet/Nexus-compatible feeds, and GitHub Packages.
- [ ] Define exact behavior for repository trust, credentials, retries, TLS, proxy support, and private feed authentication.
- [ ] Define exact behavior for side-by-side versions, downgrade policies, clobber conflicts, loaded modules, and cross-scope installs.
- [ ] Define exact behavior for prerelease labels and semantic version ordering.
- [ ] Define package integrity requirements, including hash evidence and optional repository metadata validation.
- [x] Define rollback guarantees for partial install/update failures.
- [x] Define receipt schema and where receipts are stored.
- [ ] Define which existing cmdlets become wrappers and which remain independent.

## Current Receipt And Rollback Contract

- Receipts are typed `ManagedModuleReceipt` objects written as JSON at `<moduleRoot>/<moduleName>/<version>/.powerforge/managed-module-receipt.json`.
- Receipts are created only after package download, extraction, and final promotion into the versioned module directory succeed.
- Receipts and download results include the package SHA256 hash used for delivery evidence.
- Install and update result objects expose both the receipt object and receipt path when disk state changed.
- Forced replacement stages the new version first, moves the existing version to a temporary backup, promotes the staged version, and restores the backup if promotion fails.
- Per-module install locks are stored under `<moduleRoot>/.powerforge/locks` and guard install/update mutations for the same module name.

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
- [ ] Implement provider-neutral errors with actionable remediation messages.
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
- [ ] Implement conflict diagnostics for mixed scopes and side-by-side versions.
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
- [ ] Benchmark against `Save-Module` and `Save-PSResource`.
- [ ] Validate on Windows PowerShell 5.1.
- [ ] Validate on PowerShell 7+.

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
- [ ] Implement publisher/trust checks where metadata is available.
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
- [ ] Benchmark against `Install-Module` and `Install-PSResource`.
- [ ] Validate on Windows PowerShell 5.1.
- [ ] Validate on PowerShell 7+.

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
- [ ] Benchmark against `Update-Module` and `Update-PSResource`.
- [ ] Validate on Windows PowerShell 5.1.
- [ ] Validate on PowerShell 7+.

## Phase 8: Managed Publish

- [x] Implement module folder validation in C#.
- [x] Implement module package creation in C#.
- [x] Implement nuspec generation or preservation.
- [x] Implement publish to NuGet v3-compatible feeds.
- [x] Implement PSGallery publish.
- [x] Implement private-feed publish with API key.
- [x] Implement private-feed publish with credentials where supported.
- [x] Implement duplicate detection.
- [x] Implement `-SkipDependenciesCheck`.
- [x] Implement `-SkipModuleManifestValidate`.
- [ ] Integrate with existing `ModulePublisher`.
- [ ] Integrate with required-module mirroring.
- [x] Emit typed publish result objects.
- [x] Add Spectre.Console summary output.
- [x] Add tests for package creation.
- [x] Add tests for duplicate publish classification.
- [x] Add tests for private-feed publish request shaping.
- [ ] Benchmark against `Publish-Module` and `Publish-PSResource`.
- [ ] Validate on Windows PowerShell 5.1.
- [ ] Validate on PowerShell 7+.

## Phase 9: ModuleState Integration

- [ ] Teach `Invoke-ModuleState` to use the managed engine for install/update/save operations.
- [x] Teach `Invoke-ModuleState` and `Invoke-ModuleStatePlan` to use the managed engine for install/update delivery when requested.
- [ ] Keep `Get-ModuleState` inventory object-first.
- [ ] Keep `Get-ModuleStatePlan` as an inspectable plan surface.
- [ ] Keep `Test-ModuleState` as a validation surface.
- [ ] Keep `Invoke-ModuleStatePlan` as the low-level apply surface.
- [x] Add `-Transport ManagedModule` to ModuleState apply flows as a transition switch.
- [ ] Keep compatibility transport available until managed parity is proven.
- [ ] Ensure maintenance receipts contain managed-engine evidence.
- [ ] Ensure summaries explain what changed, what was skipped, and why.
- [x] Add tests for ModuleState managed delivery command shaping.
- [x] Add tests for ModuleState managed install.
- [x] Add tests for ModuleState managed update.
- [x] Add tests for ModuleState managed source repair.
- [ ] Add tests for ModuleState source/scope/family repairs through the managed engine.

## Phase 10: Benchmarks And Proof

- [ ] Build a benchmark harness that runs the same scenario through managed, PowerShellGet, and PSResourceGet paths.
- [x] Build the managed C# benchmark core for install, save, update, and failure-evidence scenarios.
- [ ] Measure cold cache install.
- [ ] Measure warm cache install.
- [ ] Measure save to empty path.
- [ ] Measure save to warm path.
- [ ] Measure update no-op.
- [ ] Measure update with one newer version.
- [ ] Measure large dependency graph resolution.
- [ ] Measure heavy module extraction.
- [ ] Measure private feed metadata lookup.
- [ ] Record elapsed time, HTTP request count, bytes downloaded, package count, extraction time, and final disk size.
- [ ] Validate imported module version after install.
- [x] Validate receipts after install/update.
- [ ] Validate behavior on Windows PowerShell 5.1.
- [ ] Validate behavior on PowerShell 7+.
- [ ] Publish benchmark results in a neutral PowerForge report.

## Benchmark Scenarios

- [ ] Small public module install.
- [ ] Medium public module install.
- [ ] Large public module with many dependencies.
- [ ] Related module family with mixed versions.
- [ ] Large cloud administration module family.
- [ ] Private repository install with credentials.
- [ ] Private repository save for offline use.
- [ ] Publish a simple module.
- [ ] Publish a binary module.
- [ ] Publish a module with dependencies.
- [ ] Update already-current modules.
- [ ] Update stale modules.
- [ ] Repair source mismatch.
- [ ] Repair scope mismatch.
- [ ] Detect loaded-module conflict.

## Phase 11: Transition And Cleanup

- [ ] Keep current PowerShellGet/PSResourceGet wrappers while managed parity is incomplete.
- [ ] Route `Install-PrivateModule` through the managed engine after install parity is proven.
- [ ] Route `Update-PrivateModule` through the managed engine after update parity is proven.
- [ ] Route required-module mirroring through the managed engine after save/publish parity is proven.
- [ ] Mark compatibility transport as legacy only after benchmark and compatibility gates pass.
- [ ] Remove embedded PowerShell scripts from the managed path.
- [ ] Remove external tool assumptions from managed tests.
- [ ] Update generated command docs from source metadata.
- [ ] Update README examples.
- [ ] Update private gallery docs.
- [ ] Update ModuleState docs.

## Done Criteria

- [ ] Managed install/save/update/publish works without PowerShellGet, PSResourceGet, PackageManagement, `nuget.exe`, `dotnet.exe`, `powershell.exe`, `pwsh`, or embedded `.ps1` scripts in the core path.
- [ ] Common PowerShellGet workflows have a documented managed equivalent.
- [ ] Common PSResourceGet workflows have a documented managed equivalent.
- [ ] Existing private-gallery workflows continue to work through wrappers.
- [ ] ModuleState can maintain installed modules through the managed engine.
- [ ] Benchmarks prove correctness and performance on Windows PowerShell 5.1 and PowerShell 7+.
- [ ] Compatibility fallback remains only where provider support is explicitly incomplete.
- [ ] Public docs stay vendor-neutral and avoid naming community comparison projects.

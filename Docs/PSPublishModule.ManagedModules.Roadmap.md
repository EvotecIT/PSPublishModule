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
- [ ] Benchmark correctness and speed on both Windows PowerShell 5.1 and PowerShell 7+ before replacing existing compatibility paths.

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
- [ ] Benchmark against `Install-Module` and `Install-PSResource`.
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
- [ ] Benchmark against `Update-Module` and `Update-PSResource`.
- [x] Validate local-feed update smoke on Windows PowerShell 5.1.
- [x] Validate local-feed update smoke on PowerShell 7+.

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
- [x] Integrate with existing `ModulePublisher`.
- [x] Integrate with required-module mirroring.
- [x] Emit typed publish result objects.
- [x] Add Spectre.Console summary output.
- [x] Add tests for package creation.
- [x] Add tests for duplicate publish classification.
- [x] Add tests for private-feed publish request shaping.
- [ ] Benchmark against `Publish-Module` and `Publish-PSResource`.
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

- [x] Build a benchmark harness that runs the same scenario through managed, PowerShellGet, and PSResourceGet paths.
- [x] Build the managed C# benchmark core for install, save, update, and failure-evidence scenarios.
- [x] Expose a thin `Measure-ManagedModule` surface over the managed benchmark core.
- [x] Write managed benchmark JSON and Markdown evidence reports.
- [x] Add `Measure-ManagedModule -Engine Managed,PSResourceGet,PowerShellGet` selection.
- [x] Record compatibility baseline success/failure/status/version evidence through the existing compatibility install, save, update, and publish paths.
- [x] Add managed publish benchmark operation and publish evidence fields.
- [x] Publish benchmark results in a neutral PowerForge report.
- [x] Measure cold cache install.
- [x] Measure warm cache install.
- [x] Measure save to empty path.
- [x] Measure save to warm path.
- [x] Measure update no-op.
- [x] Measure update with one newer version.
- [x] Measure publish to local folder feed.
- [x] Measure large dependency graph resolution.
- [x] Measure heavy module extraction.
- [x] Measure private feed metadata lookup.
- [x] Record elapsed time, HTTP request count, package count, direct/total package bytes, direct/total extracted bytes, extraction timing, file count, and final disk size.
- [x] Validate installed module directory version after install/update.
- [x] Add optional benchmark import validation evidence for PowerShell host checks.
- [x] Validate imported module version after install in PS 5.1 and PS 7+ benchmark runners.
- [x] Validate receipts after install/update.
- [x] Validate benchmark install/import behavior on Windows PowerShell 5.1.
- [x] Validate benchmark install/import behavior on PowerShell 7+.

Benchmark Markdown reports include a neutral scenario summary grouped by scenario, operation, and engine, followed by the detailed run table. The report records observed counts and timing statistics without making unproven performance claims. Compatibility baseline routing now covers install, save, update, and publish; live benchmark evidence still needs to be collected for the unchecked scenarios below.

### Current Host Smoke Evidence

- [x] 2026-06-27: PowerShell 7.6.3 imported `PSPublishModule` from the local `net10.0` build, ran `Measure-ManagedModule` against a local folder feed, installed `Company.Tools` 1.0.0 into a custom root, validated the installed manifest version, and confirmed out-of-process imports in PowerShell 7 and Windows PowerShell returned version 1.0.0.
- [x] 2026-06-27: Windows PowerShell 5.1.26100.8655 imported `PSPublishModule` from the local `net472` build, ran the same managed benchmark scenario, and confirmed out-of-process imports in PowerShell 7 and Windows PowerShell returned version 1.0.0.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both ran `Measure-ManagedModule -Operation Save` against the same local folder feed, saved `Company.Tools` 1.0.0 into empty custom roots, validated the saved manifest version, and confirmed out-of-process imports in both hosts returned version 1.0.0.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both seeded `Company.Tools` 1.0.0 with `Install-ManagedModule`, ran `Measure-ManagedModule -Operation Update` against a local folder feed containing 1.1.0, validated the updated manifest version, and confirmed out-of-process imports in both hosts returned version 1.1.0.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both ran `Publish-ManagedModule` against local folder feeds, produced `.nupkg` packages for simple source modules, and installed the published packages back through `Install-ManagedModule`.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both ran `Measure-ManagedModule -Operation Save` twice against the same local folder feed and destination root; the first save returned `Installed`, and the warm-path save returned `AlreadyInstalled`.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both seeded `Company.Tools` 1.0.0 with `Measure-ManagedModule -Operation Install`, then ran `Measure-ManagedModule -Operation Update -Version 1.0.0`; the update returned `UpToDate` with previous and target version 1.0.0.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both ran cold and warm cache install measurements against a local folder feed; the cold install returned `Installed` with `FromCache = false`, and the warm install into a fresh root returned `Installed` with `FromCache = true`.
- [x] 2026-06-27: `Measure-ManagedModule` command-surface contract tests installed a local package graph with one root module and six transitive dependency packages, recorded dependency/package totals, and validated the deepest dependency path.
- [x] 2026-06-27: `Measure-ManagedModule -Operation Publish` command-surface contract tests published a module that declared `RequiredModules`, verified the dependency was present in the target local feed, and confirmed the generated package nuspec preserved dependency metadata.
- [x] 2026-06-27: `Measure-ManagedModule -Operation Find` service contract tests queried a private NuGet v3 metadata endpoint with Basic credentials, recorded request-count evidence, and selected the latest stable version without downloading or extracting packages.
- [x] 2026-06-27: `Measure-ManagedModule` command-surface contract tests installed a synthetic heavy package payload with 65 extracted files and more than 250 KB of extracted bytes, recording extraction timing, file count, and final disk size.
- [x] 2026-06-27: `Measure-ManagedModule -Operation Publish` command-surface contract tests published a binary-root module package and verified the `.dll` payload was preserved in the generated `.nupkg`.
- [x] 2026-06-27: Managed repair/conflict contract tests cover source repair, scoped missing-copy repair, and command-surface loaded-module update blocking without relying on PowerShellGet, PSResourceGet, or external executables.
- [x] 2026-06-27: Managed path audit found external PowerShell runner usage only in explicit compatibility benchmark engines and import-validation hosts; managed install, save, update, publish, and ModuleState managed delivery stay in C# services.
- [x] 2026-06-27: Managed benchmark service tests installed and saved a module from a private NuGet v3-style feed with Basic credentials, validating metadata, package download, extraction, request counts, and final manifest version.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both installed latest `ThreadJob` 2.1.0 from the public PowerShell Gallery NuGet v2 endpoint into temp module roots with `Measure-ManagedModule -Operation Install`, validated manifest version 2.1.0, and recorded package bytes, extracted bytes, file count, and repository request count.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both installed latest `PSScriptAnalyzer` 1.25.0 from the public PowerShell Gallery NuGet v2 endpoint, validating a medium public package with 14.6 MB downloaded, 299 MB extracted, and 48 files.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both installed latest `Microsoft.Graph.Users` 2.38.0 with `-AcceptLicense`, resolving and installing its related dependency package through the managed engine and recording 2 packages, 65 total files, and 71 MB total extracted bytes.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both ran `Find-ManagedModule -Name PSScript*` against the public PowerShell Gallery NuGet v2 endpoint, returning package-id wildcard results with `PSScriptAnalyzer` 1.25.0 first.
- [x] 2026-06-27: PowerShell 7.6.3 and Windows PowerShell 5.1.26100.8655 both ran `Measure-ManagedModule -Operation Save -Engine Managed,PowerShellGet,PSResourceGet` for `ThreadJob` 2.1.0 into temp roots with `-AllowClobber`; all three engines completed successfully and recorded comparable save evidence.
- [x] 2026-06-27: `Install-PrivateModule` and `Update-PrivateModule` wrapper tests install/update through `-Transport ManagedModule` from direct local feeds and saved NuGet repository profiles without invoking compatibility repository registration or interactive credential prompts.
- [ ] Large many-dependency public-feed, mixed-version family, and publish comparison evidence still need separate proof.

## Benchmark Scenarios

- [x] Small public module install.
- [x] Medium public module install.
- [ ] Large public module with many dependencies.
- [ ] Related module family with mixed versions.
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
- [ ] Make managed transport the default for `Install-PrivateModule` after install parity is proven.
- [ ] Make managed transport the default for `Update-PrivateModule` after update parity is proven.
- [x] Route required-module mirroring through the managed engine after save/publish parity is proven.
- [ ] Mark compatibility transport as legacy only after benchmark and compatibility gates pass.
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
- [ ] Benchmarks prove correctness and performance on Windows PowerShell 5.1 and PowerShell 7+.
- [ ] Compatibility fallback remains only where provider support is explicitly incomplete.
- [x] Public docs stay vendor-neutral and avoid naming community comparison projects.

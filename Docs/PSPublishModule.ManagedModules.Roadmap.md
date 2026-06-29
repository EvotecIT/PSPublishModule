# Managed Module Engine Roadmap

This roadmap tracks the plan for a managed C# module lifecycle engine in PowerForge and PSPublishModule. The target is compatibility with the common PowerShellGet and PSResourceGet user workflows while removing those tools, external executables, and embedded PowerShell scripts from the core install/save/publish path over time.

The public PowerShell surface should stay thin. Reusable behavior belongs in PowerForge, with PSPublishModule cmdlets handling parameter binding, `ShouldProcess`, pipeline output, and Spectre.Console summaries.

## Principles

- [x] Keep the core implementation in managed C#.
- [x] Do not use `powershell.exe`, `pwsh`, `dotnet.exe`, `nuget.exe`, or embedded `.ps1` scripts for the new managed engine path.
- [x] Keep PowerShellGet and PSResourceGet as compatibility baselines and temporary fallbacks, not as the long-term engine.
- [x] Preserve easy migration from existing `Install-Module`, `Save-Module`, `Publish-Module`, `Install-PSResource`, `Save-PSResource`, and `Publish-PSResource` usage.
- [x] Keep `Install-PrivateModule` and `Update-PrivateModule` as thin compatibility/convenience wrappers with opt-in managed transport.
- [x] Make `Get-ManagedModule`, `Update-ManagedModule`, and `Repair-ManagedModule` the day-to-day estate maintenance entrypoints while keeping ModuleState plan objects as internal engine vocabulary.
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
- [x] Keep the managed command names as the public contract and do not export unreleased public/private alias families.
- [x] Introduce `Get-ManagedModule` as the PowerShell-native installed inventory surface.
- [x] Introduce `Repair-ManagedModule` as the one-stop stale/drift/family/source maintenance surface.
- [x] Remove unreleased `Get-ModuleState`, `Get-ModuleStatePlan`, `Test-ModuleState`, `Invoke-ModuleStatePlan`, and `Invoke-ModuleState` public exports; the clean public state workflow is `Get-ManagedModule`, `Update-ManagedModule`, and `Repair-ManagedModule -Plan`.
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
- [x] Support `Update-ManagedModule` without `-Name` so the command updates installed modules from the selected roots like `Update-Module`.
- [ ] Finish exact `-Force`, `-TrustRepository`, `-SkipPublisherCheck`, and signature-check semantics so compatibility choices are explicit and unsurprising.
- [x] Define exact install/save/update semantics for `-Force`, `-AllowClobber`, and `-AcceptLicense` so the common managed delivery path is explicit and unsurprising.
- [ ] Complete managed Authenticode/catalog validation compatible with PowerShellGet/PSResourceGet expectations, including explicit catalog policy evidence, timestamped signatures, and short-lived certificate behavior.
- [x] Add initial Windows managed `-AuthenticodeCheck` support for install/save/update by validating extracted signable files before promotion.

## Current Managed Maintenance Direction

- [x] `Get-ManagedModule` inventories installed modules and returns module rows by default.
- [x] `Update-ManagedModule` updates named modules or, when no name is supplied, all discovered modules in the selected scope/root.
- [x] `Repair-ManagedModule` plans and applies estate maintenance through the ModuleState engine with managed delivery as the default transport.
- [x] Finish `Repair-ManagedModule` proof for loaded-module safety and old-version cleanup.
- [ ] `-Plan` remains the non-mutating inspection switch across install, update, and repair flows.
- [ ] `-WhatIf` remains the operator safety gate for mutations.
- [ ] Destructive cleanup remains separately gated instead of being implied by update.
- [ ] ModuleState plan/apply types remain reusable internal implementation vocabulary; unreleased ModuleState cmdlets do not need compatibility shims or aliases.

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
- ModuleState desired-state objects can carry `ExpectedPackageSha256` / `PackageSha256` / `Sha256`; managed transport preserves it in repair plans, prepared commands, and managed delivery.
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
- Direct `Publish-ManagedModule` and existing repository publish dependency validation honor `PrivateData.PSData.ExternalModuleDependencies` from the module manifest, so external runtime dependencies do not have to exist in the publish target feed.

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
- [x] Skip dependency delivery when an already installed dependency version satisfies a declared bounded range and no dependency package trust policy needs validation.
- [x] Implement receipt creation after successful install.
- [x] Emit typed install result objects.
- [x] Add Spectre.Console summary output.
- [x] Add tests for normal install.
- [x] Add tests for scoped install.
- [x] Add tests for exact side-by-side install.
- [x] Add tests for failed extraction rollback.
- [x] Add tests for installed dependency range satisfaction and trust-policy package-validation behavior.
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
- [x] Skip publish dependency validation for manifest `RequiredModules` also listed in `PrivateData.PSData.ExternalModuleDependencies`.
- [x] Emit typed publish result objects.
- [x] Add Spectre.Console summary output.
- [x] Add tests for package creation.
- [x] Add tests for duplicate publish classification.
- [x] Add tests for private-feed publish request shaping.
- [x] Benchmark local-folder publish against `Publish-Module` and `Publish-PSResource`.
- [x] Validate local-feed publish smoke on Windows PowerShell 5.1.
- [x] Validate local-feed publish smoke on PowerShell 7+.

## Phase 9: Managed Maintenance Integration

- [x] Teach the maintenance engine to use the managed engine for install/update/save operations.
- [x] Teach `Repair-ManagedModule` to plan and apply managed install/update delivery.
- [x] Teach `Repair-ManagedModule` to plan and apply managed save delivery where repair workflows need offline materialization.
- [x] Keep `Get-ManagedModule -AsInventory` object-first for support bundles and advanced planning.
- [ ] Add managed-family plan/test/apply surfaces only where `Repair-ManagedModule -Plan` and typed result objects are not enough.
- [ ] Remove unreleased ModuleState public cmdlets/exports after the managed-family surface covers the inspected workflows.
- [x] Use managed delivery as the default repair transport.
- [ ] Retire compatibility transport from the primary repair path once managed parity is proven by benchmarks and compatibility tests.
- [x] Ensure maintenance receipts contain managed-engine evidence.
- [x] Ensure summaries explain what changed, what was skipped, and why.
- [x] Add tests for managed maintenance delivery command shaping.
- [x] Add tests for managed maintenance install.
- [x] Add tests for managed maintenance update.
- [x] Add tests for managed maintenance save.
- [x] Add tests for managed maintenance source repair.
- [x] Add tests for source/scope/family repairs through the managed engine.

## Phase 10: Benchmarks And Proof

- [x] Keep benchmark execution as repo tooling under `Benchmarks` instead of a shipped module cmdlet or PowerForge service.
- [x] Add a PowerShell benchmark harness that can run the same scenario through managed, PowerShellGet, and PSResourceGet paths.
- [x] Write direct-run benchmark CSV/JSON artifacts under `Ignore\Benchmarks\ManagedModules` and suite artifacts under compact `Ignore\Benchmarks\MM` paths for Windows PowerShell 5.1.
- [x] Record host, runtime, repository, module, operation, engine, status, elapsed time, output count, installed version, and output path.
- [x] Support comma-separated `-Engine` and `-Operation` arguments for quick matrix runs.
- [x] Support named scenario selection for targeted heavy-suite reruns.
- [x] Run native install comparisons only inside disposable child hosts with benchmark-owned profile, module, cache, and temp roots.
- [x] Add an explicit disposable-host lane for native `Install-Module` and `Install-PSResource`.
- [x] Add an explicit disposable-host update lane for native `Update-Module` and `Update-PSResource`.
- [x] Add `-AcceptLicense` support to the benchmark harness and pass it only when explicitly requested.
- [x] Add warm-cache and cold-cache modes.
- [x] Add optional managed performance gates so comparison and suite runs can fail when managed falls behind the fastest successful engine by rank or ratio.
- [x] Add publish comparisons against local folder feeds.
- [x] Add import validation after install/save/update for PowerShell 5.1 and PowerShell 7+.
- [x] Add a managed repair-plan benchmark lane that stages a stale module root and times `Repair-ManagedModule -Plan -Latest` without applying changes.
- [x] Add managed repair-plan benchmark lanes for source drift, scope drift, family coherence, loaded-module safety, and cleanup planning.
- [x] Add file and byte counts for saved and installed output roots.
- [x] Add managed install detail artifacts with package count, dependency count, per-package elapsed time, download time, extraction time, promotion time, repository requests, cache hits, and byte counts.
- [x] Surface managed detail medians in benchmark summary, comparison, and suite summary artifacts so optimization runs can be ranked by phase, requests, bytes, and cache hits without opening every detail JSON file.
- [x] Write suite-level managed host comparison artifacts so PowerShell 7 and Windows PowerShell 5.1 deltas are visible without manually joining suite summary rows.
- [x] Write suite-level managed optimization target artifacts that identify the largest measured phase per managed row and keep request/cache/package/byte context beside it.
- [x] Split managed benchmark request evidence into whole-operation repository requests and package-delivery requests so parallel dependency detail rows do not hide metadata/version-resolution costs.
- [x] Split managed package count evidence into dependency-tree rows and unique package/status counts so heavy families with repeated shared dependencies do not inflate optimization targets.
- [x] Surface repair-plan maintenance action and finding medians in benchmark summary, comparison, and suite summary artifacts so repair optimization is not hidden behind install-only package metrics.
- [x] Add explicit no-op and forced-reinstall benchmark operations for install, save, and update so compatibility semantics are measured instead of inferred from fresh installs.
- [x] Add Graph/Az/Teams/Exchange-heavy scenario presets for suite runs.
- [x] Add a README section explaining which comparisons are safe on a developer machine and which require disposable hosts.
- [x] Document a same-source ModuleFast comparison gate so engine speed is not confused with package-source/backend variance.
- [x] Promote the same-source full Graph ModuleFast comparison into a named `SpeedGate` suite scenario with scenario-scoped engines, repository source, and suite metadata.
- [x] Promote full-family Graph/Az save comparisons into a named `HeavySaveGate` suite so broad save throughput is proven separately from install lifecycle behavior.
- [x] Promote managed-only full-family Graph/Az warm-cache save experiments into a named `HeavySaveCacheGate` suite so download/source/cache behavior is measured separately from save-capable provider races.
- [x] Move save benchmark payload roots to short disposable temp paths while keeping CSV/JSON artifacts under the benchmark run directory, so Windows PowerShell 5.1/native-provider comparisons are not distorted by deep repository-backed paths.
- [x] Move warm managed package-cache roots to short run-scoped temp paths so repeated rows can prove cache reuse even when expanded output roots are deleted after measurement.
- [x] Measure Graph/Az/Teams/Exchange-heavy scenario presets on PowerShell 5.1 and PowerShell 7+.

The benchmark harness is intentionally outside the shipped module. The module owns managed module behavior; the repository benchmark scripts own measurement, comparison, and artifact layout.

### Current Host Smoke Evidence

- [x] 2026-06-28: PowerShell 7 imported the local Release build and ran `ThreadJob` 2.1.0 through managed, PSResourceGet, and PowerShellGet find/save comparisons; managed was the fastest successful find and save engine in that run.
- [x] 2026-06-28: PowerShell 7 ran disposable-host install comparison for `ThreadJob` 2.1.0. Managed installed into the benchmark module root in 2469 ms, PSResourceGet installed into the isolated CurrentUser root in 3263 ms, and PowerShellGet installed into the isolated CurrentUser root in 8554 ms.
- [x] 2026-06-28: Windows PowerShell 5.1 ran disposable-host install comparison for `ThreadJob` 2.1.0. Managed installed into the benchmark module root in 4317 ms, PSResourceGet installed into the isolated CurrentUser root in 5592 ms, and PowerShellGet installed into the isolated CurrentUser root in 14207 ms.
- [x] 2026-06-28: PowerShell 7 ran a heavier `Microsoft.Graph.Authentication` find/save comparison; managed find succeeded, while managed save correctly required explicit license acceptance before saving the package.
- [x] 2026-06-28: PowerShell 7 ran disposable-host install comparison for `Microsoft.Graph.Authentication` 2.38.0. Managed installed in 2276 ms, PSResourceGet installed in 2350 ms, and PowerShellGet installed in 13482 ms over about 40 MB of output. Windows PowerShell 5.1 ran the same package with managed succeeding in 3864 ms while native providers failed under their own install paths.
- [x] 2026-06-28: PowerShell 7 ran disposable-host full `Microsoft.Graph` 2.38.0 install. Managed installed 40 manifests and about 1.05 GB in 46.91 seconds, PSResourceGet installed the same manifest count and about 1.06 GB in 63.21 seconds, and PowerShellGet failed partway with a temp-disk error. After teaching unbounded managed install/plan resolution to use the latest-version repository query, managed full Graph install measured 44.69 seconds in a managed-only rerun. After adding a larger archive copy buffer and de-duplicating directory creation during extraction, managed full Graph install measured 40.81 seconds with 483 files and about 1.05 GB of output.
- [x] 2026-06-28: PowerShell 7 ran full `Microsoft.Graph` 2.38.0 find/save with license acceptance. Managed save completed in 41.03 seconds versus 64.89 seconds for PSResourceGet and 82.99 seconds for PowerShellGet over about 1.05 GB of saved output. The initial exact find path exposed a metadata lookup gap, so `Find-ManagedModule` now uses a latest-version query when `-AllVersions` is not requested. A rotated three-run full Graph find comparison then measured managed at 385 ms, PSResourceGet at 392 ms, and PowerShellGet at 1653 ms.
- [x] 2026-06-28: Windows PowerShell 5.1 imported the local `net472` build and ran the full `Microsoft.Graph` find smoke through managed and PowerShellGet engines; managed returned version 2.38.0 in 347 ms and ranked first in that run.
- [x] 2026-06-28: PowerShell 7 ran disposable-host full `Az` 16.0.0 managed install with explicit license acceptance. Managed installed 2790 files and about 623 MB in 115.31 seconds, giving the first whole-Az baseline for the next optimization pass.
- [x] 2026-06-28: Windows PowerShell 5.1 reran disposable-host `ThreadJob` managed install after the archive extraction optimization; managed completed in 2.96 seconds.
- [x] 2026-06-28: PowerShell 7 reran full `Az` 16.0.0 managed install with package-level detail output. The run completed in 121.48 seconds; the managed detail artifact reported 204 packages, 203 dependencies, 119.15 seconds spent in root dependency delivery, 78.14 seconds summed package download time, 2.42 seconds summed extraction time, and 0.41 seconds summed promotion time. This identifies dependency scheduling and repository delivery as the next optimization target; extraction is no longer the dominant cost for Az.
- [x] 2026-06-28: Managed package download/copy streams now use a larger async sequential buffer. PowerShell 7 reran full `Az` 16.0.0 managed install in 98.84 seconds; summed package download time dropped to 65.96 seconds over the same 204 packages and 137.5 MB of downloaded package bytes. PowerShell 7 reran full `Microsoft.Graph` 2.38.0 managed install in 40.03 seconds with 78 packages, 37.22 seconds summed download time, and about 186.5 MB of downloaded package bytes.
- [x] 2026-06-28: Managed dependency installs now use bounded parallel delivery for independent direct dependencies while preserving per-branch cycle detection, per-module final promotion locks, and coordinated package-cache writes. PowerShell 7 reran full `Microsoft.Graph` 2.38.0 managed install in 13.26 seconds with 78 packages, 77 dependencies, 81 repository requests, and 186.5 MB of downloaded package bytes. PowerShell 7 reran full `Az` 16.0.0 managed install in 17.86 seconds with 204 packages, 203 dependencies, 411 repository requests, and 137.5 MB of downloaded package bytes. Summed package download/extraction time is now expected to exceed wall-clock time because dependency packages are delivered concurrently.
- [x] 2026-06-28: Windows PowerShell 5.1 reran full latest-version managed install after shortening disposable benchmark roots and managed temp stage paths to avoid classic Windows path-length noise. `Microsoft.Graph` 2.38.0 installed in 15.63 seconds with 78 packages and 77 dependencies. `Az` 16.0.0 installed in 49.50 seconds with 204 packages and 203 dependencies. The corrected suite omits empty `-Version` arguments and the comparison runner now treats omitted version as latest, so PS5 and PS7 heavy-suite evidence measures the same current package versions.
- [x] 2026-06-28: The benchmark harness now has an explicit update lane that installs `-UpdateBaselineVersion` outside the timed window and then times only the update operation in the same disposable host root. PowerShell 7 updated `ThreadJob` from 2.0.3 to 2.1.0 in 2855 ms with managed, 4479 ms with PSResourceGet, and 11238 ms with PowerShellGet; ModuleFast is an explicit skip because it does not expose update. Windows PowerShell 5.1 updated the same stale module in 4457 ms with managed and 7329 ms with PSResourceGet, while PowerShellGet failed inside its metadata conversion path and left 2.0.3 installed.
- [x] 2026-06-28: The benchmark harness now supports `-ValidateImport`, which imports the highest-version manifest from the benchmark output root after timed install/save/update operations and records import status separately from operation timing. PowerShell 7 validated `ThreadJob` install/update outputs from managed, ModuleFast install, PSResourceGet, and PowerShellGet; every successful row imported 2.1.0 from the output root. Windows PowerShell 5.1 validated the same managed, PSResourceGet, and PowerShellGet install outputs and managed/PSResourceGet update outputs; PowerShellGet update still failed in its own path before import. This exposed a small-module PS5.1 update optimization target: managed was correct but slower than PSResourceGet on the import-gated stale `ThreadJob` update.
- [x] 2026-06-28: PowerShell 7 and Windows PowerShell 5.1 also ran import-gated `ThreadJob` save comparisons. Every successful saved output imported 2.1.0 from its benchmark output root. PowerShell 7 measured PSResourceGet at 2408 ms, managed at 2734 ms, and PowerShellGet at 23304 ms. Windows PowerShell 5.1 measured managed at 2200 ms, PSResourceGet at 2515 ms, and PowerShellGet at 14616 ms.
- [x] 2026-06-28: The benchmark harness now supports explicit `-CacheMode Default|Cold|Warm` for update benchmarks. `Cold` clears disposable provider/package caches after the baseline install; `Warm` preserves native provider caches and gives managed delivery an explicit package cache folder. Windows PowerShell 5.1 import-gated `ThreadJob` update measured managed at 7978 ms versus PSResourceGet at 7519 ms in cold mode, and managed at 6861 ms versus PSResourceGet at 7449 ms in warm mode. PowerShell 7 measured managed at 4517 ms versus PSResourceGet at 4022 ms in cold mode, and managed at 4088 ms versus PSResourceGet at 3818 ms in warm mode. The managed warm detail artifact reported no package cache hits for this specific target/dependency combination, so these small-package numbers should be treated as close-run smoke evidence rather than a stable optimization claim.
- [x] 2026-06-28: Managed dependency install now reuses an installed dependency version when it satisfies a bounded dependency range and no dependency trust policy needs package metadata validation. Focused tests prove both the skip and the trust-policy non-skip. PowerShell 7 reran import-gated warm `ThreadJob` update in 3449 ms with managed, 4213 ms with PSResourceGet, and 20898 ms with PowerShellGet. Windows PowerShell 5.1 reran the same lane in 4461 ms with managed and 5752 ms with PSResourceGet, while PowerShellGet failed its own update path. The managed detail artifacts still installed `Microsoft.PowerShell.ThreadJob` 2.2.0 in this scenario, so this public benchmark remains timing/import evidence rather than direct proof of the installed-dependency skip branch.
- [x] 2026-06-28: Repeated rotated `ThreadJob` update with `-ValidateImport -RepeatCount 3` stabilized the small stale-update lane. Managed ranked first by median in all four host/cache lanes: PowerShell 7 cold 3983 ms versus PSResourceGet 6011 ms and PowerShellGet 15368 ms; PowerShell 7 warm 3511 ms versus PSResourceGet 5071 ms and PowerShellGet 14113 ms; Windows PowerShell 5.1 cold 6085 ms versus PSResourceGet 7880 ms with PowerShellGet failing all runs; Windows PowerShell 5.1 warm 5634 ms versus PSResourceGet 7086 ms with PowerShellGet failing all runs. ModuleFast remained an explicit update skip.
- [x] 2026-06-28: Repeated rotated PowerShell 7 `ThreadJob` save with `-ValidateImport -RepeatCount 3` stabilized the prior small-save concern. Managed median was 1929 ms, PSResourceGet median was 2422 ms, and PowerShellGet median was 3146 ms; every successful output imported version 2.1.0. ModuleFast remained an explicit save skip.
- [x] 2026-06-28: Managed repository reads now coalesce concurrent anonymous remote version, latest-version, and search queries so parallel dependency delivery can share identical in-flight metadata requests. Credentialed and cancellable requests intentionally remain independent to avoid mixing private-feed authorization or cancellation semantics. Focused tests cover coalesced version/search reads and the credentialed non-coalescing path.
- [x] 2026-06-28: PowerShell 7 reran a managed-only full `Microsoft.Graph` 2.38.0 install after repository read coalescing. The run succeeded in 26.25 seconds with 78 packages, 77 dependencies, 81 repository requests, 186.5 MB downloaded package bytes, and about 1.05 GB of output. This confirms the optimization is safe in a heavy public-gallery run, but it did not reduce the Graph request count in this scenario; the next heavy optimization should be chosen from measured package delivery/extraction/promotion detail rather than assuming metadata coalescing helps every family.
- [x] 2026-06-28: PowerShell 7 ran full `Microsoft.Graph` 2.38.0 install with managed and ModuleFast pointed at the same `https://pwsh.gallery/index.json` source. The first single run measured managed at 3.44 seconds versus ModuleFast at 6.65 seconds. The follow-up repeated rotated gate passed with `-RepeatCount 3 -ManagedMaxRank 1`; managed median was 3.57 seconds versus ModuleFast at 8.93 seconds, with 41 managed repository requests, 186.5 MB downloaded, and no gate violations. This same-source gate separates engine performance from the default-source comparison where managed uses PSGallery and ModuleFast uses its configured source.
- [x] 2026-06-28: The same-source full Graph install gate is now a named `SpeedGate` suite scenario (`Graph.Full.SameSource`). A PowerShell 7 suite smoke with `-ManagedMaxRank 1` passed, emitted scenario-scoped engines and repository evidence in the suite summary/metadata, and measured managed at 3.89 seconds with 41 repository requests and 186.5 MB downloaded.
- [x] 2026-06-28: The benchmark suite now writes compact suite folders under `Ignore\Benchmarks\MM` and uses compact host/scenario labels so Windows PowerShell 5.1 save benchmarks avoid legacy path-length failures. A warm `LifecycleGate` suite run with `-ManagedMaxVsFastest 1.25` passed on PowerShell 7 and Windows PowerShell 5.1; managed ranked first for install no-op, install force, save no-op, and save force on both hosts in that run.
- [x] 2026-06-28: After splitting managed install dependency delivery into a dedicated partial, the warm `LifecycleGate` suite run passed again on PowerShell 7 and Windows PowerShell 5.1; managed ranked first for install no-op, install force, save no-op, and save force on both hosts.
- [x] 2026-06-28: PowerShell 7 reran the same-source full `Microsoft.Graph` install `SpeedGate` after the install-service split. The strict `-ManagedMaxRank 1` suite passed with managed median 5821.60 ms versus ModuleFast rows at 7596.55 ms, 10765.68 ms, and 12233.83 ms.
- [x] 2026-06-28: Managed PSGallery NuGet v2 fallback downloads now use the same 1 MB async sequential package stream as the NuGet v3/local paths. `Microsoft.Graph.Authentication` save improved on PowerShell 7 from 1715.04 ms to 1409.81 ms, narrowing the PSResourceGet gap from 1.35x to 1.03x, and improved on Windows PowerShell 5.1 from 2449.07 ms to 1881.92 ms while remaining fastest.
- [x] 2026-06-28: Managed package delivery now computes SHA256 while copying HTTP/local package streams instead of reopening each `.nupkg` for a second full read. A repeated rotated PowerShell 7 `Microsoft.Graph.Authentication` save comparison improved managed median from 1763.81 ms before the change to 1490.32 ms after the change; PSResourceGet measured 1410.84 ms in the after run, so this is a real absolute improvement but not the final PS7 save win.
- [x] 2026-06-28: Managed repository HTTP requests now use `ResponseHeadersRead`, so package responses stream directly into the managed copy/hash path instead of being fully buffered by `HttpClient` first. A repeated rotated PowerShell 7 `Microsoft.Graph.Authentication` save comparison ranked managed first at 1631.76 ms versus PSResourceGet at 1855.37 ms. A repeated rotated Windows PowerShell 5.1 run using a short output root ranked managed first at 1606.17 ms versus PSResourceGet at 1701.74 ms.
- [x] 2026-06-28: `SaveGate` now promotes the repeated rotated `Microsoft.Graph.Authentication` save comparison into the benchmark suite. A strict `-ManagedMaxRank 1` trial exposed expected public-gallery variance on PowerShell 7 with managed at 1.01x, and the practical `-ManagedMaxVsFastest 1.05` gate passed on PowerShell 7 and Windows PowerShell 5.1 with managed ranking first on both hosts.
- [x] 2026-06-28: Benchmark suite scenarios can now own performance gate defaults and `-UseScenarioGates` applies them. This lets `SpeedGate` keep strict rank 1, `SaveGate` own its save-specific threshold, and `LifecycleGate` use its 1.25 warm-cache ratio without repeating those thresholds on every command. A `SaveGate` run with scenario gates passed on PowerShell 7 and Windows PowerShell 5.1 with managed ranking first on both hosts.
- [x] 2026-06-28: Suite runs now write `suite-host-comparison.csv` and `suite-host-comparison.json`, joining matching managed rows across PowerShell 7 and Windows PowerShell 5.1. A tiny managed-only `ThreadJob` find smoke produced a `Compared` row with PowerShell 7 at 457.05 ms, Windows PowerShell 5.1 at 401.06 ms, and a 0.88x host ratio before the throwaway output directory was removed.
- [x] 2026-06-28: Suite runs now write `suite-optimization-targets.csv` and `suite-optimization-targets.json`, turning managed detail medians into an optimization queue. Rows identify the largest measured phase (`RootDependency`, `Download`, `Extraction`, `Promotion`, `HarnessOverhead`, or `Uninstrumented`) and retain rank, ratio, request, package, cache, and downloaded-byte context. A short-root managed-only `ThreadJob` save smoke across PowerShell 7 and Windows PowerShell 5.1 produced `Download` bottleneck rows for both hosts: PS7 managed save 1924.56 ms with 1509.88 ms download time, and PS5.1 managed save 2135.99 ms with 1552.93 ms download time.
- [x] 2026-06-28: Managed repository HTTP handlers now request gzip/deflate metadata responses, and Brotli on modern target frameworks, while preserving explicit redirect handling for package downloads. Focused repository tests and a multi-target PSPublishModule build passed. A short-root managed-only `ThreadJob` save smoke still identified package download as the bottleneck on both hosts, so the next heavy optimization should target package source/CDN latency, cache strategy, or request count rather than extraction/promotion.
- [x] 2026-06-28: The benchmark harness now resolves update baselines automatically when `Update` is requested without `-UpdateBaselineVersion`. The resolver chooses the latest stable package version lower than the requested target, records the resolved baseline/target in child metadata and result rows, and the suite summary carries those values forward. PSGallery discovery uses the public NuGet v2 read feed because the current host receives 403 responses from PSGallery v3 metadata endpoints; generic NuGet v3 feeds still use v3 package-base metadata. PowerShell 7 proved the no-baseline path with `ThreadJob` 2.0.3 -> 2.1.0 in 3009 ms and suite-level `Microsoft.Graph.Authentication` 2.37.0 -> 2.38.0 in 4229 ms, both with successful import validation. Windows PowerShell 5.1 proved the same baseline resolver returns `ThreadJob` 2.0.3 -> 2.1.0.
- [x] 2026-06-28: The benchmark harness now caps post-timing import validation with `-ImportTimeoutSeconds` and records `TimedOut` instead of hanging a heavy suite. Managed-only heavy update runs succeeded on both hosts: PowerShell 7 updated full `Microsoft.Graph` 2.37.0 -> 2.38.0 in 8909 ms and full `Az` 15.6.1 -> 16.0.0 in 10188 ms; Windows PowerShell 5.1 updated them in 16045 ms and 25002 ms. Full Graph and Az import validation timed out after 30 seconds on both hosts. PowerShell 7 updated `MicrosoftTeams` 7.7.0 -> 7.8.0 in 5050 ms and `ExchangeOnlineManagement` 3.9.2 -> 3.10.0 in 3285 ms with successful imports; Windows PowerShell 5.1 updated them in 7115 ms and 5015 ms with successful imports. The slowest proven managed update lane is full Az on Windows PowerShell 5.1, with root dependency delivery and summed download time dominating detail artifacts.
- [x] 2026-06-28: The benchmark harness now has an initial managed `RepairPlan` lane. It stages a stale module root outside the timed window, then times `Repair-ManagedModule -Plan -Latest` planning without executing changes. PowerShell 7 planned stale `ThreadJob` 2.0.3 -> 2.1.0 in 1604 ms; Windows PowerShell 5.1 planned the same stale root in 2660 ms. The detail artifacts recorded one planned maintenance action and no findings. ModuleFast, PSResourceGet, and PowerShellGet are explicit skips for this operation because they do not expose equivalent estate repair planning.
- [x] 2026-06-28: The `RepairPlan` benchmark now supports scenario-specific rows. `-RepairScenario SourceDrift` installs the target version outside the timed window, stamps disposable module metadata with a different source, writes a maintenance receipt expecting the requested repository, and times repair planning without applying changes. Managed-only `ThreadJob` source-drift smoke measured 643 ms on PowerShell 7 and 1088 ms on Windows PowerShell 5.1; the detail artifacts recorded a forced repair install targeting `PSGallery`.
- [x] 2026-06-28: The `RepairPlan` benchmark now includes `-RepairScenario ScopeDrift`. It installs the target version outside the timed window, writes a maintenance receipt expecting `CurrentUser`, and times repair planning without applying changes. Managed-only `ThreadJob` scope-drift smoke measured 734 ms on PowerShell 7 and 665 ms on Windows PowerShell 5.1; the detail artifacts recorded a repair install targeting `CurrentUser`.
- [x] 2026-06-28: The `RepairPlan` benchmark now includes `-RepairScenario FamilyCoherence`. It seeds lightweight synthetic `Microsoft.Graph.Authentication` 2.36.0 and `Microsoft.Graph.Users` 2.38.0 manifests, passes the built-in Graph family policy, and times repair planning without applying changes. Managed-only smoke measured 654 ms on PowerShell 7 and 826 ms on Windows PowerShell 5.1; the detail artifacts recorded a repair update for `Microsoft.Graph.Authentication` to `=2.38.0`.
- [x] 2026-06-28: The `RepairPlan` benchmark now includes `-RepairScenario LoadedModuleSafety`. It seeds synthetic `ThreadJob` 1.0.0 and 2.0.0 manifests, imports the stale 1.0.0 copy in the disposable child host, and times `Repair-ManagedModule -Plan -IncludeLoaded -Version 2.0.0` without applying changes. Managed-only smoke measured 686 ms on PowerShell 7 and 666 ms on Windows PowerShell 5.1; the detail artifacts recorded `ModuleState.LoadedVersionMismatch`, side-by-side, and source-unknown findings.
- [x] 2026-06-28: The `RepairPlan` benchmark now includes `-RepairScenario CleanupPlanning`. It seeds synthetic `ThreadJob` 1.0.0 and 2.0.0 manifests and times `Repair-ManagedModule -Plan -Cleanup OldVersions` without applying changes. Managed-only smoke measured 670 ms on PowerShell 7 and 671 ms on Windows PowerShell 5.1; the detail artifacts recorded a planned `Remove` action for 1.0.0 and a no-op for retained 2.0.0.
- [x] 2026-06-28: Managed repository request accounting is now scoped per async install/update operation and benchmark details split whole-operation requests from package-delivery requests. A fresh PowerShell 7 full `Microsoft.Graph` install comparison measured managed at 7414 ms versus ModuleFast at 8906 ms, with managed recording 78 packages, 77 dependencies, 81 whole-operation repository requests, 80 package-delivery requests, and 186.5 MB downloaded. Windows PowerShell 5.1 managed-only full Graph measured 13478 ms with 81 whole-operation requests and 78 package-delivery requests.
- [x] 2026-06-28: Managed benchmark detail artifacts now split package tree rows from unique package/status counts. A fresh PowerShell 7 full `Az` install comparison measured managed at 12858 ms while ModuleFast failed resolving `Az.DataTransfer(1.0.0)`. Managed recorded 204 dependency-tree package rows, 103 unique package outputs, 102 unique installed outputs, 1 unique already-installed output, 211 whole-operation repository requests, 206 package-delivery requests, and 137.5 MB downloaded.
- [x] 2026-06-28: Repair force semantics now flow through prepared delivery commands for install, update, and save actions. Focused tests prove `Repair-ManagedModule -Latest -Force -Plan` prepares a forced `Update-ManagedModule` command without executing mutation, and the PowerShell-facing command object exposes the effective `Force` flag.
- [x] 2026-06-28: Managed performance gates now fail when a gated comparison has no successful managed result instead of treating `ManagedRank = 0` as non-actionable. This prevents strict suite gates from passing after a managed failure while another engine succeeds.
- [x] 2026-06-28: The benchmark harness now has explicit `InstallNoOp`, `InstallForce`, `SaveNoOp`, `SaveForce`, `UpdateNoOp`, and `UpdateForce` operations. PowerShell 7 `ThreadJob` 2.1.0 import-gated smoke measured managed as fastest for install no-op (701 ms), save no-op (18 ms), save force (1115 ms), and update no-op (1000 ms); ModuleFast was fastest for install force (1128 ms versus managed 3531 ms), and PSResourceGet was fastest for update force (1426 ms versus managed 1834 ms). Windows PowerShell 5.1 measured managed as fastest for install no-op (888 ms), install force (1956 ms), save no-op (20 ms), save force (1089 ms), and update no-op (273 ms); PSResourceGet was fastest for update force (1148 ms versus managed 3375 ms). ModuleFast remained an explicit PS5.1 skip, PSResourceGet save force is an explicit skip because `Save-PSResource` exposes no force/reinstall save parameter on the inspected hosts, and Windows PowerShellGet update failed inside its own metadata conversion path.
- [x] 2026-06-28: Warm-cache force benchmarks now apply the managed package cache consistently to install, save, and update rows and write managed package-detail artifacts for save rows too. PowerShell 7 `ThreadJob` 2.1.0 force smoke measured managed `InstallForce` at 1151 ms versus ModuleFast at 944 ms, managed `SaveForce` at 332 ms, and managed `UpdateForce` at 1014 ms versus PSResourceGet as the next successful engine. Windows PowerShell 5.1 measured managed `InstallForce` at 1164 ms, `SaveForce` at 457 ms, and `UpdateForce` at 1554 ms versus PSResourceGet at 1384 ms. Managed detail artifacts showed one cache hit, zero package-delivery repository requests, and zero downloaded package bytes in each managed warm force row.
- [x] 2026-06-28: Comparison and suite benchmarks now support optional `-ManagedMaxRank` and `-ManagedMaxVsFastest` gates. Normal exploratory runs remain non-failing, while CI or release evidence runs can fail after writing artifacts when managed is not the fastest successful engine or exceeds an allowed ratio.
- [x] 2026-06-28: Managed benchmark artifacts now split managed command result elapsed time from benchmark wall-clock time. `ManagedRootElapsedMs` shows the engine result timing from the managed detail artifact, while `ManagedHarnessOverheadMs` shows the child host/import/wrapper remainder, so forced reinstall optimization work can target real engine cost instead of process isolation overhead.
- [x] 2026-06-28: Managed force delivery now uses a single-dependency fast path before parallel scheduling when a root package has exactly one dependency and the installed dependency already satisfies the declared range without trust-policy package validation. Current warm-cache `ThreadJob` force comparison measured PowerShell 7 managed `UpdateForce` fastest at 1046 ms, while `InstallForce` remains second to ModuleFast at 1088 ms versus 828 ms. Windows PowerShell 5.1 measured managed fastest for both `InstallForce` at 1150 ms and `UpdateForce` at 1186 ms. Detail artifacts show managed package delivery still uses cache hits, zero package-delivery repository requests, and zero downloaded package bytes.
- [x] 2026-06-28: Managed dependency satisfaction now treats an unbounded dependency declaration as satisfied by an already-installed acceptable stable dependency when no dependency trust-policy package validation is required. Current warm-cache `ThreadJob` force comparison measured managed fastest on both hosts: PowerShell 7 `InstallForce` 691 ms and `UpdateForce` 624 ms, Windows PowerShell 5.1 `InstallForce` 1035 ms and `UpdateForce` 788 ms. Detail artifacts show zero repository requests, one cache hit, zero downloaded package bytes, and root dependency timing under 10 ms for the managed force rows.
- [x] 2026-06-28: Repeated rotated warm `ThreadJob` force/no-op gates now pass with `-RepeatCount 3` and `-ManagedMaxRank 1` on both PowerShell 7 and Windows PowerShell 5.1. PowerShell 7 measured managed fastest for `InstallNoOp` at 552 ms, `InstallForce` at 650 ms versus ModuleFast at 886 ms, `SaveNoOp` at 13 ms, `SaveForce` at 21 ms, `UpdateNoOp` at 440 ms, and `UpdateForce` at 606 ms. Windows PowerShell 5.1 measured managed fastest for `InstallNoOp` at 539 ms, `InstallForce` at 798 ms, `SaveNoOp` at 15 ms, `SaveForce` at 24 ms, `UpdateNoOp` at 480 ms, and `UpdateForce` at 880 ms; ModuleFast is an explicit skip on Windows PowerShell 5.1.
- [x] 2026-06-28: Heavy benchmark runs can now use `-RemoveOutputRoots` to delete benchmark-owned expanded module output roots after CSV/JSON summaries, metadata, gates, and managed detail artifacts are written. This keeps repeated Graph/Az proof runs from leaving multi-gigabyte install/save roots while preserving the timing, package, request, cache, and byte-count evidence needed for optimization.
- [x] 2026-06-28: Managed install now coalesces in-flight equivalent dependency targets inside one install graph and skips coalesced waits that would create cross-branch dependency cycles. The managed dependency fan-out gate was raised to 32 after measurement. A repeated rotated PowerShell 7 full `Microsoft.Graph` install comparison with `-RemoveOutputRoots` measured one managed-first median run at 6561 ms versus ModuleFast at 7572 ms, but the follow-up strict `-ManagedMaxRank 1` gate still failed narrowly at managed 7474 ms versus ModuleFast 7352 ms. The managed detail artifact recorded 78 package-tree rows, 40 unique outputs, 40 installed rows, 38 already-satisfied coalesced dependency rows, 81 whole-operation repository requests, 80 package-delivery requests, and 186.5 MB downloaded. Treat full Graph install as near parity, not a stable strict-rank pass yet.
- [x] 2026-06-29: A heavy Save suite covered full `Microsoft.Graph`, full `Az`, `MicrosoftTeams`, and `ExchangeOnlineManagement` on PowerShell 7 and Windows PowerShell 5.1. Managed ranked first for every completed heavy Save comparison: PowerShell 7 measured Graph 4396 ms, Az 4020 ms, Teams 1682 ms, and ExchangeOnlineManagement 2535 ms; Windows PowerShell 5.1 measured Az 29924 ms, Teams 4142 ms, and ExchangeOnlineManagement 4088 ms. The first Windows PowerShell 5.1 Graph Save row wrote about 1.05 GB of module output and then failed while writing the `.powerforge` receipt at a 267-character path, while PSResourceGet and PowerShellGet also failed in their own save paths. Managed receipt IO is now long-path safe on Windows, and a targeted Windows PowerShell 5.1 managed-only full Graph Save rerun completed in 26715 ms with 78 package-tree rows, 40 unique outputs, 186.5 MB downloaded, and 1.05 GB of saved module content.
- [x] 2026-06-29: `SaveGate` now owns a strict managed-rank gate instead of the earlier 1.05 public-gallery tolerance. A repeated rotated strict `SaveGate` run passed on PowerShell 7 and Windows PowerShell 5.1 with managed rank 1 on both hosts: PowerShell 7 measured managed at 1122 ms and Windows PowerShell 5.1 measured managed at 1756 ms. Suite summary rows and metadata now record the effective gate threshold, so explicit command-line gates no longer look like looser scenario defaults in the artifacts.
- [x] 2026-06-29: Managed unbounded install no-op now resolves satisfying installed versions locally before querying the repository. The failing PowerShell 7 `Microsoft.Graph.Authentication` no-op path dropped from 21716 ms of engine time and two repository requests to 2.78 ms of engine time with zero repository requests. A repeated rotated strict `InstallNoOp` comparison passed with `-RepeatCount 3 -ManagedMaxRank 1`: managed ranked first at 554.69 ms end-to-end, ModuleFast measured 819.43 ms, and PSResourceGet measured 969.19 ms. The broader force/no-op promotion remains open because the same-session force rerun hit transient `www.powershellgallery.com` unreachable-network failures for managed and PSResourceGet.
- [x] 2026-06-29: `LifecycleGate` now includes strict exact-version install no-op/force scenarios for `Microsoft.Graph.Authentication` 2.38.0 and `Az.Accounts` 5.5.0 with Managed, ModuleFast, and PSResourceGet in the speed set. Repeated rotated PowerShell 7 proof passed with `-ManagedMaxRank 1`: Graph.Authentication measured managed first for `InstallNoOp` at 513 ms and `InstallForce` at 675 ms; Az.Accounts measured managed first for `InstallNoOp` at 566 ms and `InstallForce` at 736 ms. A suite smoke with `-UseScenarioGates` also passed and reported strict gate columns for both scenarios. Managed detail rows recorded zero repository requests for both no-op and warm forced install rows. PowerShellGet remains in provider-matrix compatibility runs rather than this strict speed gate because its exact install row can dominate runtime without being a fastest-engine candidate.
- [x] 2026-06-29: Preseed/setup operations now retry transient setup failures outside the measured operation and record `SetupRetryCount` in comparison and suite metadata. This stabilized the Windows PowerShell 5.1 exact Graph/Az lifecycle gate after repeated Az.Accounts setup download failures. The retry-enabled combined Windows PowerShell 5.1 suite passed with scenario gates: Graph.Authentication measured managed first for `InstallNoOp` at 440 ms and `InstallForce` at 947 ms; Az.Accounts measured managed first for `InstallNoOp` at 563 ms and `InstallForce` at 814 ms. Timed rows remain single-attempt; only setup/preseed work is retried.
- [x] 2026-06-29: `LifecycleGate` now includes strict exact-version save no-op/force scenarios for `Microsoft.Graph.Authentication` 2.38.0 and `Az.Accounts` 5.5.0. ModuleFast is included as an explicit skipped row because it has no save command; PSResourceGet participates in `SaveNoOp` and is explicitly skipped for `SaveForce` because no force/reinstall save parameter exists on the inspected hosts. PowerShell 7 measured managed first for Graph.Authentication `SaveNoOp` at 114 ms and `SaveForce` at 122 ms, and Az.Accounts `SaveNoOp` at 129 ms and `SaveForce` at 148 ms. Windows PowerShell 5.1 measured managed first for Graph.Authentication `SaveNoOp` at 140 ms and `SaveForce` at 293 ms. After one Az.Accounts setup run exposed public-gallery transport failures before timing, a targeted Windows PowerShell 5.1 managed-only no-op passed at 125 ms and the full provider-matrix rerun passed with managed first for `SaveNoOp` at 199 ms and `SaveForce` at 376 ms. Managed timed rows recorded zero repository requests.
- [x] 2026-06-29: The named same-source full `Microsoft.Graph` 2.38.0 install `SpeedGate` was rerun with scenario gates, `-RepeatCount 3`, rotated engine order, and both managed and ModuleFast using `https://pwsh.gallery/index.json`. The strict gate passed with managed rank 1: managed median 4447 ms versus ModuleFast median 6075 ms. Managed recorded 78 package-tree rows, 40 unique package outputs, 40 installed outputs, 38 already-satisfied coalesced dependency rows, 41 whole-operation repository requests, 41 package-delivery requests, 186.5 MB downloaded, and no suite gate violations. Read save evidence through its separate provider set: ModuleFast skip rows in save gates mean no equivalent save command, not a managed save loss.
- [x] 2026-06-29: Added a separate `HeavyLifecycleGate` suite for full-family exact install no-op/force proof without making the everyday `LifecycleGate` pull down full Graph/Az trees. The new `Graph.Full.InstallExact.NoOpForce` and `Az.Full.InstallExact.NoOpForce` scenarios use `InstallNoOp` and `InstallForce`, strict managed rank 1 gates, Managed/ModuleFast/PSResourceGet engines, and the same `https://pwsh.gallery/index.json` source for managed and ModuleFast. These scenarios are the next evidence lane before claiming full-family lifecycle dominance.
- [x] 2026-06-29: The first PowerShell 7 `HeavyLifecycleGate` proof for full `Microsoft.Graph` 2.38.0 exact install no-op/force passed with `-RepeatCount 1` and strict scenario gates. Managed ranked first for `InstallNoOp` at 670 ms versus ModuleFast at 840 ms and PSResourceGet at 1813 ms. Managed ranked first for `InstallForce` at 820 ms versus ModuleFast at 897 ms and PSResourceGet at 48661 ms. Both managed timed rows used zero repository requests and zero downloaded bytes; force reused one package-cache hit and treated 39 dependencies as already installed. Keep the repeated Graph run and full Az heavy lane open before claiming full-family lifecycle dominance.
- [x] 2026-06-29: The repeated PowerShell 7 full `Microsoft.Graph` 2.38.0 `HeavyLifecycleGate` passed with `-RepeatCount 3`, rotated engine order, warm cache mode, and strict scenario gates. Managed ranked first for `InstallNoOp` with a 690 ms median versus ModuleFast at 864 ms and PSResourceGet at 1922 ms. Managed ranked first for `InstallForce` with an 826 ms median versus ModuleFast at 847 ms and PSResourceGet at 49003 ms. Managed timed rows used zero repository requests and zero downloaded bytes; force reused one package-cache hit and treated 39 dependencies as already installed. Keep the full Az heavy lane open before claiming full-family lifecycle dominance.
- [x] 2026-06-29: The first PowerShell 7 full `Az` 16.0.0 `HeavyLifecycleGate` passed for managed, but it is compatibility evidence rather than a clean speed race because both competitor engines failed setup. Managed completed `InstallNoOp` at 1529 ms and `InstallForce` at 937 ms with zero timed repository requests and zero downloaded bytes; the force row reused one package-cache hit and treated 102 dependencies as already installed. ModuleFast failed setup three times because `Az.DataTransfer(1.0.0)` was not resolved from `https://pwsh.gallery/index.json`; PSResourceGet also failed setup three times from the same source. Keep this same-source failure visible, and use a separate default-source/provider-specific lane if the next question is native-provider speed rather than managed resolver coverage.
- [x] 2026-06-29: The managed-only repeated PowerShell 7 full `Az` 16.0.0 no-op/force lifecycle proof passed with `-RepeatCount 3`, warm cache mode, and output-root cleanup. Managed `InstallNoOp` median was 831 ms and managed `InstallForce` median was 913 ms. Both rows used zero timed repository requests and zero downloaded bytes; force reused one package-cache hit and treated 102 dependencies as already installed. Treat this as managed lifecycle stability on a preseeded Az root, not as full Az save evidence or cold install throughput.
- [x] 2026-06-29: `HeavySaveGate` now owns named full-family save scenarios for `Microsoft.Graph` and `Az`, keeping broad save throughput separate from install lifecycle behavior. The first PowerShell 7 full `Az` 16.0.0 save comparison measured managed first at 3738 ms, PowerShellGet at 127818 ms, and PSResourceGet at 151123 ms. Managed saved about 623 MB across 2789 files with 204 package rows, 103 unique package outputs, 105 whole-operation repository requests, 103 package-delivery requests, and 137.5 MB downloaded. Detail evidence points the next heavy save optimization question at download/source/cache behavior rather than extraction or promotion.
- [x] 2026-06-29: Save benchmark payload roots now use short disposable temp paths while CSV/JSON artifacts remain in the benchmark run directory. This fixed the Windows PowerShell 5.1 native-provider path failures for full `Microsoft.Graph` 2.38.0 save: managed measured 12863 ms, PSResourceGet 60098 ms, and PowerShellGet 82564 ms, with all three providers succeeding. Managed saved about 1.05 GB across 482 files, recorded 78 package rows, 40 unique package outputs, 80 whole-operation/package-delivery requests, and 186.5 MB downloaded.
- [x] 2026-06-29: Windows PowerShell 5.1 full `Az` 16.0.0 save after the short-root fix measured managed first at 31864 ms and PowerShellGet at 130163 ms; PSResourceGet still failed after 84214 ms in its own temp extraction path for `Az.MachineLearningServices`. Managed saved about 623 MB across 2789 files, recorded 204 package rows, 103 unique package outputs, 210 whole-operation repository requests, 206 package-delivery requests, and 137.5 MB downloaded. Detail evidence still points the next heavy save optimization question at download/source/cache behavior rather than extraction or promotion.
- [x] 2026-06-29: Warm managed package caches now use a run-scoped short temp root shared across repeated save/install/update rows and are removed after artifact capture when `-RemoveOutputRoots` is used. A PowerShell 7 repeated warm managed `ThreadJob` 2.1.0 save proof showed iteration 1 downloading 82971 bytes with zero cache hits, then iteration 2 using two cache hits, zero downloaded bytes, and no package-delivery requests.
- [x] 2026-06-29: `HeavySaveCacheGate` now has managed-only full Graph/Az warm-cache save scenarios with scenario-owned `CacheMode=Warm` and `RepeatCount=2`. Suite summary and optimization-target artifacts now expose first/last managed timing, request, download, and cache-hit fields. The first PowerShell 7 full `Microsoft.Graph` 2.38.0 warm-cache run measured iteration 1 at 3645 ms with 40 package requests, 186.5 MB downloaded, and zero cache hits; iteration 2 measured 1944 ms with zero repository/package requests, zero downloaded bytes, and 40 cache hits.
- [x] 2026-06-29: The PowerShell 7 full `Az` 16.0.0 `HeavySaveCacheGate` run measured iteration 1 at 4136 ms with 107 repository requests, 103 package requests, 137.5 MB downloaded, and zero cache hits. Iteration 2 measured 3587 ms with 4 metadata requests, zero package requests, zero downloaded bytes, and 103 cache hits while still writing the same 2789-file, 623 MB output tree. This confirms package-cache reuse but shows the next full Az save optimization has to focus on materialization/extraction/output work, not just package transfer.
- [x] 2026-06-29: Managed delivery now keeps an expanded package cache under caller-supplied package caches, keyed by the verified package SHA256 and guarded by a short path layout for Windows PowerShell 5.1. The first save into a warm-cache scenario seeds the expanded payload cache; later saves can materialize the same package from the expanded cache while still verifying the package hash, trust policy, license requirement, clobber checks, receipts, and rollback staging. PowerShell 7 full `Microsoft.Graph` 2.38.0 warm save now measured iteration 2 at 1475 ms with 40 package-cache hits and 40 extraction-cache hits, down from the prior 1944 ms second iteration. PowerShell 7 full `Az` 16.0.0 warm save now measured iteration 2 at 2744 ms with 103 package-cache hits and 103 extraction-cache hits, down from the prior 3587 ms second iteration. Windows PowerShell 5.1 full `Az` 16.0.0 warm save measured iteration 1 at 30668 ms and iteration 2 at 6744 ms with 103 package-cache hits and 103 extraction-cache hits.
- [x] 2026-06-29: The next full Az speed proof should use the provider-matrix lane, not the same-source heavy lifecycle gate. Same-source `Az.Full.InstallExact.NoOpForce` remains resolver/source compatibility evidence because ModuleFast and PSResourceGet failed setup from `https://pwsh.gallery/index.json` for `Az.DataTransfer`. Provider-matrix scenarios now mark ModuleFast source as `ProviderDefault`, the suite runner omits `-ModuleFastSource`, and the child harness defaults that source to empty so ModuleFast `-Source` is not accidentally reintroduced.
- [x] 2026-06-29: The corrected provider-default harness path was smoke-tested with PowerShell 7 `Az.Accounts.ProviderMatrix`. All four engines succeeded with ModuleFast source shown as `ProviderDefault`: managed measured 1509 ms, ModuleFast 2098 ms, PSResourceGet 2293 ms, and PowerShellGet 9070 ms. This proves the provider-matrix lane can execute without an explicit ModuleFast source.
- [x] 2026-06-29: The corrected full PowerShell 7 `Graph.Full.ProviderMatrix` install proof measured managed first at 5080 ms, ModuleFast at 7133 ms, PSResourceGet at 53379 ms, and PowerShellGet at 67082 ms. All four engines succeeded under provider-default execution, so this is the clean full-Graph default-provider scoreboard after the ModuleFast source-default fix.
- [x] 2026-06-29: The corrected full PowerShell 7 `Az.Full.ProviderMatrix` install proof measured managed first at 6928 ms while PSResourceGet and PowerShellGet completed at 149062 ms and 140901 ms. ModuleFast still failed under provider-default execution because its own default path could not resolve `Az.DataTransfer(1.0.0)` from `https://pwsh.gallery/index.json`. Keep this as compatibility/source evidence: managed can install full Az quickly and the native providers complete slowly, but the current ModuleFast full-Az row is not a successful speed comparison.
- [x] 2026-06-29: `RepairGate` now promotes repair planning into named suite scenarios for stale versions, source drift, scope drift, family coherence, loaded-module safety, and cleanup planning. The focused loaded-module safety and cleanup planning proof passed with strict managed-rank gates on PowerShell 7 and Windows PowerShell 5.1: loaded-module safety planned in 678 ms on PowerShell 7 and 712 ms on Windows PowerShell 5.1 with one action and three findings; cleanup planning planned in 660 ms and 663 ms with two actions and two findings. ModuleFast, PSResourceGet, and PowerShellGet remain explicit skips because they do not expose equivalent module-estate repair planning.
- [x] 2026-06-29: The benchmark README now separates install scoreboards, save scoreboards, and managed-only save cache diagnosis so full-family save evidence is not confused with install lifecycle rows. Install speed claims include ModuleFast where it has an equivalent command; save speed claims compare against `Save-Module` and `Save-PSResource`, while `HeavySaveCacheGate` remains a managed-only materialization microscope.
- [x] 2026-06-29: Suite scenario lists, summaries, host comparisons, metadata, and optimization-target artifacts now emit `BenchmarkRole` and `ComparisonScope`. This keeps provider scoreboards such as `InstallSameSource` and `SaveCapableProviders` visibly separate from diagnostics such as `ManagedOnlySaveCache`, so save-cache optimization rows are not mistaken for failed save races.
- [x] 2026-06-29: ModuleState inventory now carries manifest-declared exported command names, and repair planning reports cross-scope command conflicts as warnings without changing normal managed install clobber behavior. The contract is repair-only: ordinary plan analysis stays selected-root focused, while estate repair can surface that two installed modules in different scopes export the same command.

### Next Optimization Targets

- [x] Design safe parallel dependency delivery for independent direct dependencies while preserving cycle detection, per-module install locks, source/trust/license policy, and deterministic failure reporting.
- [x] Coordinate repository metadata caches and package-cache writes so parallel dependency delivery cannot corrupt shared cached packages.
- [x] Re-measure full `Az` and full `Microsoft.Graph` install after dependency scheduling changes on PowerShell 7 and Windows PowerShell 5.1.
- [x] Add repository lookup/request coalescing for repeated transitive dependency checks; current package-cache coordination prevents file races and anonymous remote metadata reads now share identical in-flight requests.
- [x] Stabilize Windows PowerShell 5.1 stale small-module update evidence with repeated rotated cold/warm runs.
- [x] Stabilize PowerShell 7 small-module save evidence with repeated rotated import-gated runs.
- [x] Measure heavy Graph/Az/Teams/Exchange managed update scenarios on PowerShell 7 and Windows PowerShell 5.1, then optimize the slowest proven managed lane.
- [x] Add an initial repair-plan benchmark lane for stale versions.
- [x] Add a repair-plan benchmark lane for source drift.
- [x] Add a repair-plan benchmark lane for scope drift.
- [x] Add a repair-plan benchmark lane for family coherence.
- [x] Add repair benchmark lanes for loaded-module safety and cleanup planning before optimizing repair-specific behavior.
- [x] Add explicit heavy update baseline versions or a baseline-discovery mode to the suite runner so Graph/Az/Teams/Exchange update scenarios do not silently skip when no `-UpdateBaselineVersion` is supplied.
- [x] Add force/no-op benchmark gates for install, save, and update before claiming PowerShellGet/PSResourceGet compatibility parity.
- [x] Optimize warm forced update when the selected version is already present and the only dependency is already satisfied; current smoke evidence shows managed `UpdateForce` fastest on PowerShell 7 and Windows PowerShell 5.1.
- [x] Optimize warm forced install when the selected version is already present and the package has an unbounded already-installed dependency; current smoke evidence shows managed `InstallForce` fastest on PowerShell 7 and Windows PowerShell 5.1.
- [x] Optimize unbounded and bounded install no-op so an already installed satisfying version skips remote latest/range resolution before returning `AlreadyInstalled`.
- [x] Stabilize repeated force/no-op benchmark gates with `-RepeatCount 3` after the next optimization pass so one-shot child-host variance does not hide regressions.
- [x] Promote strict exact-version Graph.Authentication and Az.Accounts install no-op/force gates into the benchmark suite so heavy core-package lifecycle regressions are caught without relying on remote latest metadata during the timed operation.
- [x] Promote strict full-family Graph/Az install no-op/force gates into a separate heavy benchmark suite so broad lifecycle claims can be proven without bloating the routine lifecycle gate.
- [x] Run the repeated full `Microsoft.Graph` no-op/force heavy lifecycle gate on PowerShell 7 and record managed-rank evidence.
- [x] Run the first full `Az` no-op/force heavy lifecycle gate on PowerShell 7 and record managed success plus competitor setup failures.
- [x] Run managed-only repeated full Az no-op/force stability on PowerShell 7 after same-source competitors failed setup.
- [x] Decide whether the next full Az proof should be a default-source provider comparison or a same-source rerun after provider/source behavior changes.
- [x] Close the remaining PowerShell 7 `Microsoft.Graph.Authentication` save gap in repeated rotated local evidence.
- [x] Promote repeated `Microsoft.Graph.Authentication` save comparisons into the benchmark suite as a stability gate for both PowerShell 7 and Windows PowerShell 5.1.
- [x] Tighten `SaveGate` from the practical 1.05 ratio gate toward strict rank once public-gallery variance is separated from managed engine behavior.
- [x] Promote exact-version Graph.Authentication and Az.Accounts save no-op/force gates into the benchmark suite so heavy core-package save semantics are measured beyond the small `ThreadJob` lane.
- [x] Run full-family save gates on PowerShell 7 and Windows PowerShell 5.1 for Graph/Az after separating save payload roots from artifact roots.
- [x] Prove warm managed package cache reuse survives repeated save rows and output-root cleanup.
- [x] Add named package-source/cache experiments for heavy save rows now that full Graph/Az evidence points at download/source/cache behavior.
- [x] Run the named full Graph heavy save cache experiment and record how warm package reuse changes package requests, downloaded bytes, and cache hits.
- [x] Run the named full Az heavy save cache experiment and record whether the larger module tree shows the same cache/materialization split.
- [x] Use the heavy save cache evidence to optimize materialization/extraction/output work for full Az without weakening package integrity, receipts, or rollback behavior.
- [x] Run the full `Az.Full.ProviderMatrix` install proof on PowerShell 7 with ModuleFast provider-default source and record whether the same-source `Az.DataTransfer` failure is avoided.

### Compatibility Semantics Gates

- [x] Define and test exact `-Force` behavior for install/save/update exact-version reinstall, update reinstall, no-op, downgrade blocking, and no implied cleanup.
- [x] Define and test exact `-Force` behavior for repair, receipt repair, cleanup planning, and explicit downgrade policy.
- [x] Define and test exact `-AllowClobber` behavior versus PSResourceGet `-NoClobber` for manifest-declared command conflicts in the selected target root.
- [x] Define repair-time cross-scope command-conflict behavior so estate maintenance can report conflicts beyond one install target root without surprising normal installs.
- [x] Define and test exact `-AcceptLicense` behavior for root modules, dependencies, and unattended managed delivery.
- [x] Define repair-plan and no-name update license reporting so estate maintenance can show which planned packages require `-AcceptLicense` before mutation.
  - [x] 2026-06-29: Managed repository version metadata now carries known license and license-acceptance flags from local packages, NuGet v2, and the PowerShell Gallery v2 read path. `Install-ManagedModule -Plan` and `Update-ManagedModule -Plan` expose `LicenseAcceptanceRequired`, `LicenseAccepted`, and license text when the selected package metadata is available, including `Update-ManagedModule` without `-Name` across an installed module root. `Repair-ManagedModule` enriches managed repair actions best-effort before apply preparation, shows license state in the plan summary, and blocks managed apply before mutation when a planned package is known to require license acceptance and `-AcceptLicense` was not supplied. Existing apply-time package metadata checks still remain the final guard for repositories that cannot expose license metadata before download.
- [ ] Decide whether public `-Proxy` and `-ProxyCredential` parameters are required on managed cmdlets or remain repository/profile policy.
- [x] Add initial Windows managed `-AuthenticodeCheck` support for install/save/update signable files before promotion.
- [ ] Complete Authenticode/catalog parity with PSResourceGet, including catalog-specific evidence, timestamped signatures, short-lived certificate chains, and signed live fixtures.
- [ ] Decide whether to expose migration aliases for `-TrustRepository`, `-SkipPublisherCheck`, `-AllowPrerelease`, and `-RequiredVersion` where the managed canonical parameter differs.
- [ ] Document non-module PSResourceGet resource-kind gaps so scripts, command search, DSC/resource-kind search, and provider bootstrap behavior do not look silently supported.

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

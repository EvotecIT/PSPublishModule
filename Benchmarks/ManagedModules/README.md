# Managed Module Benchmarks

`Compare-ManagedModuleEngines.ps1` is the PowerShell/user-workflow benchmark harness for managed module lifecycle work. It compares PSPublishModule's managed cmdlets against available compatibility tools, writes direct-run evidence under `Ignore\Benchmarks\ManagedModules\Run-*`, writes suite evidence under `Ignore\Benchmarks\MM\S*`, and keeps benchmark commands out of the shipped module surface.

The production module should expose module-management cmdlets. Benchmarking stays here as contributor and CI evidence, similar to other Evotec benchmark folders.

## Common Runs

Fast sanity run:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -Suite Smoke
```

List benchmark suite scenarios:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite All -ListScenarios
```

Run the smoke suite on the current host:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite Smoke
```

Run Graph and Az save/find comparisons on PowerShell 7 and Windows PowerShell:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite Graph,Az -HostName PowerShell7,WindowsPowerShell -Operation Find,Save
```

Run one heavy scenario without paying for the whole suite:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite Graph -ScenarioName Graph.Full -HostName PowerShell7 -Operation Find,Save
```

Use repeated, rotated engine order for fairer timing:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite Graph -ScenarioName Graph.Full -HostName PowerShell7 -Operation Find -RepeatCount 3 -RotateEngineOrder
```

### Install Scoreboards

Run the named same-source Graph install speed gate against ModuleFast:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite SpeedGate -ScenarioName Graph.Full.SameSource -HostName PowerShell7 -RepeatCount 3 -RotateEngineOrder -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Run the Graph install provider matrix with ModuleFast, PSResourceGet, and PowerShellGet in the comparison. This is the best install scoreboard for seeing whether managed install is beating the common provider stack, while the same-source row stays the strict Managed-vs-ModuleFast gate:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite SpeedGate -ScenarioName Graph.Full.ProviderMatrix -HostName PowerShell7 -RepeatCount 1 -RotateEngineOrder -RemoveOutputRoots -SkipBuild
```

Run the Az provider matrix. `Az.Accounts.ProviderMatrix` is the shorter proof lane; `Az.Full.ProviderMatrix` is the heavyweight row for the full Az module family. Provider-matrix scenarios let ModuleFast use its provider default source instead of forcing the same-source `pwsh.gallery` URL:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite SpeedGate -ScenarioName Az.Accounts.ProviderMatrix -HostName PowerShell7 -RepeatCount 1 -RotateEngineOrder -RemoveOutputRoots -SkipBuild
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite SpeedGate -ScenarioName Az.Full.ProviderMatrix -HostName PowerShell7 -RepeatCount 1 -RotateEngineOrder -RemoveOutputRoots -SkipBuild
```

### Save Scoreboards

Run the named Graph.Authentication save stability gate across PowerShell 7 and Windows PowerShell 5.1. The scenario-owned gate is strict: managed must rank first on each host.

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite SaveGate -HostName PowerShell7,WindowsPowerShell -RepeatCount 3 -RotateEngineOrder -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Run the lifecycle evidence lane across PowerShell 7 and Windows PowerShell 5.1. This lane covers no-op and force semantics for install and save, keeps ModuleFast in the install comparison, and uses compact suite paths so Windows PowerShell 5.1 does not trip over legacy path-length limits. Use the warm-cache ratio gate for a practical maintenance-style check; strict one-shot rank gates can be too sensitive to child-host startup variance on no-op rows:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite LifecycleGate -HostName PowerShell7,WindowsPowerShell -RepeatCount 1 -RotateEngineOrder -CacheMode Warm -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Run the exact-version Graph and Az install lifecycle gates when optimizing force/no-op install behavior beyond small modules. These scenarios compare Managed with ModuleFast and PSResourceGet under a strict managed-rank gate. PowerShellGet remains available in provider-matrix compatibility runs, but it is not included in this speed gate because its install path can dominate runtime without being the fastest competitor. Setup retries apply only to the preseed step outside the measured operation:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite LifecycleGate -ScenarioName Graph.Authentication.InstallExact.NoOpForce,Az.Accounts.InstallExact.NoOpForce -HostName PowerShell7 -RepeatCount 3 -RotateEngineOrder -CacheMode Warm -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Run the exact-version Graph and Az save lifecycle gates when optimizing no-op and force save behavior. This is not a ModuleFast speed lane: ModuleFast is included only as an explicit skipped row because it has no equivalent save command. PSResourceGet participates in `SaveNoOp` and is skipped for `SaveForce` because it has no force/reinstall save parameter on the inspected hosts:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite LifecycleGate -ScenarioName Graph.Authentication.SaveExact.NoOpForce,Az.Accounts.SaveExact.NoOpForce -HostName PowerShell7,WindowsPowerShell -RepeatCount 3 -RotateEngineOrder -CacheMode Warm -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Run the heavy full-family no-op/force install gates only when validating a broad install lifecycle claim. These scenarios intentionally live in `HeavyLifecycleGate` instead of the everyday `LifecycleGate` because setup/preseed work downloads large Graph and Az module trees outside the timed rows:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite HeavyLifecycleGate -ScenarioName Graph.Full.InstallExact.NoOpForce,Az.Full.InstallExact.NoOpForce -HostName PowerShell7 -RepeatCount 3 -RotateEngineOrder -CacheMode Warm -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Run the heavy full-family save gates when validating broad save throughput separately from install lifecycle behavior. These rows are the official full-family save scoreboard: managed is compared against save-capable providers only. ModuleFast is intentionally not included because it does not expose an equivalent save command:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite HeavySaveGate -ScenarioName Graph.Full.Save -HostName PowerShell7 -RepeatCount 1 -RotateEngineOrder -UseScenarioGates -RemoveOutputRoots -SkipBuild
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite HeavySaveGate -ScenarioName Az.Full.Save -HostName PowerShell7 -RepeatCount 1 -RotateEngineOrder -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

### Managed Save Cache Diagnosis

Run the managed-only heavy save cache lane when separating package download/source cost from repeated save materialization. This is not a provider scoreboard. These scenarios default to `CacheMode=Warm` and `RepeatCount=2`; command-line `-CacheMode` or `-RepeatCount` still override those defaults for experiments. Read `ManagedCacheHits` as package-cache reuse and `ManagedExtractionCacheHits` as expanded-payload reuse:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite HeavySaveCacheGate -ScenarioName Graph.Full.Save.ManagedWarmCache -HostName PowerShell7 -RemoveOutputRoots -SkipBuild
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite HeavySaveCacheGate -ScenarioName Az.Full.Save.ManagedWarmCache -HostName PowerShell7 -RemoveOutputRoots -SkipBuild
```

Do not read `HeavySaveCacheGate` as managed losing a save race. It is a managed-only microscope for finding remaining cost after downloads disappear. Full-family save still has to materialize the module tree into a fresh destination, while install no-op/force rows often do little or no disk work because the selected modules are already present. The official save scoreboard is `HeavySaveGate`, where managed is compared with save-capable providers and currently ranks first in the recorded Graph and Az full-family evidence. ModuleFast is install-only in this benchmark suite, so install speed claims include it, while save speed claims compare against `Save-Module` and `Save-PSResource`.

### Security And Signature Checks

Run the Authenticode compatibility gate when validating certificate/signature behavior for install and save. The current lane compares managed delivery with PSResourceGet because both expose an equivalent signature-check switch for these operations. Its scenario-owned gate requires managed to rank first and to report at least one checked Authenticode file, so a fast run that silently stops inspecting signable files fails:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite SecurityGate -HostName PowerShell7,WindowsPowerShell -RepeatCount 1 -RotateEngineOrder -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Run the publish evidence lane against local folder feeds. The fixture module and feed are synthetic and benchmark-owned, repository registration happens outside the timed publish operation, and ModuleFast is kept as an explicit skipped row because it has no publish command:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite PublishGate -HostName PowerShell7,WindowsPowerShell -RepeatCount 1 -RotateEngineOrder -RemoveOutputRoots -SkipBuild
```

Run the repair planning gate when validating estate-maintenance behavior. `RepairGate` covers stale versions, source drift, scope drift, family coherence, loaded-module safety, and cleanup planning; the focused command below is the quick proof for the loaded-module and old-version cleanup risks:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite RepairGate -ScenarioName ThreadJob.Repair.LoadedModuleSafety,ThreadJob.Repair.CleanupPlanning -HostName PowerShell7,WindowsPowerShell -RepeatCount 1 -UseScenarioGates -RemoveOutputRoots -SkipBuild
```

Fail a comparison when managed is not the fastest successful engine:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -Operation SaveNoOp,SaveForce -Engine Managed,PSResourceGet,PowerShellGet -ManagedMaxRank 1 -SkipBuild
```

Fail a suite when managed is more than 25 percent slower than the fastest successful engine:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite Smoke -HostName PowerShell7,WindowsPowerShell -Operation Find,Save -ManagedMaxVsFastest 1.25 -SkipBuild
```

Compare install behavior in disposable module/profile roots:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite Smoke -HostName PowerShell7 -Operation Install
```

Compare already-present install behavior with and without force semantics:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -Operation InstallNoOp,InstallForce -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Compare already-saved output behavior with and without force semantics:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -Operation SaveNoOp,SaveForce -Engine Managed,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Compare update behavior from a known stale version in disposable module/profile roots:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -UpdateBaselineVersion 2.0.3 -Operation Update -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Compare already-current update behavior with and without force semantics:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -Operation UpdateNoOp,UpdateForce -Engine Managed,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Time managed estate repair planning from a known stale version in a disposable root:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -UpdateBaselineVersion 2.0.3 -Operation RepairPlan -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Time managed estate repair planning for a seeded source-drift case:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -Operation RepairPlan -RepairScenario SourceDrift -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Time managed estate repair planning for a seeded scope-drift case:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -Operation RepairPlan -RepairScenario ScopeDrift -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Time managed estate repair planning for a synthetic Graph-family version mismatch:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -Operation RepairPlan -RepairScenario FamilyCoherence -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Time managed estate repair planning when a stale loaded module conflicts with the desired installed version:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -Operation RepairPlan -RepairScenario LoadedModuleSafety -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Time managed estate cleanup planning for old side-by-side versions:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -Operation RepairPlan -RepairScenario CleanupPlanning -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
```

Omit `-UpdateBaselineVersion` to let the harness choose the latest stable version lower than the requested target:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -Operation Update -Engine Managed,PSResourceGet,PowerShellGet -ValidateImport -RepeatCount 1 -SkipBuild
```

Add import validation after install/update without adding import time to the operation timing:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -UpdateBaselineVersion 2.0.3 -Operation Install,Update -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -ValidateImport -RepeatCount 1 -SkipBuild
```

Control cache behavior for update benchmarks:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -UpdateBaselineVersion 2.0.3 -Operation Update -Engine Managed,PSResourceGet -ValidateImport -CacheMode Cold -RepeatCount 1 -SkipBuild
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -UpdateBaselineVersion 2.0.3 -Operation Update -Engine Managed,PSResourceGet -ValidateImport -CacheMode Warm -RepeatCount 1 -SkipBuild
```

Compare the managed installer against the install-only speed gate. Use `-RemoveOutputRoots` for repeated heavy runs when the CSV/JSON summaries and managed detail artifacts are enough evidence and the expanded module output does not need manual inspection:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName Microsoft.Graph -Operation Install -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -AcceptLicense -RepeatCount 1 -RemoveOutputRoots -SkipBuild
```

Compare selected engines:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -Suite Smoke -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet
```

Use a specific module and exact version:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0
```

Compare a package that requires license acceptance:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName Microsoft.Graph.Authentication -Version '' -Operation Find,Save -AcceptLicense
```

Compare Authenticode validation overhead for engines that expose an equivalent switch:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName Company.SignedTools -Operation Save,Install -Engine Managed,PSResourceGet -AuthenticodeCheck -RepeatCount 3 -RotateEngineOrder -SkipBuild
```

List emitted operation rows without running:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ListScenarios
```

## Output Files

Every run writes:

- `managed-module-results.csv`: raw per-run rows, including repair scenario, skipped/failed reason, and optional import validation columns.
- `managed-module-results.json`: raw per-run rows as JSON, including the same skipped/failed reason text as the CSV.
- `managed-module-summary.csv`: grouped median/min/max rows by operation, scenario, and engine, including output size and managed detail medians when detail artifacts are present.
- `managed-module-comparison.csv`: fastest successful engine per operation and scenario, plus managed rank, ratio, and managed package/request/download/extract/promotion medians. `ManagedMs` is the wall-clock benchmark time. `ManagedRootElapsedMs` is the managed command result elapsed time from the detail artifact, and `ManagedHarnessOverheadMs` is the wall-clock remainder from child host startup, module import, wrapper work, and output processing. `ManagedPackageCount` is the package tree row count, so shared dependencies can appear more than once. `ManagedUniquePackageCount` counts unique module output paths from the detail artifact. `ManagedInstalledPackageCount` and `ManagedAlreadyInstalledPackageCount` count package tree rows by status, which makes coalesced shared dependencies visible. `ManagedRepositoryRequests` is the whole managed operation request total; `ManagedPackageRepositoryRequests` is the package-delivery subset and excludes dependency version-resolution requests. `ManagedPackageRepositoryRedirects` counts HTTP redirects followed during package delivery, which is useful when comparing repository/CDN source behavior. `ManagedMaintenanceActions` and `ManagedMaintenanceFindings` are repair-plan metrics; package metrics are expected to be zero for repair-plan rows because no packages are downloaded during planning.
- `managed-module-gate.csv`: optional managed performance gate violations, written when `-ManagedMaxRank` or `-ManagedMaxVsFastest` is supplied.
- `metadata.json`: runtime, selected engines, module, version, repository, output paths, and output-root cleanup status.
- `managed-<Operation>-details-<n>.json`: managed package tree details, written under the benchmark run folder for install, save, and update rows that return managed delivery results. It includes package/dependency tree counts, unique package/status counts, per-package elapsed/download/extraction/promotion timings, request counts, cache hits, and byte counts.

Start with `managed-module-comparison.csv` for a quick scoreboard, then inspect `managed-module-results.csv` when a competitor is skipped or failed.

Benchmark CSV and JSON artifacts are written through same-directory temporary files and replaced only after the write succeeds. If a long provider run is interrupted, stale completed artifacts remain intact and incomplete temporary files are cleaned on the next successful writer path instead of leaving parseable-looking corrupt output.

Suite runs write `suite-summary.csv`, `suite-summary.json`, `suite-host-comparison.csv`, `suite-host-comparison.json`, `suite-optimization-targets.csv`, `suite-optimization-targets.json`, `suite-hosts.csv`, optional `suite-gate.csv`, and per-scenario child run folders. Suite summaries carry the managed detail medians from each child comparison row so heavy Graph/Az/Teams/Exchange runs can be inspected from one file before drilling into package detail artifacts. `ManagedAuthenticodeCheckedFiles` is also promoted into direct comparison and suite summary rows so signature-check gates show whether managed actually inspected signable files, and `GateManagedMinAuthenticodeCheckedFiles` records the active minimum when a security scenario requires that proof. `BenchmarkRole` separates real provider scoreboards from managed-only diagnostic rows, and `ComparisonScope` names the comparable engine set, such as `InstallSameSource`, `SaveCapableProviders`, or `ManagedOnlySaveCache`. `BenchmarkInterpretation` carries the plain-language reading for the row into scenario lists, suite summaries, host comparisons, optimization targets, metadata, and gate failures so managed-only cache/materialization diagnostics are not mistaken for provider races. Suite summary gate columns show the effective gate for the row: explicit command-line thresholds first, scenario-owned thresholds when `-UseScenarioGates` is supplied, or zero when no gate was active. `suite-host-comparison.csv` joins matching managed rows from PowerShell 7 and Windows PowerShell 5.1 so host deltas are visible without manually combining CSV files. `suite-optimization-targets.csv` ranks each managed row by the largest measured phase and preserves request, redirect, cache, package, and byte context so the next optimization pass starts from evidence. `BottleneckShareRaw` keeps the mathematical phase-to-wall ratio; `BottleneckShare` is shown as `>100%` with a timing note when summed package/dependency phase timings overlap because parallel work exceeds wall-clock time. Prefer the unique package columns for optimization planning; use the tree package columns when investigating repeated dependency traversal.

## Notes

`Find` and `Save` are safe to compare directly because each compatible engine can run against isolated output folders. ModuleFast is included as an install-only competitor; its find/save/update/repair/publish rows are explicit skips when it does not expose an equivalent command. `SaveNoOp`, `InstallNoOp`, and `UpdateNoOp` pre-seed the selected version outside the timed window, then time the same operation without force/reinstall intent against the already-present target. `SaveForce`, `InstallForce`, and `UpdateForce` pre-seed the selected version outside the timed window, then time the operation with the engine's force/reinstall equivalent when one exists. Preseed/setup operations retry transient setup failures according to `-SetupRetryCount`; timed operations are not retried. `Save-PSResource` currently has no force/reinstall save parameter in the inspected hosts, so `SaveForce` records an explicit skip for PSResourceGet rather than faking a comparison. `Install` runs each engine in a disposable child PowerShell host with benchmark-owned profile, cache, temp, and module path environment variables. `Update` first installs `-UpdateBaselineVersion` outside the timed window, then times only the update operation in the same disposable host root. `Publish` creates a lightweight synthetic module and local folder feed outside the timed operation, registers native repositories outside the timed window, then times only the publish command. `RepairPlan -RepairScenario StaleVersion` uses the same stale baseline setup, then times the managed `Repair-ManagedModule -Plan -Latest` planning path without executing changes. `RepairPlan -RepairScenario SourceDrift` installs the target version outside the timed window, stamps disposable module metadata with a different source, writes a maintenance receipt expecting the requested repository, and times repair planning without applying changes. `RepairPlan -RepairScenario ScopeDrift` installs the target version outside the timed window, writes a maintenance receipt expecting `CurrentUser`, and times repair planning without applying changes. `RepairPlan -RepairScenario FamilyCoherence` seeds lightweight synthetic `Microsoft.Graph.Authentication` and `Microsoft.Graph.Users` module manifests with mixed versions, passes the built-in Graph family policy, and times repair planning without applying changes. `RepairPlan -RepairScenario LoadedModuleSafety` seeds side-by-side synthetic versions, imports the stale copy in the child host, and times `Repair-ManagedModule -Plan -IncludeLoaded -Version 2.0.0` so loaded-version findings stay visible without mutating the root. `RepairPlan -RepairScenario CleanupPlanning` seeds side-by-side synthetic versions and times `Repair-ManagedModule -Plan -Cleanup OldVersions` so old-version removal is measured as planning rather than deletion. Use `-RepairScenario All` to emit every repair-plan scenario row. Baseline version discovery runs only for `Update` and `RepairPlan -RepairScenario StaleVersion`; source, scope, family, loaded-module, and cleanup synthetic repair scenarios avoid that repository setup cost. `-CacheMode Cold` clears disposable provider/package caches after pre-seeded install/update phases; `-CacheMode Warm` preserves native provider caches and gives managed install, save, and update delivery a run-scoped package cache folder under the short benchmark temp root; `Default` preserves the historical harness behavior. `-ValidateImport` imports the highest-version manifest from the benchmark output root after timing and writes `ImportStatus`, `ImportVersion`, `ImportMilliseconds`, `ImportManifestPath`, and `ImportError` columns. Use `-ImportTimeoutSeconds` to cap that post-timing import proof for very large module families. `-RemoveOutputRoots` deletes benchmark-owned expanded module roots and warm managed package-cache roots after raw results, summary, comparison, gate, metadata, and managed detail artifacts are written; CSV/JSON rows keep the original output paths for traceability, but those paths are intentionally gone. ModuleFast setup/import is outside the timed operation, uses the locally installed `ModuleFast` module, and is skipped on Windows PowerShell 5.1 because ModuleFast requires PowerShell 7.2 or newer. Install, save, update, and publish output roots are recorded in the CSV/JSON rows and may point at the short temp benchmark folder to keep Windows PowerShell 5.1 below legacy path-length limits.

Performance gates are optional. `-ManagedMaxRank` fails when a successful managed row ranks worse than the requested rank; `-ManagedMaxVsFastest` fails when managed is slower than the fastest successful engine by more than the requested ratio; `-ManagedMinAuthenticodeCheckedFiles` fails when managed reports fewer checked signable files than required. Named gate scenarios can also carry their own threshold; use `-UseScenarioGates` to apply those scenario-owned defaults. Explicit command-line gate parameters override scenario defaults for experiments. Gate failures are reported after CSV/JSON artifacts are written, so failed runs still leave evidence for diagnosis. Skipped competitor rows do not count as faster engines.

License acceptance is explicit. Use `-AcceptLicense` only when the benchmark scenario is allowed to accept the package license on behalf of the run.

Authenticode validation is explicit. Use `-AuthenticodeCheck` only when the benchmark is meant to measure signature verification overhead; unsigned packages are expected to fail that safety gate.

The suite runner enables license acceptance only for scenarios that require it, such as full Graph and Az package family scenarios. Keep that behavior explicit when adding new scenarios.

Use `-RotateEngineOrder` with `-RepeatCount 3` or higher before making performance claims. Repository and host caches can otherwise make the second or third engine look faster than it really is.

When comparing managed install with ModuleFast, track both the provider-default lane and a same-source lane. Provider-matrix scenarios set `ModuleFastSource` to `ProviderDefault`, which makes the harness omit ModuleFast `-Source` and lets ModuleFast use its own configured/default source behavior. A same-source gate removes repository-backend variance by passing the same NuGet v3 source to both:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite SpeedGate -ScenarioName Graph.Full.SameSource -HostName PowerShell7 -RepeatCount 3 -RotateEngineOrder -ManagedMaxRank 1 -RemoveOutputRoots -SkipBuild
```

## Current Evidence

Measured on 2026-06-28 with ModuleFast 0.6.1 installed in the current user's PowerShell 7 module path:

- PowerShell 7, `ThreadJob` 2.1.0 install, 3 rotated runs: ModuleFast median 2310.31 ms, Managed median 2769.30 ms, PSResourceGet median 4840.46 ms, PowerShellGet median 10361.05 ms.
- PowerShell 7, `ThreadJob` 2.1.0 install with `-ValidateImport`, 1 run after the installed-dependency optimization: ModuleFast measured 2487.46 ms, Managed measured 3222.92 ms, PSResourceGet measured 4483.68 ms, and PowerShellGet measured 13303.81 ms; every engine imported version 2.1.0 successfully.
- Windows PowerShell 5.1, `ThreadJob` 2.1.0 install, 1 run: Managed 4410.96 ms, PSResourceGet 5215.20 ms, PowerShellGet 76334.35 ms, ModuleFast skipped because it requires PowerShell 7.2 or newer.
- PowerShell 7, `ThreadJob` update from 2.0.3 to 2.1.0, 1 run: Managed 2855.15 ms, PSResourceGet 4479.43 ms, PowerShellGet 11237.69 ms, ModuleFast skipped because it has no update command.
- Windows PowerShell 5.1, `ThreadJob` update from 2.0.3 to 2.1.0, 1 run: Managed 4457.15 ms, PSResourceGet 7329.25 ms, ModuleFast skipped, and PowerShellGet failed in its own metadata conversion path while leaving version 2.0.3 installed.
- PowerShell 7, `ThreadJob` install/update from 2.0.3 to 2.1.0 with `-ValidateImport`, 1 run: every successful output imported version 2.1.0 from its benchmark output root; install measured ModuleFast 3367.05 ms, Managed 3539.46 ms, PSResourceGet 3947.53 ms, and PowerShellGet 12186.28 ms; update measured Managed 3982.42 ms, PSResourceGet 5168.85 ms, and PowerShellGet 14235.76 ms.
- Windows PowerShell 5.1, `ThreadJob` install/update from 2.0.3 to 2.1.0 with `-ValidateImport`, 1 run: every successful output imported version 2.1.0 from its benchmark output root; install measured PSResourceGet 5566.97 ms, Managed 5788.02 ms, and PowerShellGet 20201.12 ms; update measured PSResourceGet 6973.94 ms and Managed 9910.50 ms, while PowerShellGet failed in its own update path and left version 2.0.3 installed.
- PowerShell 7, `ThreadJob` save with `-ValidateImport`, 1 run: every successful output imported version 2.1.0 from its benchmark output root; PSResourceGet measured 2407.57 ms, Managed 2734.35 ms, and PowerShellGet 23304.06 ms.
- Windows PowerShell 5.1, `ThreadJob` save with `-ValidateImport`, 1 run: every successful output imported version 2.1.0 from its benchmark output root; Managed measured 2200.40 ms, PSResourceGet 2515.05 ms, and PowerShellGet 14615.81 ms.
- Windows PowerShell 5.1, `ThreadJob` update from 2.0.3 to 2.1.0 with `-ValidateImport -CacheMode Cold`, 1 run: managed measured 7978.12 ms and PSResourceGet measured 7519.21 ms; both imported version 2.1.0 from the output root. With `-CacheMode Warm`, managed measured 6861.28 ms and PSResourceGet measured 7448.84 ms; the managed detail artifact still reported no package cache hits for this specific target/dependency combination.
- PowerShell 7, `ThreadJob` update from 2.0.3 to 2.1.0 with `-ValidateImport -CacheMode Cold`, 1 run: managed measured 4516.84 ms and PSResourceGet measured 4022.21 ms. With `-CacheMode Warm`, managed measured 4087.70 ms and PSResourceGet measured 3818.19 ms. Treat these small-module update numbers as close-run smoke evidence, not a stable optimization claim without repeated rotated runs.
- After adding a conservative installed-dependency range satisfaction check, `ThreadJob` warm update with `-ValidateImport` was rerun on 2026-06-28. PowerShell 7 measured managed at 3448.98 ms, PSResourceGet at 4213.36 ms, and PowerShellGet at 20898.24 ms; Windows PowerShell 5.1 measured managed at 4460.59 ms, PSResourceGet at 5752.37 ms, and PowerShellGet failed its own update path. ModuleFast remains an explicit update skip because it has no matching update command. The managed detail artifacts still installed `Microsoft.PowerShell.ThreadJob` 2.2.0, so this scenario proves update timing and import correctness but does not exercise the installed-dependency skip path.
- PowerShell 7 and Windows PowerShell 5.1 repeated rotated `ThreadJob` update with `-ValidateImport`, `-RepeatCount 3`, and cold/warm cache modes after the installed-dependency optimization. Managed ranked first by median in all four lanes: PS7 cold managed 3983.02 ms versus PSResourceGet 6011.18 ms and PowerShellGet 15367.92 ms; PS7 warm managed 3511.24 ms versus PSResourceGet 5071.39 ms and PowerShellGet 14112.66 ms; PS5.1 cold managed 6085.15 ms versus PSResourceGet 7880.35 ms while PowerShellGet failed all runs; PS5.1 warm managed 5634.27 ms versus PSResourceGet 7086.47 ms while PowerShellGet failed all runs. ModuleFast remained an explicit update skip.
- PowerShell 7 repeated rotated `ThreadJob` save with `-ValidateImport -RepeatCount 3`: managed median 1929.02 ms, PSResourceGet median 2421.80 ms, PowerShellGet median 3145.99 ms, and ModuleFast explicitly skipped save. Every successful output imported version 2.1.0.
- Repeated rotated warm `ThreadJob` force/no-op gates with `-ManagedMaxRank 1` passed on both hosts after the unbounded dependency satisfaction optimization. PowerShell 7 measured managed fastest for `InstallNoOp` at 552.36 ms, `InstallForce` at 650.07 ms versus ModuleFast at 885.78 ms, `SaveNoOp` at 12.73 ms, `SaveForce` at 20.83 ms, `UpdateNoOp` at 439.81 ms, and `UpdateForce` at 606.19 ms. Windows PowerShell 5.1 measured managed fastest for `InstallNoOp` at 538.53 ms, `InstallForce` at 797.95 ms, `SaveNoOp` at 14.62 ms, `SaveForce` at 23.83 ms, `UpdateNoOp` at 479.84 ms, and `UpdateForce` at 880.04 ms; ModuleFast remained skipped on Windows PowerShell 5.1.
- After adding anonymous remote metadata request coalescing, PowerShell 7 reran a managed-only full `Microsoft.Graph` 2.38.0 install. The run succeeded in 26246.30 ms with 78 packages, 77 dependencies, 81 repository requests, 186.5 MB of downloaded package bytes, and about 1.05 GB of output. This validates the coalescing path in a heavy run, but it did not reduce the Graph request count for this dependency shape.
- After adding update baseline discovery, PowerShell 7 ran `ThreadJob` update without `-UpdateBaselineVersion`; the harness resolved 2.0.3 -> 2.1.0, managed updated and imported 2.1.0 successfully, and the timed update measured 3008.54 ms. The suite runner then ran `Microsoft.Graph.Authentication` update without a baseline; it resolved 2.37.0 -> 2.38.0, managed updated and imported 2.38.0 successfully, and the timed update measured 4229.30 ms.
- Managed-only heavy update evidence with `-ValidateImport -ImportTimeoutSeconds 30`: PowerShell 7 updated full `Microsoft.Graph` 2.37.0 -> 2.38.0 in 8909.10 ms and full `Az` 15.6.1 -> 16.0.0 in 10187.60 ms; Windows PowerShell 5.1 updated the same families in 16045.15 ms and 25002.29 ms. Full Graph and Az import validation timed out after 30 seconds on both hosts. PowerShell 7 updated `MicrosoftTeams` 7.7.0 -> 7.8.0 in 5049.66 ms and `ExchangeOnlineManagement` 3.9.2 -> 3.10.0 in 3285.01 ms with successful imports; Windows PowerShell 5.1 updated them in 7114.85 ms and 5014.80 ms with successful imports.
- Managed-only `RepairPlan` smoke evidence for stale `ThreadJob` 2.0.3 -> 2.1.0: PowerShell 7 planned the stale root in 1604.15 ms and Windows PowerShell 5.1 planned the same stale root in 2660.43 ms. The detail artifact recorded one planned maintenance action and no findings. ModuleFast, PSResourceGet, and PowerShellGet remain explicit repair-plan skips because they do not expose equivalent module-estate planning.
- Managed-only `RepairPlan -RepairScenario SourceDrift` smoke evidence for `ThreadJob` with source metadata stamped as `BenchmarkWrongSource` and a maintenance receipt expecting `PSGallery`: PowerShell 7 planned in 642.64 ms and Windows PowerShell 5.1 planned in 1088.05 ms. The detail artifacts recorded a forced repair install targeting `PSGallery` without applying changes.
- Managed-only `RepairPlan -RepairScenario ScopeDrift` smoke evidence for `ThreadJob` with a maintenance receipt expecting `CurrentUser`: PowerShell 7 planned in 734.11 ms and Windows PowerShell 5.1 planned in 665.10 ms. The detail artifacts recorded a repair install targeting `CurrentUser` without applying changes.
- Managed-only `RepairPlan -RepairScenario FamilyCoherence` smoke evidence for synthetic `Microsoft.Graph.Authentication` 2.36.0 plus `Microsoft.Graph.Users` 2.38.0: PowerShell 7 planned in 653.87 ms and Windows PowerShell 5.1 planned in 825.77 ms. The detail artifacts recorded a repair update for `Microsoft.Graph.Authentication` to `=2.38.0` without applying changes.
- Managed-only `RepairPlan -RepairScenario LoadedModuleSafety` smoke evidence with synthetic `ThreadJob` 1.0.0 and 2.0.0 plus loaded 1.0.0 evidence: PowerShell 7 planned in 685.50 ms and Windows PowerShell 5.1 planned in 666.29 ms. The detail artifacts recorded one maintenance action plus `ModuleState.LoadedVersionMismatch`, side-by-side, and source-unknown findings without applying changes.
- Managed-only `RepairPlan -RepairScenario CleanupPlanning` smoke evidence with synthetic `ThreadJob` 1.0.0 and 2.0.0: PowerShell 7 planned in 670.21 ms and Windows PowerShell 5.1 planned in 671.26 ms. The detail artifacts recorded a `Remove` action for the old 1.0.0 copy and a no-op for the retained 2.0.0 copy without deleting either version.
- PowerShell 7, full `Microsoft.Graph` install, 1 run: Managed 9511.12 ms, ModuleFast 10194.36 ms, PSResourceGet 71116.42 ms, PowerShellGet 119544.35 ms.
- After scoped repository request accounting, PowerShell 7 reran full `Microsoft.Graph` install against the ModuleFast speed gate. Managed completed in 7414.10 ms and ModuleFast completed in 8906.23 ms. The managed detail summary recorded 78 packages, 77 dependencies, 81 whole-operation repository requests, 80 package-delivery requests, 186.5 MB of downloaded package bytes, and about 1.05 GB of output.
- Windows PowerShell 5.1 reran managed-only full `Microsoft.Graph` install with the same request-accounting detail. Managed completed in 13478.31 ms with 78 packages, 77 dependencies, 81 whole-operation repository requests, 78 package-delivery requests, 178.4 MB of downloaded package bytes, and about 1.05 GB of output.
- PowerShell 7, full `Az` install, 1 run: Managed 22457.78 ms, PowerShellGet 131093.32 ms, PSResourceGet 141104.45 ms, ModuleFast failed while resolving `Az.DataTransfer(1.0.0)` from its source. Keep this failure visible because resolver compatibility is part of the install contract.
- After adding unique package/status counts, PowerShell 7 reran full `Az` install against the ModuleFast speed gate. Managed completed in 12857.88 ms. ModuleFast failed while resolving `Az.DataTransfer(1.0.0)` from its source. The managed detail summary recorded 204 dependency-tree package rows, 103 unique package outputs, 102 unique installed outputs, 1 unique already-installed output, 211 whole-operation repository requests, 206 package-delivery requests, and 137.5 MB of downloaded package bytes.
- After adding in-flight install coalescing for equivalent dependency targets and increasing managed dependency fan-out to 32, PowerShell 7 reran full `Microsoft.Graph` install against the ModuleFast speed gate with `-RepeatCount 3 -RotateEngineOrder -RemoveOutputRoots`. One rotated run measured managed first by median at 6561.35 ms versus ModuleFast at 7572.29 ms, but the follow-up strict `-ManagedMaxRank 1` gate showed the lane is not yet stable enough to call always-fastest: ModuleFast median 7352.39 ms, managed median 7474.17 ms, managed ratio 1.02x. Managed recorded 78 package-tree rows, 40 unique package outputs, 40 installed rows, 38 already-satisfied coalesced dependency rows, 81 whole-operation repository requests, 80 package-delivery requests, and 186.5 MB downloaded.
- A follow-up experiment tried a larger managed NuGet v2 download buffer, but the strict full Graph gate exposed download outliers and the change was reverted. The failed run is retained under `Ignore\Benchmarks\ManagedModules\Run-20260628-195825-164112`; managed median was 28147.64 ms versus ModuleFast 6546.36 ms, with one `Microsoft.Graph.Files` download taking about 103 seconds. Treat this as evidence that public-gallery backend and transport variance must be separated from engine changes.
- PowerShell 7 then ran same-source full `Microsoft.Graph` 2.38.0 install comparisons with both managed and ModuleFast using `https://pwsh.gallery/index.json`. The first single run measured managed at 3441.40 ms versus ModuleFast at 6652.64 ms. A follow-up repeated rotated gate with `-RepeatCount 3 -ManagedMaxRank 1` passed in `Ignore\Benchmarks\ManagedModules\Run-20260628-200710-188900`: managed median 3565.75 ms, ModuleFast median 8925.20 ms, all six operation rows succeeded, and the gate CSV contained no violations. The managed detail summary recorded 78 package-tree rows, 40 unique package outputs, 40 installed rows, 38 already-satisfied coalesced dependency rows, 41 whole-operation repository requests, 41 package-delivery requests, 186.5 MB downloaded, and about 1.05 GB of output. This proves the managed engine can beat the ModuleFast lane when both use the same package source; keep default-source and same-source gates separate.
- The suite runner now writes compact suite folders under `Ignore\Benchmarks\MM` and uses compact host/scenario folder names so Windows PowerShell 5.1 save benchmarks avoid legacy path-length failures. A warm lifecycle ratio gate passed on 2026-06-28 with `-ManagedMaxVsFastest 1.25`: PowerShell 7 measured managed fastest for `InstallNoOp` at 566.37 ms, `InstallForce` at 606.77 ms, `SaveNoOp` at 46.91 ms, and `SaveForce` at 29.27 ms; Windows PowerShell 5.1 measured managed fastest for `InstallNoOp` at 623.98 ms, `InstallForce` at 806.27 ms, `SaveNoOp` at 51.61 ms, and `SaveForce` at 36.09 ms.
- After splitting managed install dependency delivery into a dedicated partial, the same warm lifecycle ratio gate passed again on 2026-06-28: PowerShell 7 measured managed fastest for `InstallNoOp` at 659.84 ms, `InstallForce` at 751.35 ms, `SaveNoOp` at 86.92 ms, and `SaveForce` at 40.94 ms; Windows PowerShell 5.1 measured managed fastest for `InstallNoOp` at 685.87 ms, `InstallForce` at 838.27 ms, `SaveNoOp` at 83.16 ms, and `SaveForce` at 51.05 ms.
- PowerShell 7 reran the same-source full `Microsoft.Graph` install speed gate after the install-service split. The strict `-ManagedMaxRank 1` suite passed with managed median 5821.60 ms versus ModuleFast rows at 7596.55 ms, 10765.68 ms, and 12233.83 ms. Managed recorded 78 package-tree rows, 40 unique package outputs, 41 whole-operation repository requests, 41 package-delivery requests, and 186.5 MB downloaded.
- The managed PSGallery NuGet v2 fallback download path now uses the same 1 MB async sequential package stream as the NuGet v3/local paths. A before/after `Microsoft.Graph.Authentication` find/save suite showed managed PS7 save improving from 1715.04 ms to 1409.81 ms, narrowing the gap to PSResourceGet from 1.35x to 1.03x. Windows PowerShell 5.1 managed save improved from 2449.07 ms to 1881.92 ms and remained fastest.
- Managed package delivery now computes the package SHA256 while copying HTTP/local feed streams instead of reopening the `.nupkg` for a second full read. A repeated rotated PowerShell 7 `Microsoft.Graph.Authentication` save comparison measured managed median 1490.32 ms versus PSResourceGet 1410.84 ms, with managed root elapsed 1463.96 ms, download 957.65 ms, extraction 75.11 ms, and promotion 3.40 ms. This improves absolute managed timing versus the previous repeated run (1763.81 ms) but still leaves a roughly 5-6 percent PS7 save gap to close.
- Managed repository HTTP sends now use response-header completion so package content streams directly into the managed copy/hash path instead of first buffering the full response in memory. A repeated rotated PowerShell 7 `Microsoft.Graph.Authentication` save run ranked managed first by median at 1631.76 ms versus PSResourceGet at 1855.37 ms. A repeated rotated Windows PowerShell 5.1 run using a short benchmark output root ranked managed first at 1606.17 ms versus PSResourceGet at 1701.74 ms. Public-gallery timing still varies, so promote this to a suite gate before treating it as a durable release claim.
- `SaveGate` now captures the repeated rotated `Microsoft.Graph.Authentication` save comparison for PowerShell 7 and Windows PowerShell 5.1. It initially used a practical `-ManagedMaxVsFastest 1.05` tolerance while public-gallery variance was being separated from managed engine behavior.
- `SaveGate` now owns a strict managed-rank threshold. A repeated rotated strict run on 2026-06-29 passed on both hosts with managed rank 1: PowerShell 7 measured managed at 1122.16 ms and Windows PowerShell 5.1 measured managed at 1756.28 ms. Suite summary rows now show the effective gate threshold, so explicit strict runs no longer look like looser scenario-gated runs in the artifacts.
- Suite artifacts now also emit `BenchmarkInterpretation`, and the repeated `SaveGate` used that evidence to fix a real Windows PowerShell 5.1 save gap. Before the fix, managed ranked second at 1666.86 ms versus PSResourceGet at 1504.68 ms because the `net472` path used the older PowerShell Gallery v2 package endpoint with one redirect. After using the direct PowerShell Gallery CDN package URL on `net472`, the repeated gate passed on both hosts: PowerShell 7 managed median 1172.80 ms and Windows PowerShell 5.1 managed median 984.22 ms, each with one package request and zero redirects.
- Managed install no-op now checks installed versions before remote latest/range resolution. PowerShell 7 repeated rotated exact-version install lifecycle gates passed for `Microsoft.Graph.Authentication` 2.38.0 and `Az.Accounts` 5.5.0 with `-ManagedMaxRank 1`, ModuleFast, and PSResourceGet included. Graph.Authentication measured managed first for `InstallNoOp` at 513.14 ms and `InstallForce` at 674.82 ms; Az.Accounts measured managed first for `InstallNoOp` at 565.95 ms and `InstallForce` at 736.04 ms. Managed detail rows recorded zero repository requests for both no-op and warm forced install rows.
- The repeated exact-version install lifecycle gate was rerun on 2026-06-29 after the PS5.1 CDN save fix. Managed still ranked first for all eight timed rows across PowerShell 7 and Windows PowerShell 5.1. PS7 measured Graph.Authentication no-op/force at 555.83/720.65 ms and Az.Accounts no-op/force at 582.41/723.74 ms. Windows PowerShell 5.1 measured Graph.Authentication no-op/force at 565.36/867.57 ms and Az.Accounts no-op/force at 555.28/887.81 ms. Timed managed install rows used zero repository requests; no-op root work was about 3-5 ms and forced rows reused package and extraction caches, so the optimization artifact correctly identifies child-host harness overhead rather than install engine work for this small exact-version lane.
- The full `Microsoft.Graph` 2.38.0 `HeavySaveGate` scoreboard was rerun on 2026-06-29 after the PS5.1 CDN save fix. Managed ranked first on PowerShell 7 at 4815.85 ms versus PSResourceGet at 53277.79 ms and PowerShellGet at 68945.98 ms. Managed also ranked first on Windows PowerShell 5.1 at 5470.72 ms versus PSResourceGet at 55363.94 ms and PowerShellGet at 89473.49 ms. Both managed rows saved about 1.05 GB, used 40 package requests, followed zero redirects, downloaded 186.5 MB of package data, and retained only compact CSV/JSON evidence after output-root cleanup.
- Setup/preseed retries are now explicit benchmark metadata and apply only before timed operations. With the retry-enabled setup path, the combined Windows PowerShell 5.1 exact Graph/Az lifecycle gate passed under scenario gates: Graph.Authentication measured managed first for `InstallNoOp` at 440.03 ms and `InstallForce` at 947.20 ms; Az.Accounts measured managed first for `InstallNoOp` at 562.51 ms and `InstallForce` at 814.03 ms. Managed timed rows still recorded zero repository requests.
- Exact-version Graph/Az save lifecycle scenarios now sit beside the install lifecycle gates. A PowerShell 7 and Windows PowerShell 5.1 trial measured managed first for Graph.Authentication `SaveNoOp` and `SaveForce`, and for Az.Accounts `SaveNoOp` and `SaveForce`; the final Windows PowerShell Az rerun measured managed `SaveNoOp` at 199.01 ms and `SaveForce` at 375.97 ms with zero timed repository requests. Earlier failed setup rows showed public-gallery transport variance before timing, not a managed timed-operation regression.
- PowerShell 7 reran the named same-source full `Microsoft.Graph` 2.38.0 install `SpeedGate` on 2026-06-29 with scenario gates, `-RepeatCount 3`, rotated engine order, and both managed and ModuleFast using `https://pwsh.gallery/index.json`. The strict gate passed with managed rank 1: managed median 4447.36 ms versus ModuleFast median 6075.21 ms. Managed recorded 78 package-tree rows, 40 unique package outputs, 40 installed outputs, 38 already-satisfied coalesced dependency rows, 41 whole-operation repository requests, 41 package-delivery requests, 186.5 MB downloaded, and no suite gate violations.
- The corrected PowerShell 7 `Graph.Full.ProviderMatrix` proof measured managed first at 5080.40 ms, ModuleFast at 7133.02 ms, PSResourceGet at 53378.88 ms, and PowerShellGet at 67081.83 ms. All four engines succeeded under provider-default execution, so this is the clean full-Graph default-provider scoreboard after the ModuleFast source-default fix.
- Provider-matrix scenarios now use `ModuleFastSource = ProviderDefault`, which makes the harness omit ModuleFast `-Source` instead of forcing the same-source URL. After fixing the child runner so it does not reintroduce a default source, a PowerShell 7 `Az.Accounts.ProviderMatrix` smoke succeeded for all four engines: managed 1508.50 ms, ModuleFast 2098.41 ms, PSResourceGet 2293.03 ms, and PowerShellGet 9070.28 ms.
- The corrected PowerShell 7 `Az.Full.ProviderMatrix` proof measured managed first at 6927.69 ms while PSResourceGet and PowerShellGet completed at 149062.25 ms and 140900.99 ms. ModuleFast still failed under provider-default execution because its own default path could not resolve `Az.DataTransfer(1.0.0)` from `https://pwsh.gallery/index.json`, so keep this row as source/compatibility evidence rather than a successful full-Az ModuleFast speed comparison.
- `RepairGate` promotes repair planning into suite scenarios. A focused PowerShell 7 and Windows PowerShell 5.1 proof for loaded-module safety and cleanup planning passed with scenario gates: loaded-module safety planned in 677.81 ms on PowerShell 7 and 711.68 ms on Windows PowerShell 5.1 with one action and three findings; cleanup planning planned in 659.97 ms and 663.18 ms with two actions and two findings. ModuleFast, PSResourceGet, and PowerShellGet are explicit skips because they have no equivalent module-estate repair planning command.
- The first PowerShell 7 `HeavyLifecycleGate` proof for full `Microsoft.Graph` 2.38.0 exact install no-op/force passed with strict scenario gates and `-RepeatCount 1`. Managed ranked first for `InstallNoOp` at 669.94 ms versus ModuleFast at 840.16 ms and PSResourceGet at 1812.68 ms. Managed also ranked first for `InstallForce` at 819.69 ms versus ModuleFast at 897.23 ms and PSResourceGet at 48660.76 ms. Both managed timed rows used zero repository requests and zero downloaded bytes; force reused one package-cache hit and treated 39 dependencies as already installed. Repeat this lane with `-RepeatCount 3` and add the full Az heavy lane before claiming full-family lifecycle dominance.
- The repeated PowerShell 7 full `Microsoft.Graph` 2.38.0 `HeavyLifecycleGate` passed on 2026-06-29 with strict scenario gates, `-RepeatCount 3`, rotated engine order, and warm cache mode. Managed ranked first for `InstallNoOp` with a 690.44 ms median versus ModuleFast at 863.67 ms and PSResourceGet at 1921.58 ms. Managed also ranked first for `InstallForce` with an 826.10 ms median versus ModuleFast at 847.42 ms and PSResourceGet at 49003.39 ms. Managed timed rows used zero repository requests and zero downloaded bytes; the force row reused one package-cache hit and treated 39 dependencies as already installed. The remaining broad lifecycle proof is the full Az heavy lane.
- The first PowerShell 7 full `Az` 16.0.0 `HeavyLifecycleGate` passed for managed on 2026-06-29, but it is compatibility evidence rather than a clean speed race. Managed completed `InstallNoOp` at 1528.72 ms and `InstallForce` at 936.60 ms with zero timed repository requests and zero downloaded bytes; the force row reused one package-cache hit and treated 102 dependencies as already installed. ModuleFast failed setup three times because `Az.DataTransfer(1.0.0)` was not resolved from `https://pwsh.gallery/index.json`; PSResourceGet also failed setup three times from the same source. Keep this same-source failure visible, and use a separate default-source/provider-specific lane if the question is native-provider speed rather than managed resolver coverage.
- A managed-only repeated PowerShell 7 full `Az` 16.0.0 no-op/force lifecycle proof then passed with `-RepeatCount 3`, warm cache mode, and output-root cleanup. Managed `InstallNoOp` median was 831.47 ms (767.91-913.94 ms) and managed `InstallForce` median was 912.98 ms (885.54-984.86 ms). Both rows used zero timed repository requests and zero downloaded bytes; force reused one package-cache hit and treated 102 dependencies as already installed. This proves the managed lifecycle path is stable for the preseeded Az root, not that full Az save or cold install has the same shape.
- `HeavySaveGate` now owns named full-family save scenarios for `Microsoft.Graph` and `Az`, keeping broad save throughput evidence separate from install lifecycle gates. The first PowerShell 7 full `Az` 16.0.0 save comparison measured managed first at 3738.13 ms, PowerShellGet at 127817.69 ms, and PSResourceGet at 151122.53 ms. Managed saved about 623.0 MB across 2789 files, recorded 204 package rows, 103 unique package outputs, 105 whole-operation repository requests, 103 package-delivery requests, and 137.5 MB downloaded. The optimization artifact points at download/source/cache behavior as the next question; extraction and promotion were much smaller.
- Save payload roots now use short disposable paths under the benchmark temp root, while CSV/JSON artifacts remain under the benchmark run directory. This keeps Windows PowerShell 5.1/native provider comparisons from failing simply because the repository-backed artifact path is too deep.
- After the short save-root fix, Windows PowerShell 5.1 full `Microsoft.Graph` 2.38.0 save became a clean three-engine race: managed measured 12862.88 ms, PSResourceGet 60098.00 ms, and PowerShellGet 82563.59 ms, with all three providers succeeding. Managed saved about 1.05 GB across 482 files, recorded 78 package rows, 40 unique package outputs, 80 whole-operation/package-delivery requests, and 186.5 MB downloaded.
- Windows PowerShell 5.1 full `Az` 16.0.0 save with the same short root measured managed first at 31863.64 ms and PowerShellGet at 130163.03 ms; PSResourceGet still failed after 84213.81 ms in its own temp extraction path for `Az.MachineLearningServices`. Managed saved about 623.0 MB across 2789 files, recorded 204 package rows, 103 unique package outputs, 210 whole-operation repository requests, 206 package-delivery requests, and 137.5 MB downloaded. The optimization artifact still points at download/source/cache behavior rather than extraction or promotion.
- The full `Az` 16.0.0 `HeavySaveGate` scoreboard was rerun on 2026-06-29 after the `net472` direct-CDN fix. Managed ranked first on PowerShell 7 at 5726.26 ms versus PowerShellGet at 128869.37 ms and PSResourceGet at 146362.66 ms. Managed also ranked first on Windows PowerShell 5.1 at 8561.29 ms versus PowerShellGet at 151052.21 ms, while PSResourceGet still failed after 84264.43 ms with zero output. Both managed rows saved about 623.0 MB across 2789 files, used 103 package requests, followed zero redirects, downloaded 137.5 MB of package data, and retained only compact CSV/JSON evidence after output-root cleanup. Read this as the current full-Az save scoreboard; ModuleFast is absent because it has no equivalent save command.
- Warm managed package caches now live under a run-scoped short temp root instead of inside each output root, so repeated save/install/update rows can prove reusable cache behavior even with `-RemoveOutputRoots`. A PowerShell 7 repeated warm managed `ThreadJob` 2.1.0 save proof showed iteration 1 downloading 82971 bytes with zero cache hits, then iteration 2 using two cache hits, zero downloaded bytes, and no package-delivery requests; the cache and output roots were removed after the CSV/JSON evidence was written.
- `HeavySaveCacheGate` adds managed-only full-family warm-cache save scenarios with scenario-owned `CacheMode=Warm` and `RepeatCount=2`. A PowerShell 7 full `Microsoft.Graph` 2.38.0 run measured iteration 1 at 3645.44 ms with 40 package requests, 186506621 downloaded bytes, and zero cache hits; iteration 2 measured 1944.07 ms with zero repository/package requests, zero downloaded bytes, and 40 cache hits. The suite summary and optimization target artifacts now include first/last managed timing, request, download, and cache-hit fields so this cache contrast is visible without opening raw child rows.
- A PowerShell 7 full `Az` 16.0.0 `HeavySaveCacheGate` run measured iteration 1 at 4136.39 ms with 107 repository requests, 103 package requests, 137506101 downloaded bytes, 0 cache hits, and about 623.0 MB of saved output. Iteration 2 measured 3586.55 ms with 4 metadata requests, 0 package requests, 0 downloaded bytes, 103 cache hits, and the same 2789-file output shape. Read this as strong package-cache reuse with a smaller wall-clock drop than Graph because full Az save still has to materialize the 623 MB module tree.
- `SecurityGate` promotes Authenticode install/save compatibility into a named suite scenario. The PowerShell 7 and Windows PowerShell 5.1 proof for `ThreadJob` 2.1.0 with `-AuthenticodeCheck` now passes strict managed-rank gates plus `GateManagedMinAuthenticodeCheckedFiles=1`. PowerShell 7 measured managed install/save at 1386.63/815.64 ms versus PSResourceGet at 3208.73/3065.33 ms. Windows PowerShell 5.1 measured managed install/save at 1417.27/799.97 ms versus PSResourceGet at 3228.07/2468.69 ms. Each managed suite row reported `ManagedAuthenticodeCheckedFiles=4`, covering the signable `.psd1`, `.psm1`, and dependency `.dll`/manifest files before promotion. This is initial file-signature evidence, not full catalog/timestamp/short-lived-certificate parity.
- The expanded package-cache materialization path now uses the Windows platform copy primitive on `net472` while preserving the buffered stream path for modern PowerShell. Full `Az` 16.0.0 `HeavySaveCacheGate` on Windows PowerShell 5.1 improved the warm second save from the prior 6744 ms record to 3900 ms with 103 package-cache hits, 103 extraction-cache hits, zero package requests, and zero downloaded bytes. Current PowerShell 7 diagnostic reruns measured the warm second save at 3526-3755 ms on the unchanged modern stream path, so treat this as a PS5.1 materialization improvement rather than a PS7 optimization claim.

Install and save numbers should be read through their own comparison sets. ModuleFast is the install speed competitor and is intentionally present in the full Graph same-source install gate. Save rows compare managed against save-capable providers such as PSResourceGet and PowerShellGet; ModuleFast skip rows in save evidence are capability markers, not managed losses. Warm install lifecycle rows can also reuse an already-populated module root, while save rows have to materialize a saved module tree, so a fast install no-op/force result should not be read as evidence about save throughput.

Treat these numbers as a local baseline, not a release claim. Re-run the same commands after installer changes and compare the emitted CSV/JSON files before deciding whether an optimization is real.

# Managed Module Benchmarks

`Compare-ManagedModuleEngines.ps1` is the PowerShell/user-workflow benchmark harness for managed module lifecycle work. It compares PSPublishModule's managed cmdlets against available compatibility tools, writes raw evidence and summary files under `Ignore\Benchmarks\ManagedModules\Run-*`, and keeps benchmark commands out of the shipped module surface.

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

Compare install behavior in disposable module/profile roots:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkSuite.ps1 -Suite Smoke -HostName PowerShell7 -Operation Install
```

Compare update behavior from a known stale version in disposable module/profile roots:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName ThreadJob -Version 2.1.0 -UpdateBaselineVersion 2.0.3 -Operation Update -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -RepeatCount 1 -SkipBuild
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

Compare the managed installer against the install-only speed gate:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ModuleName Microsoft.Graph -Operation Install -Engine Managed,ModuleFast,PSResourceGet,PowerShellGet -AcceptLicense -RepeatCount 1 -SkipBuild
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

- `managed-module-results.csv`: raw per-run rows, including repair scenario and optional import validation columns.
- `managed-module-results.json`: raw per-run rows as JSON.
- `managed-module-summary.csv`: grouped median/min/max rows by operation, scenario, and engine, including output size and managed detail medians when detail artifacts are present.
- `managed-module-comparison.csv`: fastest successful engine per operation and scenario, plus managed rank, ratio, and managed package/request/download/extract/promotion medians.
- `metadata.json`: runtime, selected engines, module, version, repository, and output paths.
- `managed-install-details-<n>.json`: managed install package tree details, written under the benchmark run folder for install runs. It includes package/dependency counts, per-package elapsed/download/extraction/promotion timings, request counts, cache hits, and byte counts.

Start with `managed-module-comparison.csv` for a quick scoreboard, then inspect `managed-module-results.csv` when a competitor is skipped or failed.

Suite runs write `suite-summary.csv`, `suite-summary.json`, `suite-hosts.csv`, and per-scenario child run folders. Suite summaries carry the managed detail medians from each child comparison row so heavy Graph/Az/Teams/Exchange runs can be inspected from one file before drilling into package detail artifacts.

## Notes

`Find` and `Save` are safe to compare directly because each compatible engine can run against isolated output folders. ModuleFast is included as an install-only competitor; its find/save/update/repair rows are explicit skips when it does not expose an equivalent command. `Install` runs each engine in a disposable child PowerShell host with benchmark-owned profile, cache, temp, and module path environment variables. `Update` first installs `-UpdateBaselineVersion` outside the timed window, then times only the update operation in the same disposable host root. `RepairPlan -RepairScenario StaleVersion` uses the same stale baseline setup, then times the managed `Repair-ManagedModule -Plan -Latest` planning path without executing changes. `RepairPlan -RepairScenario SourceDrift` installs the target version outside the timed window, stamps disposable module metadata with a different source, writes a maintenance receipt expecting the requested repository, and times repair planning without applying changes. `RepairPlan -RepairScenario ScopeDrift` installs the target version outside the timed window, writes a maintenance receipt expecting `CurrentUser`, and times repair planning without applying changes. `RepairPlan -RepairScenario FamilyCoherence` seeds lightweight synthetic `Microsoft.Graph.Authentication` and `Microsoft.Graph.Users` module manifests with mixed versions, passes the built-in Graph family policy, and times repair planning without applying changes. `RepairPlan -RepairScenario LoadedModuleSafety` seeds side-by-side synthetic versions, imports the stale copy in the child host, and times `Repair-ManagedModule -Plan -IncludeLoaded -Version 2.0.0` so loaded-version findings stay visible without mutating the root. `RepairPlan -RepairScenario CleanupPlanning` seeds side-by-side synthetic versions and times `Repair-ManagedModule -Plan -Cleanup OldVersions` so old-version removal is measured as planning rather than deletion. Use `-RepairScenario All` to emit every repair-plan scenario row. Baseline version discovery runs only for `Update` and `RepairPlan -RepairScenario StaleVersion`; source, scope, family, loaded-module, and cleanup synthetic repair scenarios avoid that repository setup cost. `-CacheMode Cold` clears disposable provider/package caches after the baseline install; `-CacheMode Warm` preserves native provider caches and gives managed delivery an explicit package cache folder under the benchmark root; `Default` preserves the historical harness behavior. `-ValidateImport` imports the highest-version manifest from the benchmark output root after timing and writes `ImportStatus`, `ImportVersion`, `ImportMilliseconds`, `ImportManifestPath`, and `ImportError` columns. Use `-ImportTimeoutSeconds` to cap that post-timing import proof for very large module families. ModuleFast setup/import is outside the timed operation, uses the locally installed `ModuleFast` module, and is skipped on Windows PowerShell 5.1 because ModuleFast requires PowerShell 7.2 or newer. Install and update output roots are recorded in the CSV/JSON rows and may point at the short temp benchmark folder to keep Windows PowerShell 5.1 below legacy path-length limits.

License acceptance is explicit. Use `-AcceptLicense` only when the benchmark scenario is allowed to accept the package license on behalf of the run.

Authenticode validation is explicit. Use `-AuthenticodeCheck` only when the benchmark is meant to measure signature verification overhead; unsigned packages are expected to fail that safety gate.

The suite runner enables license acceptance only for scenarios that require it, such as full Graph and Az package family scenarios. Keep that behavior explicit when adding new scenarios.

Use `-RotateEngineOrder` with `-RepeatCount 3` or higher before making performance claims. Repository and host caches can otherwise make the second or third engine look faster than it really is.

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
- PowerShell 7, full `Az` install, 1 run: Managed 22457.78 ms, PowerShellGet 131093.32 ms, PSResourceGet 141104.45 ms, ModuleFast failed while resolving `Az.DataTransfer(1.0.0)` from its source. Keep this failure visible because resolver compatibility is part of the install contract.

Treat these numbers as a local baseline, not a release claim. Re-run the same commands after installer changes and compare the emitted CSV/JSON files before deciding whether an optimization is real.

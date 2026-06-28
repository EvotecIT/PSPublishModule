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

List emitted operation rows without running:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -ListScenarios
```

## Output Files

Every run writes:

- `managed-module-results.csv`: raw per-run rows.
- `managed-module-results.json`: raw per-run rows as JSON.
- `managed-module-summary.csv`: grouped median/min/max rows by operation and engine.
- `managed-module-comparison.csv`: fastest successful engine per operation, plus managed rank and ratio.
- `metadata.json`: runtime, selected engines, module, version, repository, and output paths.
- `managed-install-details-<n>.json`: managed install package tree details, written under the benchmark run folder for install runs. It includes package/dependency counts, per-package elapsed/download/extraction/promotion timings, request counts, cache hits, and byte counts.

Start with `managed-module-comparison.csv` for a quick scoreboard, then inspect `managed-module-results.csv` when a competitor is skipped or failed.

Suite runs write `suite-summary.csv`, `suite-summary.json`, `suite-hosts.csv`, and per-scenario child run folders.

## Notes

`Find` and `Save` are safe to compare directly because each compatible engine can run against isolated output folders. ModuleFast is included as an install-only competitor; its find/save rows are explicit skips because it does not expose equivalent commands. `Install` runs each engine in a disposable child PowerShell host with benchmark-owned profile, cache, temp, and module path environment variables. ModuleFast setup/import is outside the timed operation, uses the locally installed `ModuleFast` module, and is skipped on Windows PowerShell 5.1 because ModuleFast requires PowerShell 7.2 or newer. Install output roots are recorded in the CSV/JSON rows and may point at the short temp benchmark folder to keep Windows PowerShell 5.1 below legacy path-length limits.

License acceptance is explicit. Use `-AcceptLicense` only when the benchmark scenario is allowed to accept the package license on behalf of the run.

The suite runner enables license acceptance only for scenarios that require it, such as full Graph and Az package family scenarios. Keep that behavior explicit when adding new scenarios.

Use `-RotateEngineOrder` with `-RepeatCount 3` or higher before making performance claims. Repository and host caches can otherwise make the second or third engine look faster than it really is.

## Current Evidence

Measured on 2026-06-28 with ModuleFast 0.6.1 installed in the current user's PowerShell 7 module path:

- PowerShell 7, `ThreadJob` 2.1.0 install, 3 rotated runs: ModuleFast median 2310.31 ms, Managed median 2769.30 ms, PSResourceGet median 4840.46 ms, PowerShellGet median 10361.05 ms.
- Windows PowerShell 5.1, `ThreadJob` 2.1.0 install, 1 run: Managed 4410.96 ms, PSResourceGet 5215.20 ms, PowerShellGet 76334.35 ms, ModuleFast skipped because it requires PowerShell 7.2 or newer.
- PowerShell 7, full `Microsoft.Graph` install, 1 run: Managed 9511.12 ms, ModuleFast 10194.36 ms, PSResourceGet 71116.42 ms, PowerShellGet 119544.35 ms.
- PowerShell 7, full `Az` install, 1 run: Managed 22457.78 ms, PowerShellGet 131093.32 ms, PSResourceGet 141104.45 ms, ModuleFast failed while resolving `Az.DataTransfer(1.0.0)` from its source. Keep this failure visible because resolver compatibility is part of the install contract.

Treat these numbers as a local baseline, not a release claim. Re-run the same commands after installer changes and compare the emitted CSV/JSON files before deciding whether an optimization is real.

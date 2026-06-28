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

Compare selected engines:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Compare-ManagedModuleEngines.ps1 -Suite Smoke -Engine Managed,PSResourceGet,PowerShellGet
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

Start with `managed-module-comparison.csv` for a quick scoreboard, then inspect `managed-module-results.csv` when a competitor is skipped or failed.

Suite runs write `suite-summary.csv`, `suite-summary.json`, `suite-hosts.csv`, and per-scenario child run folders.

## Notes

`Find` and `Save` are safe to compare directly because each engine can run against isolated output folders. `InstallManaged` intentionally measures only the managed install command because normal `Install-Module` and `Install-PSResource` do not provide a reliable custom module-root isolation contract. Native install/update comparison should be added as a separate disposable-host lane before it is treated as a fair scoreboard.

License acceptance is explicit. Use `-AcceptLicense` only when the benchmark scenario is allowed to accept the package license on behalf of the run.

The suite runner enables license acceptance only for scenarios that require it, such as the full Graph package family. Keep that behavior explicit when adding new scenarios.

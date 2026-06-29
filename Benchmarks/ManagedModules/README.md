# Managed Module Benchmarks

These benchmarks are intentionally small. They measure normal public commands and
produce one CSV file that can update the root README table.

Scenarios:

- `ThreadJob`
- `Microsoft.Graph.Authentication`
- `Microsoft.Graph`
- `Az.Accounts`
- `Az`

Operations:

- `Find`
- `Install`
- `Save`

Repair is not benchmarked here because PowerShellGet, PSResourceGet, and
ModuleFast do not expose an equivalent module-estate repair command.

Run PowerShell 7:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Measure-ManagedModuleBenchmark.ps1 -RepeatCount 1
```

Append Windows PowerShell 5.1 results:

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Measure-ManagedModuleBenchmark.ps1 -RepeatCount 1 -Append
```

Native `Install-Module` and `Install-PSResource` install into the user module
location. To include those install rows, run in a disposable profile or VM and
pass `-AllowUserProfileInstall`.

Update the root README marker block:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Update-ManagedModuleBenchmarkReadme.ps1 -ResultPath .\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv
```

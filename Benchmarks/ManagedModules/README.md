# Managed Module Benchmarks

These benchmarks are intentionally small. They measure normal public commands and
produce one CSV file that can update the root README table.

Scenarios:

- `PSScriptAnalyzer`
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

What the benchmark is trying to prove:

- Managed should be fast because dependency resolution, package download,
  extraction, caching, and promotion run in the C# engine instead of through the
  older provider stack.
- Large dependency graphs should benefit from concurrent dependency work and
  operation-local coalescing, not from skipping dependencies.
- PS 5.1 should use the same managed engine path as PS 7, with short staging
  paths so deep packages such as `Az.MachineLearningServices` remain viable.
- ModuleFast is a useful comparison, but it uses `pwsh.gallery`, a community
  NuGet v3 mirror used by ModuleFast rather than the normal PSGallery provider
  path. If that mirror is missing a dependency, mark the row as a source/index
  miss instead of comparing elapsed time.

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

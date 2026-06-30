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
  extraction, caching, and promotion run in one purpose-built engine instead of
  through a generic package-provider workflow.
- Large dependency graphs should benefit from concurrent dependency work and
  operation-local coalescing, not from skipping dependencies.
- PS 5.1 should use the same managed engine path as PS 7, with short staging
  paths so deep packages such as `Az.MachineLearningServices` remain viable.
- ModuleFast is a useful comparison, but it uses `pwsh.gallery`, a community
  NuGet v3 mirror used by ModuleFast rather than the normal PSGallery provider
  path. If that mirror is missing a dependency, mark the row as a source/index
  miss instead of comparing elapsed time.
- Safe runs skip native `Install-Module` and `Install-PSResource` rows unless
  `-AllowUserProfileInstall` is passed, because those tools install into the
  user module path. Use a disposable profile or VM when including those rows.
- When `-RepeatCount` is greater than one, the README updater summarizes each
  tool/scenario row with the median successful timing before writing the table.

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

Windows PowerShell 5.1 uses short root-level run and temp folders by default
because deep packages such as `Az.MachineLearningServices` can still hit legacy
path limits when extracted under a long repository worktree path.

Update the root README marker block:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Update-ManagedModuleBenchmarkReadme.ps1 -ResultPath .\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv
```

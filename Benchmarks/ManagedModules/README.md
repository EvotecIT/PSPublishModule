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
  miss instead of comparing elapsed time. The matrix wrapper passes
  `-ModuleFastSource https://pwsh.gallery/index.json` by default so the source
  is explicit in both the command line and result CSV.
- Native `Install-Module` and `Install-PSResource` rows are real
  `CurrentUser` installs against a temporary Windows user. The runner stages
  only the native provider modules for that account, runs the measured install
  with a loaded user profile, imports the small CSV result, and deletes the
  account/profile afterward.
- When `-RepeatCount` is greater than one, the README updater summarizes each
  tool/scenario row with the median successful timing before writing the table.

Managed installs use `Install-ManagedModule -ModuleRoot <isolated folder>` and
ModuleFast uses `Install-ModuleFast -Destination <isolated folder>`. Native
providers do not expose an equivalent arbitrary install root for install
commands, so their install rows intentionally use the real `CurrentUser`
provider path inside a temporary Windows user. Use
`-SkipTemporaryUserNativeInstall` only when a machine cannot create a temporary
local benchmark account.

Managed `Install` and `Save` rows also include phase columns that make
performance triage less speculative. The wall-clock `Seconds` column stays the
scoreboard value; `ManagedDownloadMillisecondsSum`,
`ManagedExtractionMillisecondsSum`, `ManagedDependencyMillisecondsRoot`,
`ManagedPromotionMillisecondsSum`, request counts, byte counts, and matching
max/wait columns show where the managed engine spent time. Sum columns can be
larger than wall-clock time because dependency downloads and extraction run in
parallel; the max and root elapsed columns are usually better indicators for
the critical path.

Run PowerShell 7:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 -BenchmarkHost PowerShell7 -RepeatCount 1
```

Run the focused Managed-vs-ModuleFast install comparison:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 -ComparisonProfile ManagedVsModuleFast -BenchmarkHost PowerShell7 -RepeatCount 1 -OutputPath .\Ignore\Benchmarks\ManagedModules\managed-vs-modulefast.csv -OutputRoot .\Ignore\Benchmarks\ManagedModules\ManagedVsModuleFast
```

Use the focused profile while tuning install throughput. It skips
PSResourceGet/PowerShellGet native install baselines and only runs the install
rows where ModuleFast has an equivalent public command. To compare a local or
development ModuleFast build, install that build into the benchmark host first
or pass its supported source with `-ModuleFastSource`; the result CSV records
`ModuleFastSource`, `EngineCommandPath`, and `EngineModuleVersion` for each
row.

Append Windows PowerShell 5.1 results:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 -BenchmarkHost WindowsPowerShell -RepeatCount 1 -Append
```

Native `Install-Module` and `Install-PSResource` install into the user module
location. The runner measures those rows from a temporary local Windows account
and deletes that account/profile afterward; it does not move the real user
module folder.

Windows PowerShell 5.1 uses short root-level run and temp folders by default
because deep packages such as `Az.MachineLearningServices` can still hit legacy
path limits when extracted under a long repository worktree path.

Update the root README marker block:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Benchmarks\ManagedModules\Update-ManagedModuleBenchmarkReadme.ps1 -ResultPath .\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv
```

# Managed Module Benchmarks

This folder contains the benchmark runner used to measure managed module
lifecycle commands. It writes CSV evidence and can refresh the benchmark table
in the root README.

The runner measures public commands. It does not skip dependencies or replace a
tool's normal install behavior just to make a row faster.

## Scenarios

The built-in scenarios are:

- `SingleModule`: `PSScriptAnalyzer`
- `GraphAuthentication`: `Microsoft.Graph.Authentication`
- `Graph`: `Microsoft.Graph`
- `AzAccounts`: `Az.Accounts`
- `Az`: `Az`

## Operations

Supported operations are:

- `Find`
- `Install`
- `Save`

Repair is not included because the comparison tools do not expose an equivalent
module-estate repair command.

## Engines

Supported engines are:

- `Managed`
- `ModuleFast`
- `PSResourceGet`
- `PowerShellGet`

The `Managed` engine uses PSPublishModule's managed lifecycle commands. Native
provider install rows use the provider's normal `CurrentUser` install behavior.
When those rows are enabled on Windows, the runner can execute them inside a
temporary local user profile so the real user module folder is not changed.

## Run The Default Profile

The default profile is `ManagedVsModuleFast`. It runs install-focused rows for
the engines that expose equivalent install commands.

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
    -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 `
    -BenchmarkHost PowerShell7 `
    -RepeatCount 1
```

Use `-OutputPath` and `-OutputRoot` to control where evidence is written:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
    -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 `
    -BenchmarkHost PowerShell7 `
    -RepeatCount 3 `
    -OutputPath .\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv `
    -OutputRoot .\Ignore\Benchmarks\ManagedModules\Runs
```

## Run The Full Matrix

Use `-ComparisonProfile Full` when you need a broader compatibility baseline
across all supported operations and engines.

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
    -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 `
    -ComparisonProfile Full `
    -BenchmarkHost PowerShell7 `
    -RepeatCount 1
```

Limit a run with `-ScenarioName`, `-Operation`, and `-Engine`:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
    -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 `
    -ComparisonProfile Full `
    -ScenarioName GraphAuthentication `
    -Operation Install `
    -Engine Managed,PSResourceGet `
    -BenchmarkHost PowerShell7
```

## Host Selection

`-BenchmarkHost` controls which PowerShell host runs the measured work:

| Value | Behavior |
| --- | --- |
| `Current` | Uses the current host. |
| `PowerShell7` | Uses the highest available PowerShell 7 executable. |
| `WindowsPowerShell` | Uses Windows PowerShell 5.1. |

Append results from another host with `-Append`:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
    -File .\Benchmarks\ManagedModules\Invoke-ManagedModuleBenchmarkMatrix.ps1 `
    -BenchmarkHost WindowsPowerShell `
    -RepeatCount 1 `
    -Append
```

Windows PowerShell 5.1 uses short root-level run and temp folders by default to
avoid legacy path-length issues in deep module graphs.

## Temporary User Native Installs

Native `Install-Module` and `Install-PSResource` install into a user module
location. On Windows, the runner measures those rows from a temporary local user
profile and removes that account/profile after the run.

Use `-SkipTemporaryUserNativeInstall` only when the machine cannot create a
temporary local benchmark account. Use `-KeepTemporaryUserProfile` when a failed
native-provider run needs inspection.

## ModuleFast Selection

`-ModuleFastModulePath` selects the ModuleFast module implementation loaded by
the benchmark host. When it is omitted, the host uses the `Install-ModuleFast`
command already available in that PowerShell session.

`-ModuleFastSource` controls the source URL passed to ModuleFast. The result CSV
records `ModuleFastSource`, `ModuleFastModulePath`, `EngineCommandPath`,
`EngineModuleBase`, and `EngineModuleVersion` so rows from different module
implementations or sources can be separated later.

## Output Columns

The CSV includes:

- scenario, operation, engine, host, repeat number, status, and elapsed seconds
- source and module identity columns for each engine
- managed phase timings for install/save rows
- byte counts, request counts, and wait/max columns for managed dependency work
- failure reason when a row does not complete

When `-RepeatCount` is greater than one, the README updater uses the median
successful timing for each row.

## Refresh The Root README Table

After a run, update the root README marker block:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
    -File .\Benchmarks\ManagedModules\Update-ManagedModuleBenchmarkReadme.ps1 `
    -ResultPath .\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv
```

Limit the rendered engines:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
    -File .\Benchmarks\ManagedModules\Update-ManagedModuleBenchmarkReadme.ps1 `
    -ResultPath .\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv `
    -Engine Managed,ModuleFast
```

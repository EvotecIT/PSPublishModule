# Managed Module Benchmarks

This folder contains the managed module benchmark suite used to compare
PSPublishModule's managed module lifecycle commands with equivalent public
provider commands. The suite is a PowerForge benchmark spec: provider setup,
command invocation, skip rules, validation, and managed result metrics are
declared in the spec; the reusable runner, profile, artifact, comparison, and
README update mechanics stay in PowerForge.

## Run The Matrix

Build or import the PSPublishModule you want to measure, then run:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -Scenario SingleModule, GraphAuthentication, Graph, AzAccounts, Az `
    -Operation Find, Install, Save `
    -Engine Managed, ModuleFast, PSResourceGet, PowerShellGet `
    -Host Core, Desktop `
    -WarmupCount 1 `
    -IterationCount 3 `
    -RunMode local
```

That comparison expands the standard scenario set across the lifecycle
operations and provider engines:

- `SingleModule`: `PSScriptAnalyzer`
- `GraphAuthentication`: `Microsoft.Graph.Authentication`
- `Graph`: `Microsoft.Graph`
- `AzAccounts`: `Az.Accounts`
- `Az`: `Az`

The checked-in README table is usually refreshed with a one-iteration full
matrix smoke so the full matrix shape is proven without multiplying the large
Graph/Az download work by the normal warmup policy. For stable local numbers,
keep the warmup and iteration counts above.

## Select A Matrix

The benchmark spec declares the available cases, engines, and operations. Use
runner filters when you want a focused matrix:

| Filter | Example |
| --- | --- |
| `-Scenario` / `-Case` | `SingleModule, AzAccounts` |
| `-Operation` | `Find, Install, Save` |
| `-Engine` | `Managed, ModuleFast, PSResourceGet, PowerShellGet` |
| `-Host` | `Core, Desktop` |

Example full matrix for one scenario:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -Scenario SingleModule `
    -Operation Find, Install, Save `
    -Engine Managed, ModuleFast, PSResourceGet, PowerShellGet
```

`ModuleFast` only participates in `Install`; non-equivalent lanes are recorded as
skipped instead of being timed.
The `Desktop` host runs the supported Windows PowerShell 5.1 lanes. Engines that
do not support that host, such as `ModuleFast` and `PSResourceGet`, are recorded
as skipped for that host instead of being treated as failures.

Use `ModuleFastPath` to pin the released ModuleFast lane to a specific local
module path instead of resolving `ModuleFast` from `PSModulePath`.

## Fair Install Comparisons

The default sources compare the normal product paths: managed modules use the
official PowerShell Gallery while ModuleFast uses its configured ModuleFast
source. Those results include repository and CDN behavior, so they should not be
presented as a pure installer-engine comparison.

To isolate installer behavior, point both engines at the same NuGet v3 feed:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -Scenario SingleModule, Graph, Az `
    -Operation Install `
    -Engine Managed, ModuleFast `
    -Variable @{
        RepositoryUri     = 'https://feed.example.test/index.json'
        ModuleFastSource  = 'https://feed.example.test/index.json'
        ManagedModulePath = 'C:\Build\PSPublishModule.dll'
        ModuleFastPath    = 'C:\Modules\ModuleFast\1.0.0\ModuleFast.psd1'
    }
```

Every install sample starts with a fresh destination. ModuleFast import and its
resolution-cache reset happen during setup, outside measured time. The managed
binary can be pinned with `ManagedModulePath`; validation fails if the measured
command does not come from that exact file. The managed install intentionally
uses its operation-local package buffer rather than a
persistent extracted-package cache because ModuleFast has no equivalent cache
in this lane. ModuleFast runs with `DestinationOnly`, so neither engine mutates
the process module path. Validation requires exactly one requested module
manifest with the exact requested version, and the artifacts record installed
file count and bytes alongside timing.

## Native Provider Installs

`Install-PSResource` and `Install-Module` install into the current user profile.
The suite skips those install lanes by default so a normal benchmark run does
not mutate the maintainer's real module folder.

To measure those lanes safely on Windows, run the suite through the
`TemporaryLocalUser` benchmark profile from an elevated PowerShell session:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -Profile TemporaryLocalUser `
    -Cleanup KeepOnFailure `
    -Operation Install `
    -Engine Managed, PSResourceGet, PowerShellGet
```

The shared benchmark runner creates and removes the temporary local account,
writes normalized JSON/CSV/Markdown artifacts, and preserves the profile only
when requested by cleanup mode.

## Artifacts And README Table

The suite writes artifacts under `Ignore\Benchmarks\ManagedModules` by default:

- `samples.json` / `samples.csv`
- `summary.json` / `summary.csv`
- `comparison.json` / `comparison.md`
- `metadata.json`
- `run-report.json`

The suite also declares the root `README.MD` benchmark block, so a normal run can
refresh the managed-module table directly. To inspect the planned lanes without
running network or install work:

```powershell
Invoke-BenchmarkSuite -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 -Plan
```

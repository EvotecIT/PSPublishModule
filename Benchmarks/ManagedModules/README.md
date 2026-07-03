# Managed Module Benchmarks

This folder contains the managed module benchmark suite used to compare
PSPublishModule's managed module lifecycle commands with equivalent public
provider commands. The suite is a PowerForge benchmark spec: provider setup,
command invocation, skip rules, validation, and managed result metrics are
declared in the spec; the reusable runner, profile, artifact, comparison, and
README update mechanics stay in PowerForge.

## Run The Focused Comparison

Build or import the PSPublishModule you want to measure, then run:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -Scenario SingleModule, GraphAuthentication, Graph, AzAccounts, Az `
    -Operation Install `
    -Engine Managed, ModuleFast `
    -WarmupCount 1 `
    -IterationCount 3 `
    -RunMode local
```

That focused comparison measures `Install` for `Managed` and `ModuleFast` across
the standard scenario set:

- `SingleModule`: `PSScriptAnalyzer`
- `GraphAuthentication`: `Microsoft.Graph.Authentication`
- `Graph`: `Microsoft.Graph`
- `AzAccounts`: `Az.Accounts`
- `Az`: `Az`

Run from the PowerShell host you want to measure. For example, start PowerShell
7 when comparing ModuleFast behavior on PowerShell 7.

## Select A Matrix

The benchmark spec declares the available cases, engines, and operations. Use
runner filters when you want a focused matrix:

| Filter | Example |
| --- | --- |
| `-Scenario` / `-Case` | `SingleModule, AzAccounts` |
| `-Operation` | `Find, Install, Save` |
| `-Engine` | `Managed, ModuleFast, PSResourceGet, PowerShellGet` |

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

Use `ModuleFastPath` to pin the released ModuleFast lane to a specific local
module path instead of resolving `ModuleFast` from `PSModulePath`.

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

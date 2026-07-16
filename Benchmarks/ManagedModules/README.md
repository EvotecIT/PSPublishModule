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
    -RunMode local `
    -Variable @{ UpdateReadme = $true }
```

That comparison expands the standard scenario set across the lifecycle
operations and provider engines:

- `SingleModule`: `PSScriptAnalyzer`
- `GraphAuthentication`: `Microsoft.Graph.Authentication`
- `Graph`: `Microsoft.Graph`
- `AzAccounts`: `Az.Accounts`
- `Az`: `Az`

The checked-in root README table is usually refreshed with a one-iteration full
matrix smoke and `UpdateReadme = $true` so the full matrix shape is proven
without multiplying the large Graph/Az download work by the normal warmup
policy. Focused runs do not modify the root README. For stable local numbers,
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
binary can be pinned with `ManagedModulePath`; validation compares the loaded
command's SHA-256 with the requested artifact, which also supports exact byte
validation when an external host copies the assembly before loading it. The
managed install intentionally uses its operation-local package buffer rather than a
persistent extracted-package cache because ModuleFast has no equivalent cache
in this lane. ModuleFast runs with `DestinationOnly`, so neither engine mutates
the process module path. Validation requires exactly one requested module
manifest with the exact requested version, and the artifacts record installed
file count and bytes alongside timing.

## ModuleFast 1.0.0-beta1 Results

These results answer two different questions. The controlled comparison uses an
identical local NuGet v3 feed to isolate installer behavior. The default-source
comparison intentionally races the normal product paths: PowerForge uses the
official PowerShell Gallery and ModuleFast uses `https://pwsh.gallery/index.json`.
The latter includes endpoint and CDN behavior, so it is useful real-world
evidence but not a pure installer-engine comparison.

### Controlled Identical-Feed Comparison

Both engines used the same local feed, exact packages, fresh destinations,
rotated order, and exact manifest/payload validation. PowerForge was built from
commit `ab1a417a3ba35dd266fe786b87ccefe05882d510` and ModuleFast was
`1.0.0-beta1`.

| Workload | Samples | PowerForge median | ModuleFast median | PowerForge wins |
| --- | ---: | ---: | ---: | ---: |
| PSScriptAnalyzer 1.25.0 | 10 | 640 ms | 757 ms | 7/10 pairs |
| Microsoft.Graph 2.29.1 | 6 | 3.04 s | 4.04 s | 4/6 pairs |
| Az 14.0.0 | 6 | 4.47 s | 7.98 s | 5/6 pairs |

### Default Sources: PSGallery Versus pwsh.gallery

Run `20260716-092623-ef4ac8ed` used PowerShell 7.6.3 on Windows
10.0.26200, six samples per engine, rotated order, no warmup, and no outlier
removal. On this machine and network, PowerForge won every scenario median even
while ModuleFast used pwsh.gallery.

| Workload | PowerForge median | ModuleFast median | PowerForge wins |
| --- | ---: | ---: | ---: |
| PSScriptAnalyzer 1.25.0 | 889 ms | 1.21 s | 5/6 pairs |
| Microsoft.Graph 2.29.1 | 6.90 s | 7.60 s | 4/6 pairs |
| Az 14.0.0 | 5.40 s | 8.19 s | 6/6 pairs |

Pinned benchmark artifacts:

| Product | Version | Binary SHA-256 |
| --- | --- | --- |
| PowerForge / PSPublishModule | `1.0.0+2ce9a46b512c6f859a00536704767e4fa813be88` | `9D74D285F3E2A878F4676EF2C52502DC842CCFAFE55093C45EEB3F86D595C905` |
| ModuleFast | `1.0.0-beta1` | `CC2F05A9D6C60D0FF5FBBEC7405B7C644F03BAB1EED5F3F58AF8D438657AF9E3` |

The requested root packages downloaded from both endpoints were byte-identical:

| Package | Bytes | SHA-256 |
| --- | ---: | --- |
| PSScriptAnalyzer 1.25.0 | 14,658,674 | `14E634C828EB98EFB9F40B2918BA90F139ED5ECCDF663A2A747736D996995D60` |
| Microsoft.Graph 2.29.1 | 17,463 | `29844C04C2B69C536691C868B9F787A785FF508B9795AE4638E71C5D6F3FA9C8` |
| Az 14.0.0 | 41,951 | `BE8743551FC08A71DB04056FE03D795DD99AEC377313B0D002F04860A5B34709` |

### Windows PowerShell 5.1

Run `20260716-093024-d11767ac` used the same PowerForge commit under
Windows PowerShell 5.1.26100.8655. All 18 measured PowerForge installs
succeeded. ModuleFast does not support this host, so one lane per scenario was
recorded as `Skipped`, not failed or silently omitted.

| Workload | Samples | PowerForge median | ModuleFast |
| --- | ---: | ---: | --- |
| PSScriptAnalyzer 1.25.0 | 6 | 1.12 s | Unsupported / skipped |
| Microsoft.Graph 2.29.1 | 6 | 4.56 s | Unsupported / skipped |
| Az 14.0.0 | 6 | 4.59 s | Unsupported / skipped |

The pinned net472 PSPublishModule binary SHA-256 was
`0619D590ABDA7B415D4204CBEA4FB283B1D6B4AD7D493E75B0AB1656637B944E`.
Each run's `metadata.json` records these versions and hashes under the
`benchmark.*` keys together with the source endpoints and comparison mode.

To reproduce the exact net472 lane, start Windows PowerShell 5.1, import the
net472 PSPublishModule binary, and use a short artifact root to stay within the
legacy host's path limits:

```powershell
Import-Module .\PSPublishModule\bin\Release\net472\PSPublishModule.dll -Force
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -OutputRoot "$env:TEMP\pf-managed-ps51" `
    -Scenario SingleModule, Graph, Az `
    -Operation Install `
    -Engine Managed, ModuleFast `
    -Host Desktop `
    -WarmupCount 0 `
    -IterationCount 6 `
    -Variable @{
        ManagedModulePath = (Resolve-Path .\PSPublishModule\bin\Release\net472\PSPublishModule.dll).Path
        ModuleFastPath = 'C:\Modules\ModuleFast\1.0.0\ModuleFast.psd1'
    }
```

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

Focused runs leave the root `README.MD` unchanged. To intentionally refresh its
declared benchmark block after a representative full-matrix run, pass
`-Variable @{ UpdateReadme = $true }`. To inspect the planned lanes without
running network or install work:

```powershell
Invoke-BenchmarkSuite -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 -Plan
```

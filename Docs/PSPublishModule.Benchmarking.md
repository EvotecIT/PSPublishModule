# PSPublishModule Benchmarking

PSPublishModule includes a reusable benchmark layer for PowerShell workflows and
BenchmarkDotNet output. It gives projects one place to define benchmark cases,
run measurements, import results, update Markdown tables, and verify baselines.

The benchmark commands are intended for contributor and CI evidence. Benchmark
numbers are machine-specific; publish the command, host, input data, and run
metadata with any table that is committed or shared.

## Commands

| Command | Purpose |
| --- | --- |
| `Invoke-BenchmarkSuite` | Runs a `.benchmark.ps1` suite, writes artifacts, updates declared README blocks, and returns a `BenchmarkRunResult`. |
| `Import-BenchmarkResult` | Imports BenchmarkDotNet CSV/JSON artifacts or normalized benchmark JSON/CSV into the common result schema. |
| `Update-BenchmarkDocument` | Replaces one marker-delimited Markdown block from a normalized summary or comparison file. |
| `Test-BenchmarkGate` | Verifies benchmark summary metrics against a JSON baseline with tolerance rules. |

## Benchmark Specs

PowerShell benchmark suites are authored in `.benchmark.ps1` files. A suite
declares cases, matrix axes, engines, operations, optional validation, custom
metrics, comparison rules, README blocks, and artifact choices.

```powershell
benchmark 'managed-modules' -out 'Ignore/Benchmarks/ManagedModules' {
    cases {
        case PSScriptAnalyzer @{
            ModuleName = 'PSScriptAnalyzer'
            Version = '1.25.0'
            AcceptLicense = $false
        }
    }

    axis Operation Find, Install, Save
    axis Host Current

    setup {
        param($case, $run)
        $run.RepositoryName = 'PSGallery'
    }

    engine Managed {
        operation Find {
            param($case, $run)
            Find-ManagedModule -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        operation Install {
            param($case, $run)
            Install-ManagedModule -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryName `
                -ModuleRoot $run.OutputDirectory `
                -AcceptLicense:$case.AcceptLicense `
                -Force | Out-Null
        }
    }

    validate {
        param($case, $run)
        if (-not (Test-Path -LiteralPath $run.OutputDirectory)) {
            throw 'Expected benchmark output was not created.'
        }
    }

    comparison Engine -Baseline Managed -Metric MedianMs
    readme 'README.MD' -Block 'managed-module-benchmark-table' -Renderer ComparisonTable
    artifacts Json, Csv, Markdown
}
```

Benchmark declaration words are real PSPublishModule commands. The shorter DSL
keywords are aliases over the explicit command names, so a spec can use a compact
Pester-style form while still being backed by normal PowerShell parameter
binding.

| Short form | Explicit form |
| --- | --- |
| `benchmark` | `New-BenchmarkSuite` |
| `cases` | `Add-BenchmarkCases` |
| `case` | `Add-BenchmarkCase` |
| `caseSource` | `Add-BenchmarkCaseSource` |
| `axis` | `Add-BenchmarkAxis` |
| `setup` | `Set-BenchmarkSetup` |
| `data` | `Set-BenchmarkDataFactory` |
| `profile` | `Set-BenchmarkProfile` |
| `cleanup` | `Set-BenchmarkCleanup` |
| `engine` | `Add-BenchmarkEngine` |
| `operation` | `Add-BenchmarkOperation` |
| `skip` | `Add-BenchmarkSkipRule` |
| `validate` | `Add-BenchmarkValidation` |
| `metric` | `Add-BenchmarkMetric` |
| `comparison` | `Add-BenchmarkComparison` |
| `readme` | `Add-BenchmarkReadmeBlock` |
| `artifacts` | `Set-BenchmarkArtifacts` |

The managed-module provider comparison in
`Benchmarks/ManagedModules/managed-modules.benchmark.ps1` is intentionally a
normal benchmark spec. It keeps PSPublishModule-specific module scenarios,
provider command mappings, native-provider install safety, validation, and
managed-result metrics in the benchmark file while the reusable runner,
artifact, comparison, profile, and README update mechanics stay in PowerForge.

## Running A Suite

Run a suite from a file:

```powershell
Invoke-BenchmarkSuite -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1
```

Inspect the resolved work without executing measurements:

```powershell
Invoke-BenchmarkSuite -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 -Plan
```

Override common runner settings from the command line:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -OutputRoot .\Ignore\Benchmarks\ManagedModules `
    -WarmupCount 1 `
    -IterationCount 5 `
    -RunMode local
```

Select a focused matrix from the command line:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\ManagedModules\managed-modules.benchmark.ps1 `
    -Scenario SingleModule, AzAccounts `
    -Operation Find, Install, Save `
    -Engine Managed, ModuleFast, PSResourceGet
```

`-Scenario` is an alias for `-Case`. The runner applies `-Case`, `-Engine`,
`-Operation`, and `-Host` after the spec is declared, so benchmark files do not
need to parse comma-separated strings or duplicate matrix-selection logic.

`Invoke-BenchmarkSuite` returns the full run result. The run contains raw
samples, summary rows, comparison rows, metadata, and artifact paths.

## What Is Timed

Only the operation script block is measured. The runner executes lifecycle blocks
in this order:

1. expand cases and axes into work items
2. evaluate skip rules
3. run `setup`
4. run the configured data factory
5. run warmup iterations
6. time the selected `operation`
7. run `validate`
8. capture custom `metric` values
9. write samples, summaries, comparisons, metadata, and requested artifacts

Setup, data generation, validation, and metric capture are outside the timed
operation. Validation and metric failures are still recorded as failed benchmark
samples so fast but invalid output is visible.

## Profiles And Cleanup

`profile` selects runner behavior. Supported profile values are:

| Profile | Behavior |
| --- | --- |
| `Current` | Runs in the current PowerShell process. |
| `TemporaryLocalUser` | Runs a file-backed suite inside a temporary Windows local user profile. |

`TemporaryLocalUser` is useful for commands that install into the current user
profile. The runner creates a temporary local user, grants access to the spec,
working directory, output root, requested README files, and required module
assemblies, runs the child PowerShell process with a loaded profile, imports the
result, then removes the account and scratch folder according to the cleanup
mode.

Cleanup modes:

| Cleanup mode | Behavior |
| --- | --- |
| `Always` | Remove the temporary user, profile, and scratch folder after the run. |
| `KeepOnFailure` | Keep the temporary profile and scratch folder when the run fails. |
| `KeepAlways` | Keep temporary state for inspection. |

Example:

```powershell
benchmark 'native-install' -out 'Ignore/Benchmarks/NativeInstall' {
    profile TemporaryLocalUser -Cleanup KeepOnFailure
    # cases, engines, and operations
}
```

`TemporaryLocalUser` requires `Invoke-BenchmarkSuite -Path`; inline `-Settings`
blocks cannot be re-evaluated inside the child user profile.

## Output Artifacts

The default artifact layout is:

```text
<OutputRoot>/<run-id>/
  samples.json
  samples.csv
  summary.json
  summary.csv
  comparison.json
  comparison.md
  metadata.json
  run-report.json
```

Use ignored output roots such as `Ignore/Benchmarks/...` or `Build/Benchmarks/...`
for local runs. Commit only curated summaries or README blocks that are meant to
be public evidence.

## Importing Results

Import normalized benchmark artifacts:

```powershell
Import-BenchmarkResult -Path .\Build\Benchmarks\latest\run-report.json
```

Import BenchmarkDotNet artifacts:

```powershell
Import-BenchmarkResult `
    -Path .\BenchmarkDotNet.Artifacts `
    -Suite dotnet-benchmarks `
    -OutputPath .\Build\Benchmarks\normalized.json
```

The importer preserves BenchmarkDotNet method/job identity, timing units,
memory/statistical metrics, and user parameters where they do not conflict with
the normalized benchmark schema.

## Markdown Blocks

Benchmark document updates use marker-delimited blocks. Only the content between
markers is replaced.

```markdown
<!-- managed-module-benchmark-table:start -->
generated content
<!-- managed-module-benchmark-table:end -->
```

Update a summary table:

```powershell
Update-BenchmarkDocument `
    -Path .\README.MD `
    -BlockId managed-module-benchmark-table `
    -SummaryPath .\Build\Benchmarks\summary.json `
    -Renderer SummaryTable
```

Update a comparison table:

```powershell
Update-BenchmarkDocument `
    -Path .\README.MD `
    -BlockId managed-module-benchmark-table `
    -ComparisonPath .\Build\Benchmarks\comparison.json `
    -Renderer ComparisonTable
```

The updater fails when the target document or marker block is missing.

## Gates

`Test-BenchmarkGate` verifies normalized summary rows against a JSON baseline.
The default metric is `MedianMs`, and the default grouping keeps suites,
scenarios, operations, engines, hosts, operating systems, and variables separate.

Verify a baseline:

```powershell
Test-BenchmarkGate `
    -SummaryPath .\Build\Benchmarks\summary.json `
    -BaselinePath .\Build\Benchmarks\baseline.json `
    -Metric MedianMs `
    -RelativeTolerance 0.15 `
    -AbsoluteToleranceMs 50
```

Update a baseline intentionally:

```powershell
Test-BenchmarkGate `
    -SummaryPath .\Build\Benchmarks\summary.json `
    -BaselinePath .\Build\Benchmarks\baseline.json `
    -Update
```

The gate fails for failed summary rows, missing requested metrics, duplicate
group keys, non-finite values, and regressions outside the configured tolerance.

## Result Schema

Raw samples include:

- run id
- suite
- scenario
- operation
- engine
- host
- operating system
- run mode
- iteration
- status
- duration in milliseconds
- failure reason
- variables
- custom metrics

Summary rows aggregate samples by suite, scenario, operation, engine, host,
operating system, run mode, status, and variables. They include sample counts,
failure counts, median/mean/min/max duration, and custom metrics.

Comparison rows compare one dimension against a baseline value for one or more
metrics. They are used by README comparison tables and by reviewable benchmark
evidence.

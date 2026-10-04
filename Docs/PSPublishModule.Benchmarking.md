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
| `Start-BenchmarkProvenanceCapture` / `Complete-BenchmarkProvenanceCapture` | Bind a fresh external benchmark artifact directory to clean, unchanged Git source state and per-file hashes. |
| `Import-BenchmarkResult` | Imports BenchmarkDotNet CSV/JSON artifacts or normalized benchmark JSON/CSV into the common result schema. |
| `Merge-BenchmarkEvidenceCatalog` | Verifies and consolidates portable evidence bundles produced by independent platform runners. |
| `Update-BenchmarkEvidenceCatalog` | Records an independent Windows, Linux, or macOS result lane and exposes missing or incompatible platform evidence. |
| `Update-BenchmarkDocument` | Replaces one marker-delimited Markdown block from a normalized summary or comparison file. |
| `Test-BenchmarkGate` | Verifies benchmark summary metrics against a JSON baseline with tolerance rules. |
| `Test-BenchmarkHistory` | Calibrates and verifies duration limits from accepted independent runs on the same runner environment. |

## Runner-Specific Timing History

Use timing history in a manual, scheduled, or explicitly enabled performance job.
Keep ordinary correctness checks independent of shared-runner timing variation.
`Test-BenchmarkHistory` consumes `run-report.json`, including raw samples, from
the benchmark runner. It does not accept a summary as proof of a complete run.

Accept a known healthy run explicitly:

```powershell
Test-BenchmarkHistory -ResultPath ./run-report.json -HistoryPath ./history.json `
    -WorkloadId topology-fixture-v1 -RunnerIdentity dedicated-renderer -Update
```

Repeat acceptance for at least five independent runs, each with at least five
successful measured iterations per lane. Then verify a new run without `-Update`:

```powershell
Test-BenchmarkHistory -ResultPath ./run-report.json -HistoryPath ./history.json `
    -WorkloadId topology-fixture-v1 -RunnerIdentity dedicated-renderer
```

Choose a workload version or fixture hash that changes when the measured work
changes. Choose a stable runner identity for a dedicated machine or comparable
pool. OS, architecture, CPU, runtime, SDK, runner version, placement and measurement
policy, profile, cooldown, and each external host's actual affinity and priority
split calibration automatically. Windows, Linux and macOS histories may
share one file; they do not share duration thresholds. Missing placement metadata
cannot establish that processor placement was controlled.

Each lane uses the median of the most recent twenty accepted run medians. Its
upper limit adds the largest of `-RelativeTolerance` (default ten percent),
`-AbsoluteToleranceMs` (default zero), or three times the median absolute deviation
of those run medians. This is an observable noise allowance, not a statistical
confidence bound or a portable performance guarantee. Adjust tolerances only from
representative retained runs. The service retains all raw timings when deriving
a run median, including outliers, and keeps at most five hundred accepted runs.

Insufficient history returns `Calibrating = true` and `Passed = false`.
`-AllowCalibration` permits that initial state without a terminating error; a
regression or missing established lane still fails. Failed, skipped, duplicated
or incomplete measured iterations cannot enter history. Verification never writes
the file and can read a complete snapshot while an accepted update replaces it.
`-Update` reports acceptance rather than a passing verification, supports
`-WhatIf`, and uses the shared lock and atomic writer. Do not automatically accept a
run after a failed gate. Keep the history file as a local or CI artifact alongside
the complete run reports used to calibrate it.

## Benchmark Specs

For Windows measurements that need fixed processor placement, declare
`Set-BenchmarkPolicy -ProcessorAffinityMask 0xFFFF -ProcessPriority Normal`
inside the suite, or pass the same parameters to `Invoke-BenchmarkSuite` to
override its policy. Choose a nonzero mask within the host process's current
affinity mask. These optional controls require a single Windows processor
group; execution rejects them on other platforms. `-Plan` can still expand
the suite on any platform.

Placement covers setup, warmup, measured operations and validation. The runner
restores the original affinity and priority when it exits, including failed
runs, and records requested, applied and original values in report metadata.
Child PowerShell hosts apply the same settings and the combined report retains
their host-specific values. Run placement-controlled benchmarks in a dedicated
host process: affinity and priority affect every thread in that process, and
the runner rejects overlapping placement controllers.

PowerShell benchmark suites are authored in `.benchmark.ps1` files. A suite
declares cases, matrix axes, engines, operations, optional validation, custom
metrics, comparison rules, README blocks, and artifact choices.

```powershell
New-BenchmarkSuite 'managed-modules' -OutputRoot 'Ignore/Benchmarks/ManagedModules' {
    Add-BenchmarkCases {
        Add-BenchmarkCase PSScriptAnalyzer @{
            ModuleName = 'PSScriptAnalyzer'
            Version = '1.25.0'
            AcceptLicense = $false
        }
    }

    Add-BenchmarkAxis Operation Find, Install, Save
    Add-BenchmarkAxis Host Current

    Set-BenchmarkSetup {
        param($case, $run)
        $run.RepositoryName = 'PSGallery'
    }

    Add-BenchmarkEngine Managed {
        Add-BenchmarkOperation Find {
            param($case, $run)
            Find-ManagedModule -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        Add-BenchmarkOperation Install {
            param($case, $run)
            Install-ManagedModule -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryName `
                -ModuleRoot $run.OutputDirectory `
                -AcceptLicense:$case.AcceptLicense `
                -Force | Out-Null
        }
    }

    Add-BenchmarkValidation {
        param($case, $run)
        if (-not (Test-Path -LiteralPath $run.OutputDirectory)) {
            throw 'Expected benchmark output was not created.'
        }
    }

    Add-BenchmarkComparison Engine -Baseline Managed -Metric MedianMs -TieTolerance 0.05
    Add-BenchmarkReadmeBlock 'README.MD' -Block 'managed-module-benchmark-table' -Renderer ComparisonTable
    Set-BenchmarkArtifacts Json, Csv, Markdown
}
```

Benchmark declarations use the full PSPublishModule command names. Generic
shorthand such as `setup`, `data`, or `case` is intentionally unsupported so
benchmark evaluation cannot shadow commands from other modules.

`-TieTolerance` is a fractional threshold for practical equivalence. For
example, `0.05` labels results within five percent of the fastest successful
engine as tied. Omitting it preserves exact ranking.

The managed-module provider comparison in
`Benchmarks/ManagedModules/managed-modules.benchmark.ps1` is intentionally a
normal benchmark spec. It keeps PSPublishModule-specific module scenarios,
provider command mappings, native-provider install safety, validation, and
managed-result metrics in the benchmark file while the reusable runner,
artifact, comparison, profile, and README update mechanics stay in PowerForge.

### Benchmark Inputs

Use `Get-BenchmarkInput` to read values supplied through
`Invoke-BenchmarkSuite -Variable`. Specs should not parse `$BenchmarkVariables`
or carry local string/int/bool helper functions.

```powershell
$server = Get-BenchmarkInput Server localhost
$rows = Get-BenchmarkInput RowCount 1000, 5000, 20000 -Int
$keepTables = Get-BenchmarkInput KeepTables -Bool

New-BenchmarkSuite 'sql-import' {
    Add-BenchmarkCaseSource {
        foreach ($rowCount in $rows) {
            [pscustomobject]@{ Name = "$rowCount rows"; RowCount = $rowCount }
        }
    }
}
```

Input helpers keep benchmark files focused on benchmark intent. Use `-Required`
when a value has no safe default.

### Benchmark Provenance

Use `Add-BenchmarkMetadata` for suite-specific provenance that should travel with every
artifact, such as the exact compared product versions, binary hashes, source
endpoints, or comparison mode:

```powershell
New-BenchmarkSuite 'installer-race' {
    Add-BenchmarkMetadata ComparisonMode 'DefaultSources'
    Add-BenchmarkMetadata ToolVersion '1.0.0-beta1'
    Add-BenchmarkMetadata ToolSha256 '0123456789ABCDEF'
}
```

Declared values are written to `metadata.json`, the returned run metadata, and
`run-report.json` with a `benchmark.` prefix. Duplicate names and empty values
are rejected so a benchmark cannot silently replace its own provenance.

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
    -Engine Managed, ModuleFast, PSResourceGet, PowerShellGet `
    -Host Core, Desktop
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
3. run `Set-BenchmarkSetup`
4. run the configured data factory
5. run warmup iterations
6. time the selected `Add-BenchmarkOperation` block
7. run `Add-BenchmarkValidation`
8. capture custom `Add-BenchmarkMetric` values
9. write samples, summaries, comparisons, metadata, and requested artifacts

Setup, data generation, validation, and metric capture are outside the timed
operation. Validation and metric failures are still recorded as failed benchmark
samples so fast but invalid output is visible.

## Profiles And Cleanup

`Set-BenchmarkProfile` selects runner behavior. Supported profile values are:

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
New-BenchmarkSuite 'native-install' -OutputRoot 'Ignore/Benchmarks/NativeInstall' {
    Set-BenchmarkProfile TemporaryLocalUser -Cleanup KeepOnFailure
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
memory/statistical metrics, user parameters, and typed host details such as the
operating system, CPU, architecture, runtime, SDK, and core counts. Directory
imports retain that environment instead of flattening it into an unidentified
combined result.

## Cross-Platform Evidence

Do not merge benchmark measurements from different operating systems into one
number. Import each run, attach exact workload provenance, and update the shared
catalog:

```powershell
$metadata = @{
    'benchmark.workload.id' = 'markpflug-65k-sales-v1'
    'benchmark.fixture.csv' = 'AC959F43...'
    'benchmark.package.officeimo' = '3.0.4'
    'benchmark.package.sylvan' = '0.5.7'
}
$capture = Start-BenchmarkProvenanceCapture `
    -SourceRoot . `
    -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts `
    -Metadata $metadata `
    -RunMode full

dotnet run -c Release --project .\Benchmarks -- `
    --artifacts .\Build\BenchmarkDotNet.Artifacts

$capture | Complete-BenchmarkProvenanceCapture

$result = Import-BenchmarkResult `
    -Path .\Build\BenchmarkDotNet.Artifacts `
    -Suite tabular-65k

Update-BenchmarkEvidenceCatalog `
    -InputObject $result `
    -Path .\Website\static\data\benchmarks\tabular\index.json `
    -ComparisonId markpflug-65k-sales-v1-net10.0 `
    -ResultPath windows-full.json `
    -RunMode full `
    -ExpectedPlatform windows,linux,macos `
    -Publish
```

Declare workload identity, fixture hashes, package versions, and run mode when
starting the capture. PowerForge stores those declarations in the sidecar and
applies them before sealing the imported result. Changing measurements or
provenance-bound metadata after import is rejected for publishable evidence.
Publishing also requires the sidecar-bound `benchmark.workload.id` to exactly
match the catalog comparison ID and the sidecar-bound run mode to exactly match
the requested lane. Diagnostic captures may omit those declarations, but they
cannot later be relabeled as public Full evidence.

Directory imports reject reports that identify more than one operating system.
External imports are publishable only when their fresh artifact directory has a
completed production sidecar; adding the current checkout SHA after a run is not
accepted as provenance. Catalog updates use a cross-process lease and
metadata-preserving same-directory replacement, write the exact validated
normalized result beside the catalog, and record its SHA-256, so concurrent
platform jobs cannot silently discard, substitute, or truncate another lane.
Each platform runner should write an isolated portable bundle: one catalog plus
its normalized result files in the same directory. Consolidate independently
produced bundles only after every runner finishes:

```powershell
Merge-BenchmarkEvidenceCatalog `
    -SourcePath `
        .\artifacts\windows\index.json, `
        .\artifacts\linux\index.json, `
        .\artifacts\macos\index.json `
    -Path .\Website\static\data\benchmarks\tabular\index.json `
    -ExpectedPlatform windows,linux,macos
```

The merge verifies every normalized result against the SHA-256 and lane metadata
recorded by its source catalog and rejects conflicting copies of the same lane.
Verified results are written beside the destination catalog under immutable,
content-addressed file names before the catalog is atomically switched. An older
catalog therefore continues to reference unchanged bytes even if a process stops
during publication. This is the cross-machine convergence step; the update lock
only coordinates writers that actually share one filesystem. Schema 1 catalogs
must first be updated by the current PowerForge version so legacy publish flags
are demoted and revalidated.

The catalog replaces only the matching comparison/platform/run-mode lane.
Windows, Linux, and macOS remain separate entries. `availability` lists missing
platforms explicitly. A publishable lane must contain successful measurements,
no failures, measured runtime and runner identity, and an exact `gitSha` from a
verified clean worktree; invalid evidence is rejected rather than appearing
available. Publishable lanes with different `gitSha` values or
different `benchmark.fixture.*`, `benchmark.package.*`, or
`benchmark.workload.*` metadata are marked non-comparable and carry the exact
conflicting dimensions.

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

## Operation memory measurements

The PowerShell runner records `AllocatedBytes` around the operation handler on
modern .NET using the process-wide managed allocation counter. Setup, data
preparation, configured memory cleanup, validation and metric callbacks are outside
this interval. Host invocation and allocations on other managed threads in the
same process are included. Run evidence on an idle host; the counter measures
allocated bytes, not live objects or retained memory. .NET Framework reports a
missing allocation observation because it has no equivalent counter.

`WorkingSetDeltaBytes` is the signed difference between process working-set
observations around the operation. It can be negative, includes native and managed
resident pages, and is neither peak memory nor retained-memory evidence. Unavailable
process observations remain missing rather than being reported as zero.

Failed operations retain observations captured before failure; validation failures
retain the completed operation's measurements. Setup failures have no operation
measurement. Summary metrics contain the arithmetic mean of successful observations
only when every successful sample has that counter. `Test-BenchmarkGate -Metric
AllocatedBytes` can compare allocation summaries with a declared baseline; its
absolute tolerance is expressed in the selected metric's units. Keep these
machine-dependent gates in opt-in evidence runs.

### Sampled operation memory

Use `Set-BenchmarkPolicy -MemorySamplingIntervalMilliseconds 5` or the same
`Invoke-BenchmarkSuite` override for opt-in memory observations. Zero disables
sampling by default; valid enabled intervals are 1 through 1000 milliseconds.
The runner observes managed-heap estimates and process resident pages at the
operation boundaries and on a background thread during the operation. Startup,
shutdown, setup, configured collection and validation stay outside elapsed timing.
The observer itself allocates and perturbs scheduling; keep these runs separate
from uninstrumented timing comparisons.

Raw sample metrics retain `BaselineManagedHeapBytes`, `SampledMaxManagedHeapBytes`,
`SampledManagedHeapDeltaBytes`, `MemorySampleCount` and `MemorySamplingIntervalMs`.
`GC.GetTotalMemory(false)` estimates managed heap bytes without forcing collection;
it can include objects awaiting collection. Available resident observations add
`BaselineWorkingSetBytes`, `SampledMaxWorkingSetBytes`, `SampledWorkingSetDeltaBytes`
and `WorkingSetSampleCount`. Missing resident observations remain absent rather
than zero. Failed operations retain their observations. An unexpected observer
failure fails the sample and records `MemorySamplingFailed`.

The maxima are the largest observed values, not guaranteed peaks. Short operations
may have only the two boundary observations, and scheduler delays can miss
transients between samples. Resident values include managed and native pages;
they do not isolate native allocations or prove retained native memory. Read the
sample counts and raw values, retain slower cases, and qualify any budget on its
actual host/workload. Summary values are means of per-operation observations.
The sampling metric names are reserved only when sampling is enabled.

### Collected managed-heap observations

`PowerForge.BenchmarkManagedMemoryProbe` supports explicit retained-memory evidence.
Construct it in setup after releasing previous results; call `Capture()` after
validation and after releasing the current results. Both boundaries force garbage
collection and wait for pending finalizers, outside the timed operation. The result
contains `BaselineBytes`, `CollectedBytes` and a signed `DeltaBytes`. Repeated calls
compare against the same original baseline; a negative delta is meaningful and is
not clamped away.

Record these values as custom benchmark metrics. They measure the whole process's
live managed heap, including host state, caches and concurrent managed activity.
They do not measure native allocations, peak memory or prove a leak in one operation.
Warm up the workload, keep runs isolated, and compare equivalent repeated runs before
choosing a budget. Full collection can disturb other work in the process; do not use
this probe in normal application or correctness timing paths.

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

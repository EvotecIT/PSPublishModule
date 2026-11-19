# PSPublishModule Benchmarking

PSPublishModule includes a reusable benchmark layer for PowerShell workflows and
BenchmarkDotNet output. It gives projects one place to define benchmark cases,
run measurements, import results, update Markdown tables, and verify baselines.

The benchmark commands are intended for contributor and CI evidence. Benchmark
numbers are machine-specific; publish the command, host, input data, and run
metadata with any table that is committed or shared.

Comparisons against other libraries follow the
[benchmark standard](PowerForge.BenchmarkStandard.md): equivalent work with
validated output, a wide scenario matrix, fair runs, honest statistics and licence
classes for every library.

## Commands

| Command | Purpose |
| --- | --- |
| `Invoke-BenchmarkSuite` | Runs a `.benchmark.ps1` suite, writes artifacts, updates declared README blocks, and returns a `BenchmarkRunResult`. |
| `Start-BenchmarkProvenanceCapture` / `Complete-BenchmarkProvenanceCapture` | Bind a fresh external benchmark artifact directory to clean, unchanged Git source state and per-file hashes. |
| `Import-BenchmarkResult` | Imports BenchmarkDotNet CSV/JSON artifacts or normalized benchmark JSON/CSV into the common result schema. |
| `Merge-BenchmarkEvidenceCatalog` | Verifies and consolidates portable evidence bundles produced by independent platform runners. |
| `Update-BenchmarkEvidenceCatalog` | Records one lane (comparison, platform, framework, profile) and applies the publish policy per cell. |
| `Test-BenchmarkDeclaration` | Lints the library policy and comparison declarations, and checks a result for declaration drift. |
| `Select-BenchmarkLibrary` | Selects the libraries a run may execute from the library policy, with explicit licence acceptance. |
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

The PowerShell runner records the CLI SDK selected by `dotnet --version` from
the benchmark source directory, including its `global.json` policy. An absent
SDK or unresolved pin leaves this field empty. This describes the selected
toolchain, not the build provenance of an externally supplied DLL; retain
assembly hashes and build metadata separately when measuring such inputs.

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

Timing history separates reports with different `operationTimingBoundary`
metadata and different enabled memory-sampling intervals. Measurements of the
guarded operation body require their own accepted references; earlier measurements
that included invocation setup cannot qualify that cohort. Legacy reports without
the boundary field retain their existing identity.

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

Guard preparation, native-exit tracker setup, and DSL alias installation finish
before timing starts. Timing stops in `finally` before the wrapper checks exit
codes and restores caller state. Native failures still fail the sample, and an
operation that throws retains its elapsed duration. Reports record this boundary
as `operationTimingBoundary = GuardedScriptBodyV1`.

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

For JSON exports that encode the job in `DisplayInfo`, the engine identity includes
the job name and settings. Jobs with different settings stay separate even when
they share a name. The importer reads the exporter's ampersand-separated parameter
fields while preserving punctuation and empty values in that format. When
`DisplayInfo` cannot be matched, the parameters come from `FullName`
(`Bench.Merge(Scale: Easy, Producer: IText)`), but only when they reproduce the
exported `Parameters` text exactly.

Every row carries the library it measured. BenchmarkDotNet methods name it with a
category, `[BenchmarkCategory("library:ClosedXML")]`, or the import declares a
method-to-library map; the library is never guessed from method or job names.
When a map is given, every method must resolve or the import fails. When each
library has its own method for the same workload, `[BenchmarkCategory("case:ReadTyped")]`
or `-CaseMap` names the shared case; the method name is kept as the `Method` variable
and is not part of the measured cell:

```powershell
Import-BenchmarkResult `
    -Path .\BenchmarkDotNet.Artifacts `
    -LibraryMap @{ ReadOfficeImo = 'OfficeIMO'; ReadClosedXml = 'ClosedXML' } `
    -ValidationPath .\Build\validation.json `
    -AcceptLicense EPPlus
```

`-ValidationPath` reads the output validation the benchmark performed before
timing: a JSON array of `{ "library": "...", "case": "...", "status": "passed", "detail": "..." }`
(`case` is optional and `status` is `passed` or `failed`). `-AcceptLicense` records
the libraries whose licence you accepted for this run. Normalized results and
CSV files carry `library`, `validations` and `acceptedLibraries` directly. The
importer also records the runner block from BenchmarkDotNet: warmup and measured
iteration counts from the JSON measurements or the job settings, launches, and the
runner version.

BenchmarkDotNet job characteristics (`Runtime`, `IterationCount`, `Affinity`, ...)
are never case variables. CSV imports record their shared values once as
`benchmarkdotnet.job.<Name>` metadata. Statistic columns after `Mean`, such as
`Error` or `Alloc Ratio`, are metrics or dropped, even when their value is `NA`;
columns named like statistics before `Mean` stay parameters.

## Cross-Platform Evidence

Do not merge benchmark measurements from different operating systems into one
number. Import each run, attach exact workload provenance, and record it in the
shared schema 5 catalog:

```powershell
$metadata = @{
    'benchmark.workload.id' = 'xlsx-read-typed'
    'benchmark.workload.framework' = 'net10.0'
    'benchmark.fixture.xlsx' = 'AC959F43...'
    'benchmark.package.closedxml' = '0.105.0'
    'benchmark.package.closedxml.sha256' = '5D0A...'
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
    -ValidationPath .\Build\validation.json

Update-BenchmarkEvidenceCatalog `
    -InputObject $result `
    -Path .\Website\static\data\benchmarks\excel\index.json `
    -ComparisonId xlsx-read-typed `
    -ResultPath windows-net10.0-full.json `
    -Profile full `
    -DeclarationPath .\Benchmarks\benchmarks.json `
    -Publish
```

A lane is keyed by comparison, platform, framework and profile, plus lane
parameters such as `-LaneParameter @{ affinity = '0xffff0000' }` (or the affinity
recorded in the runner block). Framework and affinity are never part of the
comparison id; an id such as `xlsx-read-typed-net10.0` is rejected. The framework
comes from `-Framework`, the `benchmark.workload.framework` metadata, or the
measured .NET runtime.

Profiles replace the older run modes. `diagnostic` (formerly `quick`, still
accepted by `-Profile`/`-RunMode`) is never published. `standard` is publishable
from any runner and `full` from a complete BenchmarkDotNet run; both are held to
the catalog's publish policy:

- lane: clean `gitSha`, OS, architecture, CPU, logical cores, runtime and SDK; the
  runner block with at least 3 warmups; no machine or user name; a declared,
  eligible comparison without declaration drift; pinned package versions and
  hashes for every non-subject library.
- cell: at least 10 measured samples, a 95% confidence interval of the median,
  passed output validation, a library whose licence class may be published, and
  no failed samples.

A lane that fails a lane rule is rejected. A cell that fails a cell rule stays in
the catalog with its reason and is left out of public claims; the lane records
`completeness: partial`. Failed and skipped cells, such as a library that crashed,
are kept the same way, so one crash never discards the rest of a run. Write each
comparison as soon as it is measured. Cells whose confidence intervals overlap the
fastest cell's interval are marked `even`.

Declare workload identity, fixture hashes, package versions, and run mode when
starting the capture. PowerForge stores those declarations in the sidecar and
applies them before sealing the imported result. Changing measurements or
provenance-bound metadata after import is rejected for publishable evidence.
Publishing also requires the sidecar-bound `benchmark.workload.id` to exactly
match the catalog comparison ID and the sidecar-bound run mode to exactly match
the requested profile.

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
    -Path .\Website\static\data\benchmarks\excel\index.json `
    -DeclarationPath .\Benchmarks\benchmarks.json `
    -ExpectedPlatform windows,linux,macos
```

`-ResultPath` is what readers use to find the result. When it is root-relative
(`/data/benchmarks/x.json`), absolute or a URL, also pass `-ResultArtifactPath` with
the local file to write; without it the update fails rather than writing to a
drive root. A relative `-ResultPath` is written beside the catalog.

`Update-BenchmarkEvidenceCatalog` writes each result under a content-addressed name
(`windows-full.<sha256>.json`) and records that path, so a superseded run's file is
never overwritten. Every update also re-evaluates the lanes already in the catalog
against the current declarations and policy, and refuses the update if a published
lane no longer meets them.

A merge keeps the publish policy of its source catalogs unless `-Policy` is given,
and fails when the sources disagree. When the declarations restrict libraries, every
row must carry its library, so redaction can tell which rows to remove.

The merge verifies every normalized result against the SHA-256 and lane metadata
recorded by its source catalog, re-evaluates every lane against the declarations
and publish policy, and rejects conflicting copies of the same lane. Verified
results are written beside the destination catalog under immutable,
content-addressed file names before the catalog is atomically switched. When two
bundles carry different runs of the same lane, the newest becomes the lane and the
older one is kept in `superseded` with its date and hash. Catalogs and declaration
files from earlier schemas are rejected; record fresh runs instead.

Windows, Linux, and macOS remain separate entries. `availability` lists every
declared comparison, framework and expected platform, and only published lanes
make a platform available. Published lanes with different `gitSha` values or
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

Update a block from published evidence in a schema 5 catalog. `-Comparison` takes
comparison ids or group names; each comparison gets a table of its published run on
the platform (and framework, when given), with the workload parameters shared by
every case, the head-to-head lines, the statistic, the runner settings and a note for
each library whose results are not published, giving the licence or the policy reason:

```powershell
Update-BenchmarkDocument `
    -Path .\README.MD `
    -BlockId excel-read `
    -Catalog .\Website\static\data\benchmarks\index.json `
    -Comparison read `
    -Platform windows `
    -Framework net10.0
```

A full run is preferred over a standard one, then the newest. A comparison without
a published run on that platform shows a "no published run yet" line instead of a
table, so a README never quotes diagnostic or unpublished numbers.

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
- library
- host
- operating system
- run mode
- iteration
- status
- duration in milliseconds
- failure reason
- variables
- custom metrics

Summary rows aggregate samples by suite, scenario, operation, engine, library,
host, operating system, run mode, status, and variables. They include sample
counts, failure counts, median/mean/min/max duration, and custom metrics.

A result also carries its runner block (`kind`, `version`, `statistic`, `warmups`,
`iterations`, `launches`, `ordering`, `inProcess`, `affinity`, `priority`), the output
`validations` performed before timing, and the `acceptedLibraries` whose licence was
accepted for the run. `Invoke-BenchmarkSuite` records each engine as the row's
library and its own runner block (`powershell-suite`), and no longer writes the
machine or user name into result metadata.

Comparison rows compare one dimension against a baseline value for one or more
metrics. They are used by README comparison tables and by reviewable benchmark
evidence.

### Declared comparison coverage

Schema 5 declaration documents hold the library policy and the comparisons
(`Schemas/powerforge.benchmark-declarations.schema.json`). Each library has `id`,
`package`, a pinned `version`, `license` (SPDX or `LicenseRef-*`), optional
`licenseUrl`, a `restriction` class (`open`, `publish-restricted`,
`acceptance-required`, `excluded`) and a `reason`. Each comparison has
`comparisonId`, `area`, `group`, `title`, `subject`, `libraries`, `axes`, `cases`,
`expectedPlatforms` and `frameworks`, plus an optional `eligibility` that can only
exclude it and optional `cells` for comparisons where not every case × library ×
axis-value combination is valid:

```json
"cells": [
  { "case": "Create", "parameters": { "Scale": "Normal" } },
  { "case": "Create", "library": "OfficeIMO", "parameters": { "Scale": "Huge" } }
]
```

Catalogs derive each comparison's eligibility: a single-library comparison is
declared but never publishable, and so is a comparison without declared `cases` or
`cells`, because its runs can't be checked for missing combinations. Its lanes are
recorded and shown with that reason, and publishing them is refused. Restricted
libraries do not block a comparison; the published lane drops their cells, which
carry the issue "Not published (licence)", and publishes the rest.

Generate the document from the benchmark code and check it in CI:

```powershell
Test-BenchmarkDeclaration -Path ./Benchmarks/benchmarks.json
Import-BenchmarkResult ./artifacts | Test-BenchmarkDeclaration -Path ./Benchmarks/benchmarks.json -ComparisonId xlsx-read-typed
$selection = Select-BenchmarkLibrary -Path ./Benchmarks/benchmarks.json -AcceptLicense EPPlus
```

The first lints the document. The second reports drift between a result and its
declaration: cases, libraries or axis values that ran but are not declared, and
declared cases or axis values with no cell at all. When the comparison declares its
cases, it also checks every case × library × axis-value combination for each library
that ran (`missing-combination`). When the comparison lists explicit `cells`, those
cells define the expected combinations (`missing-combination`,
`unexpected-combination`) and, without `cases`, the case names; only the cases and
axis values of cells for the libraries that ran are required. A case that ran and crashed is a
failed cell, not drift; it is excluded from publication and the rest of the lane is
published. Both stop with an error unless `-PassThru` is used.
`Select-BenchmarkLibrary` returns the libraries a runner may execute; excluded
libraries never run and acceptance-required libraries run only when named in
`-AcceptLicense`.

Pass the declaration document when merging or updating. To update a subset,
include the existing destination catalog in `-SourcePath` alongside the incoming
bundles. Unchanged lanes and their immutable artifacts survive. With
`-DeclarationPath`, that document is authoritative; otherwise declarations are
merged from source catalogs and conflicting definitions fail before output
replacement. Result payloads and their hashes remain unchanged.

Use `-ResultPathPrefix /data/benchmarks/library-comparisons` when consolidating
portable runner bundles into a website. Result links use that root-relative
public directory, while content-addressed artifacts are written beside the output
catalog.

### Comparison views for websites

`BenchmarkComparisonViewBuilder` turns a schema 5 catalog and the results beside it
into a presentation view: for every declared comparison, every run with each
contender and case side by side, the relative time to the fastest contender, wins,
and the declared subject against each competitor. Rows that carry a library are
shown under that library. Rows without one are matched to the declared libraries
ignoring case and punctuation, so
`MagickNETEncode` belongs to `Magick.NET`; a suffix shared by two or more
libraries (`Encode`) becomes the case's operation, and one used by a single
library (`Parallel` in `OfficeIMOEncodeParallel`) names a variant. Results imported
before job settings were separated are normalized when the view is built, so
published evidence does not need to be measured again.

PowerForge.Web exposes the view to templates from `site.json`:

```json
{
  "BenchmarkEvidence": [
    { "Name": "benchmarks", "Catalog": "static/data/benchmarks/library-comparisons/index.json" }
  ]
}
```

Templates then read `data.benchmarks.comparisons`, each with `runs`, `overview`,
`preferredRunId`, and per run `contenders`, `cases[].cells`, `headToHead` and
`environment.text`. Times, allocations and ratios come with display text
(`timeText`, `allocatedText`, `relative.text`), so templates do no arithmetic.
`preferredRunId` names the run to show by default: a published run before an
unpublished one, then full before standard, then Windows, then the newest.

Each comparison also carries the declared facts a page needs to explain what it
shows:

- `group` and `frameworks` from the declaration;
- `eligibility` (`publishable`, `reason`), for example a single-library comparison
  or one without declared cases;
- `availability`, one entry per expected platform and framework, with text such as
  `Windows · net10.0 · standard · 2026-11-18`, `Linux · net10.0 · no published run`
  (measured, but only in runs that are not published) or `macOS · net10.0 · not measured`.
  It names only profiles the publish policy publishes;
- `libraryStates`, each library's restriction class and whether its results are
  published, with the note `not published (licence)` when they are not.

Each run adds `profile` and `profileText`, `framework`, `laneParameters`,
`runner.text` (kind, statistic, warmups, iterations, launches, ordering, process
isolation, affinity and priority), `completeness`, `comparable`,
`compatibilityIssues`, `policyIssues` and `notPublished`, which lists libraries
measured in the lane whose cells were withheld and why.

Large catalogs can be split so a page loads an area only when it is opened. With
`Output`, the build writes `index.json` (areas, their groups and comparison
summaries with headline results) and one `<area>.json` per area under that
directory of the site output. `IndexOnly` gives templates just the index, which
keeps every page's data small:

```json
{
  "BenchmarkEvidence": [
    {
      "Name": "benchmarks",
      "Catalog": "static/data/benchmarks/library-comparisons/index.json",
      "Output": "data/benchmarks",
      "IndexOnly": true
    }
  ]
}
```

Templates then read `data.benchmarks.areas`, each with `id`, `file`, `groups`,
`measured` and `comparisons[].headlines`, and fetch `/data/benchmarks/<file>` for
the full comparisons. Headlines come only from a published run, so a comparison
measured only in diagnostic or unpublished runs has none. `IndexOnly` requires `Output`, and `Output` must stay inside
the site output.

Each cell also carries `interval` (`level`, `lowerMs`, `upperMs` and `text`, such as
`95% CI 10.1 ms – 10.8 ms`) when the evidence records one, and `even`, which is true
when its interval overlaps the fastest cell's interval in that case.

### Website components

PowerForge.Web ships Scriban partials that render the view, so every site presents
benchmarks the same way. They need no files in the theme:

| Partial | Argument | Renders |
| --- | --- | --- |
| `powerforge/benchmarks/comparison` | one comparison | Title, group, platform availability, a platform and framework switcher, and the published runs, followed by licence notes |
| `powerforge/benchmarks/run` | a run and its comparison | Run facts, environment, workload, the case table with times, confidence intervals and "fastest", "even" or the ratio, head-to-head lines, withheld libraries with their reason (never their times) and runner settings |
| `powerforge/benchmarks/area` | an area file's content, or in area pages the area's index entry | Every comparison of one area; in area pages `summaries` lists them and `comparisonsHtml` holds them already rendered |
| `powerforge/benchmarks/index` | the `BenchmarkEvidence` data | Areas with comparison headlines, and each area's results on demand |

Only published runs are shown. A comparison without one shows "No published run
yet.", platforms are listed as "not measured" or "no published run", and a
comparison that cannot be published shows the reason. When cells have confidence
intervals, only the fastest cell is "fastest" and a cell whose interval overlaps it
is "even"; cells without intervals use the 0.5% tolerance. The index tells a
comparison with no run, one measured only in unpublished runs and one whose
published run has no head-to-head result apart, using each summary's `measured`
and `published` flags.

To adopt them in a site:

1. Declare the evidence in `site.json`. For a large catalog, add `IndexOnly` (with
   `Output`) so pages carry only the index, and `AreaFragments` so the index loads
   each area's rendered results when it is opened:

   ```json
   {
     "BenchmarkEvidence": [
       {
         "Name": "benchmarks",
         "Catalog": "static/data/benchmarks/index.json",
         "Output": "data/benchmarks",
         "IndexOnly": true,
         "AreaFragments": true
       }
     ]
   }
   ```

2. Include a component in a Scriban layout or partial. A summary page with every
   area:

   ```html
   {{ include "powerforge/benchmarks/index" data.benchmarks }}
   ```

   or one comparison, when the full view is loaded (no `IndexOnly`):

   ```html
   {{ for c in data.benchmarks.comparisons }}
     {{ if c.id == "excel-read" }}{{ include "powerforge/benchmarks/comparison" c }}{{ end }}
   {{ end }}
   ```

3. Build the site. A page that renders a component gets
   `assets/powerforge/benchmarks.v1.css` and `assets/powerforge/benchmarks.v1.js`
   added to its head and body; the build refuses to write them through a linked
   `assets/powerforge` folder or file. With `AreaFragments`, the build also writes
   each area as a small page to
   `_powerforge/fragments/benchmarks/<name>-<hash>/<area>.html`. The folder adds a
   hash of the exact `Name`, so entries such as `benchmarks.csv` and `benchmarks-csv`
   never share one, and two entries that would still share a folder fail the build
   before anything is written. The `area` component, or the theme's replacement,
   renders the page; the comparisons are rendered one at a time beforehand and
   passed to it as `comparisonsHtml`, so an area of any size stays within the
   template loop limit. Site audits, sitemaps and search already skip
   `_powerforge/fragments`, and the page is marked `noindex`.
   The script resolves these files from the site root it was loaded from, so a site
   served from a sub-path works unchanged.

Without the script, the platform switcher stays hidden, every published run is
shown in turn and "Show results" opens a link to the area page, which has its own
styles; without `AreaFragments`, an index over the full view renders areas
inline and an `IndexOnly` index lists the summaries. Themes restyle the components
through the `--pf-bench-*` CSS properties (accent, even, failed, border, radius),
or replace one by adding `partials/powerforge/benchmarks/<name>.html`; the build
then uses the theme's version, including for the area files. The partials
HTML-escape every value from the evidence.


# PSPublishModule Benchmarking Design

This note proposes a reusable benchmark layer for EvotecIT projects. It is a design
pass only: it does not change the benchmark scripts in PR #476, OfficeIMO,
PSWriteOffice, CodeMatrix, or other consumers.

## Why This Exists

Benchmarks are useful, but the current benchmark code is spread across repo-local
scripts that all solve the same problems again:

- build or prepare the code under test
- expand scenario, engine, host, OS, row-count, and mode matrices
- time PowerShell or .NET work
- collect environment metadata
- keep failed competitor lanes visible
- aggregate raw samples into comparison rows
- update Markdown or website data between marker comments
- gate regressions against a baseline

The repeated script shape makes every benchmark useful but expensive to maintain.
The public surface should follow PSPublishModule naming conventions, with
consumer repos owning only scenario definitions and product-specific setup.

## Current Inventory

### Existing Benchmark Gate Support

The dotnet publish pipeline already has `DotNetPublishBenchmarkGate` and
`DotNetPublishBenchmarkMetric`. The existing feature extracts metrics from JSON
or text logs, compares them with a baseline, applies tolerances, and reports gate
results in the publish pipeline.

That is a good gate layer, but it is not a benchmark runner and it does not
standardize how benchmark results are generated, summarized, published, or
inserted into README files.

### PSPublishModule PR #476

The managed module benchmark scripts in PR #476 are a good example of the current
pain:

- `Measure-ManagedModuleBenchmark.ps1` defines scenarios, imports providers,
  creates isolated module paths, runs stopwatch measurements, records CSV rows,
  and owns provider-specific command calls.
- `Invoke-ManagedModuleBenchmarkMatrix.ps1` launches current, PowerShell 7, and
  Windows PowerShell hosts, prepares provider module copies, assembles command
  strings, and handles scratch folders.
- `Update-ManagedModuleBenchmarkReadme.ps1` parses CSV, computes medians,
  renders a Markdown table, and replaces a marker-delimited README block.

Those responsibilities should not live in each benchmark folder.

### PSWriteOffice

`Benchmarks/Compare-ExcelPerformance.ps1` is a large PowerShell benchmark harness.
It owns dependency installation, PSWriteOffice build/import, test data creation,
scenario definitions, validation, measurement, CSV/JSON output, summary rows,
comparison rows, environment metadata, and operator output.

The domain-specific scenario bodies should stay in PSWriteOffice. The matrix,
measurement, artifact schema, comparison summary, metadata, and document update
logic should move into the shared benchmark layer.

### OfficeIMO

OfficeIMO uses BenchmarkDotNet for some .NET benchmarks and a custom lightweight
C# runner for large Excel comparisons. It also has PowerShell report-generation
scripts that turn comparison output into Markdown, website JSON, and generated
HTML partials.

BenchmarkDotNet should remain the measurement engine for C# microbenchmarks. A
shared benchmark layer should import BenchmarkDotNet outputs into the common
EvotecIT benchmark schema and then handle comparison summaries, gates, README
sections, and website data.

### CodeMatrix / CodeGlyphX

CodeMatrix uses BenchmarkDotNet projects and already updates `BENCHMARK.md`
between marker blocks such as `<!-- BENCHMARK:WINDOWS:QUICK:START -->`.
The update contract is useful, but the implementation is repo-specific and the
report generator carries a lot of BenchmarkDotNet parsing, JSON index handling,
website-copy behavior, and Markdown assembly.

The shared benchmark layer should keep the marker-update idea and remove the
repo-local parser and template sprawl.

### External References

- [EliteLoser/Benchmark](https://github.com/EliteLoser/Benchmark) is a compact
  Measure-Command-based module around script blocks and loop counts.
- [StartAutomating/Benchpress](https://github.com/StartAutomating/Benchpress)
  provides a nice PowerShell vocabulary: benchmark files, techniques, repeat
  counts, checkpointed outputs, and Markdown-oriented display.
- [BenchmarkDotNet](https://benchmarkdotnet.org/) is the right engine for
  publication-grade .NET method benchmarks and should be integrated, not
  replaced.

These are useful references, not target APIs to clone.

## Design Goals

- Keep consumer benchmark code tiny: declare scenarios, provide setup/measure/
  validate script blocks or BenchmarkDotNet project paths, and choose reports.
- Use BenchmarkDotNet for C#/.NET benchmark execution when the benchmark is a
  .NET method or benchmark project.
- Provide a native PowerShell runner for PowerShell workflows, cmdlets, module
  installs, file-format workflows, and integration-style comparisons where
  BenchmarkDotNet is not the right shape.
- Normalize all benchmark outputs into one schema before summarizing, publishing,
  gating, or updating docs.
- Make README and website updates marker-driven, deterministic, UTF-8 no BOM,
  and safe by default.
- Keep failures as data when a competitor or optional lane cannot run.
- Separate quick engineering feedback from publishable benchmark evidence.
- Avoid local absolute paths in committed artifacts unless intentionally kept as
  machine-local evidence.

## Ownership Boundary

### Shared Engine Layer

Own host-agnostic models and reusable behavior:

- benchmark run/spec/result models
- BenchmarkDotNet result import
- sample aggregation and comparison summaries
- baseline/gate evaluation
- Markdown block replacement
- artifact path normalization
- JSON/CSV/Markdown writers
- deterministic report metadata

### PowerShell Host Layer

Own PowerShell-host concepts:

- PowerShell scenario execution
- runspace or process isolation
- `pwsh` and Windows PowerShell host selection
- `PSModulePath` and temporary module roots
- script-block lifecycle invocation
- PowerShell module/package dependency preparation
- PowerShell-specific environment metadata

### PSPublishModule Public Surface

Expose thin cmdlets only:

- parameter binding
- `ShouldProcess` where files are updated
- PowerShell-friendly output objects
- calls into the shared benchmark engine and PowerShell host services

### Consumer Repos

Own only:

- benchmark scenario meaning
- product-specific setup and validation
- competitor-specific command calls
- intentional fixture data
- repo-specific report placement and README block IDs

## Proposed Public Surface

### Cmdlets And Authoring Commands

Names are provisional, but the shape should stay thin and match the module's
existing public style:

- `Invoke-BenchmarkSuite`
  - Runs a PowerShell benchmark spec or a BenchmarkDotNet project entry.
  - Writes raw samples, normalized results, summaries, and optional reports.
- `Import-BenchmarkResult`
  - Imports BenchmarkDotNet CSV/JSON/Markdown outputs or existing EvotecIT
    benchmark JSON/CSV into the common schema.
- `Update-BenchmarkDocument`
  - Replaces marker-delimited Markdown blocks from a normalized summary.
- `Test-BenchmarkGate`
  - Evaluates normalized benchmark metrics against a baseline with tolerances.
- `.benchmark.ps1` authoring keywords:
  - `benchmark`, `cases`, `case`, `from`, `axis`, `setup`, `data`, `engine`,
    `operation`, `skip`, `validate`, `metric`, `compare`, `readme`, and
    `artifacts`.

The short keywords should be aliases or DSL-scoped helper names. The long names
should be normal PowerShell command names so scripts can opt into the explicit
form when readability or tooling matters:

| Short form | Long command |
| --- | --- |
| `benchmark` | `New-BenchmarkSuite` |
| `cases` | `Add-BenchmarkCases` |
| `case` | `Add-BenchmarkCase` |
| `from` | `Add-BenchmarkCaseSource` |
| `axis` | `Add-BenchmarkAxis` |
| `setup` | `Set-BenchmarkSetup` |
| `data` | `Set-BenchmarkDataFactory` |
| `engine` | `Add-BenchmarkEngine` |
| `operation` | `Add-BenchmarkOperation` |
| `skip` | `Add-BenchmarkSkipRule` |
| `validate` | `Add-BenchmarkValidation` |
| `metric` | `Add-BenchmarkMetric` |
| `compare` | `Add-BenchmarkComparison` |
| `readme` | `Add-BenchmarkReadmeBlock` |
| `artifacts` | `Set-BenchmarkArtifacts` |

The explicit form of the common engine block would be:

```powershell
Add-BenchmarkEngine -Name Managed -ScriptBlock {
    Add-BenchmarkOperation -Name Find -ScriptBlock {
        param($case, $run)
        Find-Module -Name $case.ModuleName -Repository $run.RepositoryName
    }
}
```

The short keywords should be local to `.benchmark.ps1` evaluation, so normal
PowerShell sessions are not polluted. Public module exports should favor the
long commands plus aliases where they are genuinely useful.

Typed builder commands such as `New-ConfigurationBenchmarkSuite` can exist as a
lower-level model/serialization API, but they should not be the normal human
authoring surface.

The existing dotnet publish benchmark gate cmdlets can remain as adapters or move
to the common gate engine once the generic model exists.

### Command Entry Points

The initial design should be PSPublishModule command-first:

```powershell
Invoke-BenchmarkSuite -Path .\Benchmarks\benchmark.ps1 -Suite standard
Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -OutputPath .\Build\Benchmarks\normalized.json
Update-BenchmarkDocument `
    -Path .\README.MD `
    -BlockId managed-module-benchmark-table `
    -SummaryPath .\Build\Benchmarks\summary.json
Test-BenchmarkGate -SummaryPath .\Build\Benchmarks\summary.json -BaselinePath .\Build\Benchmarks\baseline.json
```

A CLI wrapper can come later if there is a real consumer that cannot import the
module, but it should wrap the same engine and should not define a separate
branded benchmark vocabulary.

## PowerShell Benchmark DSL

PowerShell script blocks cannot live naturally in JSON, so the PowerShell-first
authoring surface should be a `.benchmark.ps1` file. JSON remains useful for
generated config and output, but it should not be the normal way to write a
PowerShell benchmark.

The authoring surface should be low ceremony. The typed `New-Configuration*`
objects can exist underneath for serialization, validation, CLI, and tests, but
benchmark authors should not have to write them by hand.

The benchmark spec must make three things visible:

- the case data that is expanded into benchmark runs
- the axes that turn those cases into a matrix
- the operation handler that executes a specific case

The matrix should not be a bag of strings where the benchmark engine guesses what
code to run. A reader should be able to answer: "for this scenario, operation,
and engine, which script block runs?"

Preferred authoring shape:

```powershell
benchmark 'managed-modules' -out 'Ignore/Benchmarks/ManagedModules' {
    cases {
        case SingleModule @{
            Label = 'PSScriptAnalyzer'
            ModuleName = 'PSScriptAnalyzer'
            Version = '1.25.0'
            AcceptLicense = $false
        }

        case GraphAuthentication @{
            Label = 'Graph.Authentication'
            ModuleName = 'Microsoft.Graph.Authentication'
            Version = '2.38.0'
            AcceptLicense = $true
        }
    }

    axis Operation Find, Install, Save
    axis Host Current, PowerShell7, WindowsPowerShell

    setup {
        param($case, $run)
        # Prepare provider modules, isolated module roots, and temp paths.
    }

    engine Managed {
        operation Find {
            param($case, $run)
            Find-ManagedModule -Name $case.ModuleName -Repository $run.Repository | Out-Null
        }

        operation Install {
            param($case, $run)
            Install-ManagedModule -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.Repository `
                -ModuleRoot $run.InstallRoot `
                -AcceptLicense:$case.AcceptLicense `
                -Force | Out-Null
        }

        operation Save {
            param($case, $run)
            Save-ManagedModule -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.Repository `
                -Path $run.SaveRoot `
                -AcceptLicense:$case.AcceptLicense `
                -Force | Out-Null
        }
    }

    engine PSResourceGet {
        operation Find {
            param($case, $run)
            Find-PSResource -Name $case.ModuleName -Repository $run.RepositoryName | Out-Null
        }

        operation Install {
            param($case, $run)
            Install-PSResource -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryName `
                -Scope CurrentUser `
                -TrustRepository `
                -AcceptLicense:$case.AcceptLicense | Out-Null
        }

        operation Save {
            param($case, $run)
            Save-PSResource -Name $case.ModuleName `
                -Version $case.Version `
                -Repository $run.RepositoryName `
                -Path $run.SaveRoot `
                -TrustRepository `
                -AcceptLicense:$case.AcceptLicense | Out-Null
        }
    }

    validate {
        param($case, $run)
        # Optional and not timed. For example, verify output exists in the isolated root.
    }

    compare Engine -baseline Managed -metric MedianMs
    readme 'README.MD' -block 'managed-module-benchmark-table' -renderer ComparisonTable
}
```

This is the shape the user writes. The loader can lower it into typed models such
as `BenchmarkSuite`, `BenchmarkCase`, `BenchmarkAxis`, `BenchmarkEngine`,
`BenchmarkOperation`, and `BenchmarkReport`.

Advanced scenarios should not require abandoning the DSL. The same file should
support generated cases, filters, custom metrics, and per-case data:

```powershell
benchmark 'excel-workflows' -out 'Ignore/Benchmarks/ExcelPerformance' {
    cases {
        from {
            foreach ($scenario in Get-ExcelBenchmarkScenarioCatalog) {
                $scenario
            }
        }
    }

    axis Rows 1000, 10000, 25000
    axis Engine PSWriteOffice, ImportExcel, ExcelFast
    axis Mode Write, Read, RoundTrip

    skip {
        param($case)
        $case.Engine -eq 'ExcelFast' -and $case.RequiresPivotTables
    }

    data {
        param($case, $run)
        New-ExcelBenchmarkData -Rows $case.Rows -Profile $case.Profile
    }

    engine PSWriteOffice {
        operation Write {
            param($case, $run)
            $run.Data | Export-OfficeExcel -Path $run.OutputPath -Table
        }
    }

    engine ImportExcel {
        operation Write {
            param($case, $run)
            $run.Data | Export-Excel -Path $run.OutputPath -TableName Data
        }
    }

    metric FileSizeBytes {
        param($case, $run)
        (Get-Item -LiteralPath $run.OutputPath).Length
    }

    compare Engine -baseline PSWriteOffice -metric MedianMs, FileSizeBytes
    artifacts Json, Csv, Markdown
}
```

The runner should have a plan mode that prints the resolved work before anything
is measured:

```powershell
Invoke-BenchmarkSuite -Path .\Benchmarks\managed-modules.benchmark.ps1 -Plan
```

The plan should show each expanded case, host, engine, operation, output path,
and selected handler. Missing handlers are a plan-time error, not a surprise
halfway through a long run.

The implementation can still expose lower-level typed cmdlets for generated
configs, tests, and JSON round-tripping. They should be the engine API, not the
normal authoring experience.

Previous over-ceremonial shape to avoid:

```powershell
New-ConfigurationBenchmarkSuite -Name 'managed-modules' {
    New-ConfigurationBenchmarkScenario -Name 'module-gallery-operation'
    New-ConfigurationBenchmarkEngine -Name 'Managed'
}
```

That style is useful for generated config, but it is too noisy for a human
benchmark file.

The benchmark runner owns the lifecycle around the handlers:

1. expand each case from declared cases and axes
2. apply `skip` rules
3. validate that every `(Engine, Operation)` pair has a declared handler
4. create the requested host/isolation context
5. call `setup` and `data` outside the timed block
6. time only the bound operation handler
7. call `validate` and custom metrics outside the timed block
8. record raw samples, failure rows, metadata, summaries, reports, and gates

The consumer still writes the real product operation. It does not own timing
loops, metadata, summary aggregation, Markdown replacement, or output schemas.

## Measurement Model

PowerShell runner defaults:

- `WarmupCount`: default `1`
- `IterationCount`: default `3` for quick runs, higher for publish runs
- `Order`: rotate scenario/engine order by iteration to reduce first-run bias
- `Timer`: `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime()` where
  available, falling back to `Stopwatch`
- `GarbageCollection`: opt-in, per scenario or suite
- `Validation`: after the measured block, never included in measured duration
- `FailurePolicy`: record failed rows by default; fail the suite when configured
- `Isolation`: `None`, `Runspace`, `Process`, or `ProcessPerIteration`
- `Host`: `Current`, `PowerShell7`, `WindowsPowerShell`, or explicit executable

For C# benchmarks, the shared benchmark layer should not reinvent statistical
measurement. It should run or import BenchmarkDotNet and normalize its
artifacts.

## Common Result Schema

Raw sample row:

```json
{
  "runId": "20260630-120000-abc123",
  "suite": "managed-modules",
  "scenario": "GraphAuthentication",
  "operation": "Install",
  "engine": "Managed",
  "host": "PowerShell 7",
  "os": "windows",
  "runMode": "quick",
  "iteration": 2,
  "status": "Succeeded",
  "durationMs": 1234.56,
  "allocatedBytes": null,
  "workingSetDeltaBytes": null,
  "outputMetric": null,
  "reason": "",
  "variables": {
    "moduleName": "Microsoft.Graph.Authentication",
    "version": "2.38.0"
  }
}
```

Summary row:

```json
{
  "suite": "managed-modules",
  "scenario": "GraphAuthentication",
  "operation": "Install",
  "engine": "Managed",
  "host": "PowerShell 7",
  "sampleCount": 3,
  "status": "Succeeded",
  "medianMs": 1234.56,
  "meanMs": 1240.12,
  "minMs": 1210.0,
  "maxMs": 1280.0,
  "standardDeviationMs": 35.4,
  "ratioToBest": 1.0,
  "ratioToBaselineEngine": 1.0,
  "baselineEngine": "Managed"
}
```

Run metadata:

- git commit, branch, dirty state when discoverable
- PowerShell version and edition
- .NET SDK/runtime when relevant
- OS description and architecture
- processor count
- benchmark config path and suite
- output paths
- dependency versions captured by configured probes

## Marker-Driven Document Updates

Use one generic marker contract:

```markdown
<!-- powerforge-benchmark:managed-module-benchmark-table:start -->
generated content
<!-- powerforge-benchmark:managed-module-benchmark-table:end -->
```

Rules:

- block IDs are case-insensitive but written in lower-case examples
- updates fail when the block is missing unless an explicit insert policy is set
- only the content between markers is replaced
- writes are atomic and UTF-8 no BOM
- generated content includes a short caveat that benchmark data is machine
  specific
- renderer output is deterministic so diffs stay small

For compatibility, the updater should also support existing marker aliases, such
as CodeMatrix's `BENCHMARK:WINDOWS:QUICK` and PR #476's
`managed-module-benchmark-table`, so consumers can migrate without a noisy README
rewrite.

## Reports And Artifacts

Default output layout:

```text
Build/Benchmarks/<suite>/<run-id>/
  samples.json
  samples.csv
  summary.json
  summary.csv
  comparison.json
  comparison.md
  metadata.json
  run-report.json
```

Consumer configs may choose an ignored path such as `Ignore/Benchmarks/...` for
local-heavy evidence or a committed path such as `Docs/benchmarks/...` for small,
curated evidence.

The shared benchmark layer should know which artifacts are generated and which
are intended to be committed. Heavy run folders should remain ignored by default.

## Gates And Baselines

The generic benchmark gate should reuse the current DotNet publish gate ideas:

- baseline mode: `Verify` or `Update`
- relative tolerance
- absolute tolerance
- `FailOnNew`
- policy on regression
- policy on missing metric

The difference is input: gates should read normalized benchmark summary rows, not
only project-specific JSON paths or regex captures.

Example:

```powershell
Test-BenchmarkGate `
    -SummaryPath .\Build\Benchmarks\managed-modules\latest\summary.json `
    -BaselinePath .\Build\Benchmarks\managed-modules\baseline.json `
    -Metric 'medianMs' `
    -GroupBy suite,scenario,operation,engine,host `
    -RelativeTolerance 0.15 `
    -AbsoluteToleranceMs 50
```

DotNet publish can continue to expose `BenchmarkGates[]`, but the implementation
should eventually call the same generic gate service.

## Engine Upgrade Design

The next upgrade should improve the benchmark engine itself, not add
scenario-specific switches to the engine. A command option such as
`-NoPSModulePathUpdate` belongs inside one operation handler when that operation
needs it. The shared benchmark layer should instead make it easy to declare,
run, compare, validate, publish, and gate benchmark evidence consistently across
PowerShell, .NET, and repo-specific workflows.

### Capability Model

The benchmark engine should treat a run as five separate contracts:

- **Plan contract**: cases, axes, engines, operations, hosts, and resolved work
  items. This answers what will run before anything is measured.
- **Lifecycle contract**: setup, data, warmup, measure, validate, metric capture,
  cleanup, and failure recording. This defines what is timed and what is not.
- **Metric contract**: duration and custom numeric metrics with unit, direction,
  aggregation, and formatting metadata.
- **Comparison contract**: how summary rows are grouped, which dimension is being
  compared, which baseline value is used, and how missing or failed rows are
  handled.
- **Evidence contract**: raw samples, summaries, comparisons, metadata, artifact
  paths, and document updates that can be reproduced or audited later.

Keeping these contracts separate prevents the engine from becoming a collection
of benchmark-specific knobs.

### Profiles

Profiles should describe runner behavior, not product command-line flags. They
are useful because a quick local benchmark, a CI gate, and a publishable benchmark
need different repeat counts and artifact policies.

Example authoring shape:

```powershell
benchmark 'excel-workflows' -out 'Ignore/Benchmarks/ExcelPerformance' {
    profile Quick -Warmup 1 -Iterations 3 -Artifacts Json,Csv
    profile Publish -Warmup 3 -Iterations 10 -Artifacts Default -Isolation Process

    # cases, axes, engines, operations, reports, and gates
}
```

`Invoke-BenchmarkSuite -Profile Quick` should select those runner settings. When
no profile is specified, the suite should keep today's explicit/default values.

Profile-owned settings:

- warmup count
- measured iteration count
- isolation mode
- artifact set
- failure policy
- optional randomization or rotation policy
- optional output retention policy
- optional metadata probes

Operation-owned settings:

- command-specific flags
- repository names
- paths needed by the operation
- product-specific setup
- validation rules for the product output

This lets scenarios stay honest without making the benchmark engine know about
installer, document, database, or website-specific flags.

### Metric Definitions

Duration should remain the default metric, but custom metrics need more structure
than a name and script block. A metric definition should carry:

- name
- unit, such as `ms`, `bytes`, `files`, `rows`, `ops/s`, or `count`
- direction, usually `LowerIsBetter` or `HigherIsBetter`
- aggregation, such as `Median`, `Mean`, `Min`, `Max`, `Sum`, or `Last`
- format string or renderer hint
- optional description for generated docs

Example:

```powershell
metric FileSizeBytes -Unit bytes -Direction LowerIsBetter -Aggregate Mean {
    param($case, $run)
    (Get-Item -LiteralPath $run.OutputPath).Length
}

metric RowsPerSecond -Unit 'rows/s' -Direction HigherIsBetter -Aggregate Median {
    param($case, $run)
    $case.Rows / ($run.DurationMs / 1000)
}
```

The runner can keep accepting the current simple `metric Name { ... }` form and
lower it into a metric definition with safe defaults.

### Comparison Specs

Comparison should be a reusable service over normalized summary rows. It should
not require a suite to have just finished running. Imported BenchmarkDotNet
results, normalized CSV, and PowerShell runner output should all be comparable
through the same contract.

Comparison definition:

- dimension, initially `Engine`, later any structural or variable field
- baseline value
- metrics
- group-by fields
- missing-baseline policy
- missing-current policy
- failed-row policy
- direction override when metric metadata is not available

Default grouping should keep comparable lanes separate:

```text
Suite, Scenario, Operation, Host, OS, Variables
```

Example DSL:

```powershell
compare Engine `
    -Baseline PSWriteOffice `
    -Metric MedianMs, FileSizeBytes `
    -GroupBy Suite,Scenario,Operation,Host,OS,Variables `
    -MissingBaseline Fail `
    -FailedRows Fail
```

Example command form:

```powershell
Compare-BenchmarkResult `
    -SummaryPath .\Build\Benchmarks\latest\summary.json `
    -Dimension Engine `
    -Baseline PSWriteOffice `
    -Metric MedianMs, FileSizeBytes `
    -OutputPath .\Build\Benchmarks\latest\comparison.json
```

`Compare-BenchmarkResult` would be thin over the same core service used by
`Invoke-BenchmarkSuite`.

### Validation And Status

Validation must remain outside measured time, but validation failures should be
first-class benchmark data. A benchmark can be fast and still invalid. The status
model should make that visible.

Recommended status handling:

- setup failure records a failed sample before measurement
- warmup failure records a failed warmup/sample row and prevents measured samples
  for that work item
- operation failure records a failed measured sample
- validation failure records a failed measured sample with the measured duration
  retained when available
- metric failure records a failed sample unless the metric is explicitly optional
- skipped rows stay visible in samples and summaries

The current `Succeeded`, `Failed`, and `Skipped` statuses can remain, but result
rows should preserve a reason category such as `Setup`, `Warmup`, `Operation`,
`Validation`, `Metric`, or `Skip`. That lets reports distinguish "slow" from
"did not produce valid evidence".

### Run Metadata And Manifest

Every run should write a manifest that makes comparison claims inspectable. The
current metadata dictionary is a good start; the upgrade should formalize it into
stable keys and optional sections.

Baseline metadata:

- run id
- suite name and suite file path
- selected profile
- invocation command when available
- git branch, commit, and dirty state
- OS description and architecture
- process architecture
- PowerShell edition and version
- .NET runtime and SDK version when available
- processor count
- culture
- runner version
- output root and artifact paths

Optional probes:

- module versions
- package versions
- executable versions
- repository/source metadata
- fixture hashes or generated data description

The manifest should avoid committing machine-specific absolute paths unless the
artifact is explicitly local evidence. Reports can show friendly summaries while
the full manifest remains in JSON.

### Report Renderers

Report rendering should be data-driven and independent of the runner. A renderer
should take summary/comparison rows plus renderer options. It should not know how
the benchmark was executed.

Initial renderers:

- `SummaryTable`: one row per summary lane
- `ComparisonTable`: baseline, actual, ratio, and winner/regression hints
- `RegressionTable`: gate-focused output for changed or failed metrics
- `LatestRun`: small run metadata block for README or docs
- `MatrixTable`: grouped view for multi-axis benchmark families

Renderer options should include:

- metrics to show
- group-by fields
- sort fields
- maximum rows
- status filter
- ratio precision
- unit display
- whether to include machine-specific caveats

Markdown block updates should keep using marker replacement. Website renderers
can consume the same normalized JSON instead of parsing Markdown.

### Planning And Explainability

Plan mode should become the primary way to understand a benchmark before running
it. It should return structured work items and a readable table.

The plan should include:

- suite
- scenario
- engine
- operation
- host
- OS lane when known
- variables
- handler source location when available
- output directory/path
- selected profile
- skip prediction when static skip data is available
- missing handler or unsupported host errors before execution

This directly addresses the "what code is executed for this matrix row?"
problem.

### Public Surface Additions

The current public surface can stay. The upgrade should add only surfaces that
remove real friction:

- `Compare-BenchmarkResult`
  - standalone comparison over imported or freshly generated summary rows
- profile support in `.benchmark.ps1`
  - short form `profile`
  - explicit form `Add-BenchmarkProfile`
- richer metric support
  - optional `-Unit`, `-Direction`, and `-Aggregate`
- renderer options on `readme` / `Add-BenchmarkReadmeBlock`
- optional `-Profile` on `Invoke-BenchmarkSuite`

Everything else should remain an engine model or service until more than one
consumer needs it.

### Implementation Sequence

The upgrade should land in small slices:

1. Add metric metadata and comparison specs to core models.
2. Move comparison grouping/direction/missing-policy logic into a reusable
   comparison service.
3. Add `Compare-BenchmarkResult` over normalized summaries.
4. Add profile model and DSL support, then wire `Invoke-BenchmarkSuite -Profile`.
5. Formalize run metadata keys and write the manifest through the existing JSON
   artifact path.
6. Extend Markdown renderers with renderer options and unit-aware metric output.
7. Add reason categories for failed samples without breaking the existing status
   enum.
8. Improve plan output with handler/source/profile information.

Each slice should keep cmdlets thin and put reusable behavior in `PowerForge` or
`PowerForge.PowerShell`.

## Migration Plan

### Phase 1: Import, Summarize, Update

Build the host-agnostic pieces first:

- common models
- BenchmarkDotNet importer
- CSV/JSON importer for existing EvotecIT outputs
- summary/comparison writer
- Markdown block updater
- gate service over normalized summaries
- thin cmdlets, with a CLI wrapper only when a real consumer needs it

This phase can reduce CodeMatrix and OfficeIMO report scripts without touching
scenario execution.

### Phase 2: PowerShell Runner

Add PowerShell-host runner support:

- suite/scenario DSL
- matrix expansion
- warmup/repeat/rotation
- process/runspace isolation
- host selection
- setup/measure/validate/cleanup lifecycle
- dependency/version probes

This phase targets PSWriteOffice and the managed module benchmarks from PR #476.

### Phase 3: Build Pipeline Integration

Add benchmark steps to existing build/release flows:

- `.benchmark.ps1` suite config as a build/module/dotnet publish step
- build/module/dotnet publish hooks
- generated run reports
- CI baseline gates
- optional website benchmark data components

At this point, benchmark runs become first-class build evidence instead of
bespoke scripts.

## Migration Targets

### PR #476

Do not fix this PR in-place as part of this design. Later, replace the three
managed module benchmark scripts with:

- one benchmark spec file
- small scenario operation functions if needed
- one README marker block declaration
- optional baseline JSON

The module-state PR can then keep benchmark evidence without owning a benchmark
framework.

### PSWriteOffice

Keep the workbook/CSV scenario bodies and validation logic. Move the following
into the shared benchmark layer:

- suite defaults
- matrix expansion
- repeat policy
- timing and failure recording
- summary/comparison outputs
- metadata capture
- README update

### OfficeIMO

Keep BenchmarkDotNet projects for C# microbenchmarks. Normalize BenchmarkDotNet
artifacts through the shared importer. For the custom Excel comparison runner,
either emit the common schema directly or import its current JSON into the
common schema as an interim bridge.

Generated website HTML should eventually move to the website/docs renderer or
HtmlForgeX components fed by benchmark JSON, not stay as large PowerShell string
assembly.

### CodeMatrix

Keep the BenchmarkDotNet benchmark project. Replace `Generate-BenchmarkReport.ps1`
with:

- BenchmarkDotNet import
- normalized summary/comparison output
- marker-block update
- website data export

Preserve existing `BENCHMARK:OS:MODE` markers through alias support for a small
first migration.

## Non-Goals

- Do not replace BenchmarkDotNet for C# benchmark methods.
- Do not adopt an external PowerShell benchmark module as-is.
- Do not force all repos to commit heavy benchmark artifacts.
- Do not hide product-specific benchmark scenario code in the shared benchmark layer.
- Do not treat benchmark numbers as universal performance claims.

## First Implementation Slice

The first useful implementation is now the in-process benchmark path:

1. Shared benchmark models and summary/gate services.
2. `BenchmarkDocumentUpdater` with marker aliases.
3. `BenchmarkDotNetResultImporter` for CSV/JSON artifacts.
4. Thin cmdlets:
   - `Invoke-BenchmarkSuite`
   - `Import-BenchmarkResult`
   - `Update-BenchmarkDocument`
   - `Test-BenchmarkGate`
5. `.benchmark.ps1` DSL evaluation with short keywords and explicit long forms
   such as `Add-BenchmarkEngine`.
6. Focused tests for marker replacement, summary grouping, ratio calculation,
   baseline tolerance, import, and DSL planning.

That slice can immediately clean up README and website update code while leaving
process-per-host isolation as the next runner hardening step.

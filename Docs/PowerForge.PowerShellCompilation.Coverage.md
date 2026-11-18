# PowerShell compiler measurements: 2026-11-18

These results describe two fixed external packets on Windows x64 with SDK
10.0.404. The public module probes and external assessment runner use
PowerShell 7.6.9. The semantic oracle uses the pinned
PowerShell 7.6.6 package.

An emitted unit is a complete CLR method in the final output. A smaller compiled
region inside retained source does not count as another emitted unit. A method
can still require hosted PowerShell operations, so emission does not establish
runtime-free execution.

| Packet | Complete emitted units | Complete emitted functions | Execution proof |
| --- | ---: | ---: | --- |
| Ten public Gallery modules | 436/753 (57.90%) | Not measured separately | 10/10 Hybrid artifacts built, imported, and passed an invocation probe |
| Seven external workloads | 130/196 (66.33%) | 130/183 (71.04%) | 1/7 complete workloads passed; ten helper functions in ten scenarios on original, built, and installed surfaces |

The external assessment discovers 155 source files, with zero parser errors and
zero acquisition/analysis failures. It records five promoted regions in retained
functions. The public packet records 317 retained source units, 626 runtime-routed
units, and zero promoted regions. Those probes exercise Hybrid artifacts in
PowerShell. The ratios describe the measured packets and their discovered units.

The generated [public baseline](../Benchmarks/PowerShellCompilation/Corpus/public-corpus-baseline.net10.json)
and [external baseline](../Benchmarks/PowerShellCompilation/Corpus/external-assessment-baseline.net10.json)
pin the packet hashes and counts. The September Gallery snapshot discovered 735 units. The current
compiler discovers 753 for the same packet, so each ratio uses its own census
denominator. Earlier documents and `m29a-*` files preserve development results. In particular, the five-module **545/565**
result from 2026-10-03 is a separate targeted maintainer packet and has not been
rerun here. It must not replace either external measurement above.

The same 753-unit packet rises from **114/753 (15.14%)** to **436/753 (57.90%)**:
322 additional complete methods, with no loss in another module. All ten artifact
probes pass. The complete-method counts by module are:

| Module | Before | After |
| --- | ---: | ---: |
| ImportExcel | 0/2 | 0/2 |
| InvokeBuild | 0/1 | 0/1 |
| Microsoft.PowerShell.Archive | 19/21 | 19/21 |
| Pester | 0/491 | 322/491 |
| platyPS | 42/43 | 42/43 |
| Pode | 0/1 | 0/1 |
| powershell-yaml | 14/15 | 14/15 |
| PSFramework | 38/176 | 38/176 |
| PSScriptAnalyzer | 1/2 | 1/2 |
| ThreadJob | 0/1 | 0/1 |
| **Total** | **114/753** | **436/753** |

A separate three-test Pester run also passes on the pinned PowerShell 7.6.6 host
against both the original and generated modules: three passed, zero failed, and
zero skipped on each surface. It exercises pipeline assertions, object-property
assertions, and an expected throwing script block. It does not execute every
emitted Pester method or replace the fixed packet's invocation probes.

## Why these forms were selected

The starting census reproduced 114/753 complete methods. Pester accounted for
491 units and emitted none. Its document-wide `source.type-definition`
diagnostic affected all 491 units and was the sole visible blocker for 331.
PSFramework emitted 38/176; its overlapping type, syntax, and parameter-metadata
boundaries affected 68, 53, and 26 units respectively. The larger first gain was
therefore independent functions sharing a file with a hosted type, rather than
another isolated operator.

Removing that file-wide blocker exposed a shaping boundary: early command
discovery kept 475 Pester units hosted, with `function.graph` the sole remaining
visible blocker for 320. Native-bound Hybrid functions install at their authored
declaration statement, so those methods can preserve discovery timing without
eager command registration. Duplicate names, typed cmdlet hoisting, executable
entry rules, dependent authored types, and unsupported function metadata retain
their existing guards. These two frontier counts are sequential and overlap;
they are not additive coverage gains.

The emitted bodies use the existing PowerShell command and binding implementation for
pipeline stages, cmdlet invocation, advanced parameters, and
`begin`/`process`/`end`. A pinned 7.6.6 and installed 5.1 oracle compares original
and generated modules for zero/one/many records, by-value and by-property binding,
aliases and abbreviated names, validation errors and continuation, verbose
records, downstream stop and finally, repeated invocation, early discovery, and
retained class identity. General Strict pipelines and runtime-free authored
classes remain unsupported.

## Reproduce the measurements

Build the current CLI and choose a disposable scratch directory with enough room
for verified downloads and generated artifacts:

```powershell
dotnet build ./PowerForge.Cli/PowerForge.Cli.csproj -c Release -f net10.0
$cli = (Resolve-Path ./PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) 'PowerForgeCoverage'
./Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1 `
    -CliAssemblyPath $cli -WorkspacePath "$scratch/public" `
    -EvidencePath "$scratch/public.json" -PerformancePolicy RecordOnly -RefreshBaseline
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalAssessment.ps1 `
    -CliAssemblyPath $cli -WorkspacePath "$scratch/external" `
    -EvidencePath "$scratch/external.json" -ExecuteWorkloads -RefreshBaseline
```

Both commands acquire exact hashes from the checked-in packets. Public module
probes import generated Hybrid artifacts in clean child processes and invoke
their checked-in probes. Those artifacts can execute retained PowerShell source;
the ten probes do not separately compare original modules. External assessment
with `-ExecuteWorkloads` runs the reviewed complete-workload contracts; without
that option it performs acquisition and census only. `-RefreshBaseline` deliberately
records new measured counts. Omit it for comparison against the committed counts.
The public refresh requires the complete module packet on the baseline host.
It keeps the comparison to the previous Hybrid results and saves the run report
before replacing the Hybrid baseline. The Strict timing budgets remain in place.
Execution mode creates fresh projects and requires online package restore;
verified source payloads alone do not supply that environment. It cannot be
combined with `-Offline`. Census-only assessment can use a verified offline cache.

The complete external workload is the unchanged ten-function GitHub helper
library. Its contract covers SHA512 hashing, logging, invocation redaction,
deep copying, temporary and reusable directories, noninteractive output, path
resolution, an HTTP response, and error-record creation. Configuration and time
are fixed in the importing module; HTTP uses an owned loopback server. All ten
functions have complete CLR bodies and execute through Analyze/Lock/Restore/
Build/Test/Pack/Install, with the compiler input withdrawn before observing the
built and installed artifacts. The other six workloads have no complete
execution contract and are reported as unattempted. These scenarios do not
test live GitHub operations, exhaustive branches, or runtime-free execution.

## Semantic and performance limits

Windows compiler CI downloads and verifies the pinned PowerShell **7.6.6** archive,
sets `POWERFORGE_PWSH76_PATH` and
`POWERFORGE_REQUIRE_PINNED_SEMANTIC_HOSTS=true`, and selects the semantic oracle in
the normal compiler tests. The report records pass/fail/skip counts and refuses
skips. Local verification executed all **27 pinned tests with 27 passed and zero
skipped**; the complete selected suite passed **158 tests with zero skips**. The exact executable bytes and version remain pinned; the OS build belongs
to the runner. Windows PowerShell 5.1 is tested as the installed supported profile,
with its observed identity recorded. Its historical Windows component pin is not
a provisionable package for current hosted runner images.

All six small Strict programs built and passed on Windows x64 in this run. The [user guide](PowerForge.PowerShellCompilation.Guide.md)
contains two additional small Strict examples built and executed for this change.

The Strict startup measurements now include committed raw samples and runtime
locations. On Ubuntu 24.04 under WSL2, with both the executable and .NET on ext4,
the 2026-11-17 framework-dependent run measured **25 ms p50 / 26 ms p95**. The
2026-11-18 run measured **32/35 ms**, versus **242/278 ms** for source, using
SDK 10.0.403 and .NET 10.0.14. Both passed the existing **100/250 ms** budgets.
The later Windows x64 run measured **168/221 ms**, versus **757/1,570 ms** for
source, with .NET 10.0.15. It records source-host identity before timing and
checks it afterward; its existing startup budgets passed. The earlier
**79/85 ms** Windows samples remain in the index with their source-identity limit.
The separate Linux NativeAOT comparison measured **3.878/4.370 ms** across 48
launches. These are measurements of the small Strict application, with warmed OS
caches, on one development host; they do not predict arbitrary module performance.
See the [startup records](../Benchmarks/PowerShellCompilation/Corpus/Performance/startup-evidence.json)
for exact commits, hosts, deployment details and every sample.

The [2026-10-16 Linux result](../Benchmarks/PowerShellCompilation/Corpus/m29a-whole-workflow-qualification.json)
is retained as a superseded startup measurement: its **346/1,506 ms** result
loaded .NET from a Windows-mounted **9p** filesystem even though the executable
was on ext4. A controlled comparison of identical runtime files measured
**273.444 ms p50** on 9p and **24.571 ms** on ext4. It establishes the runtime
location cost without reproducing the original tail exactly. The
[raw comparison](../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-17-runtime-location-ab.json)
preserves both sets of samples. The timing runner now checks the resolved Linux
CoreCLR and the other existing runtime-library paths resolved in the host trace
with `findmnt`. It refuses 9p/drvfs, missing CoreCLR or missing mount evidence
before measuring startup, including with `-PerformancePolicy RecordOnly`.

The [Gallery performance comparison](performance/powershell-compiler.md#hybrid-modules-versus-source)
records separate first-import, reimport, first-invocation and loaded-module
invocation measurements for all ten modules. Hybrid first-import p50 is slower
in **10 of 10 modules**, and reimport p50 is slower in **8 of 10**. All declared
probes pass and the export counts match, but these results do not establish a
general Hybrid speedup. The runtime now reuses parsed assignment documents and
command-region plans within their defining PowerShell context. A separate,
alternating revision comparison measured **2.59x** and **4.45x** lower loaded-call
p50 for YAML and Archive after command-plan reuse. Both still trail source;
remaining assignment/storage preparation has samples and profiles recorded for
the next performance milestone in the linked report.

# PowerShell compiler measurements: 2026-11-17

These results describe two fixed external packets on Windows x64 with SDK
10.0.404. The public module probes use PowerShell 7.6.9. The external
assessment runner used PowerShell 7.6.10. The semantic oracle uses the pinned
PowerShell 7.6.6 package.

An emitted unit is a complete CLR method in the final output. A smaller compiled
region inside retained source does not count as another emitted unit. A method
can still require hosted PowerShell operations, so emission does not establish
runtime-free execution.

| Packet | Complete emitted units | Complete emitted functions | Execution proof |
| --- | ---: | ---: | --- |
| Ten public Gallery modules | 114/753 (15.14%) | Not measured separately | 10/10 Hybrid artifacts built, imported, and passed an invocation probe |
| Seven external workloads | 130/196 (66.33%) | 130/183 (71.04%) | Acquisition and census only; zero complete-workload executions |

The external assessment discovers 155 source files, with zero parser errors and
zero acquisition/analysis failures. It records five promoted regions in retained
functions. The public packet records 639 retained source units, 732 runtime-routed
units, and zero promoted regions. Those probes exercise Hybrid artifacts in
PowerShell. The ratios describe the measured packets and their discovered units.

The generated [public baseline](../Benchmarks/PowerShellCompilation/Corpus/public-corpus-baseline.net10.json)
and [external baseline](../Benchmarks/PowerShellCompilation/Corpus/external-assessment-baseline.net10.json)
pin the packet hashes and counts. The September Gallery snapshot discovered 735 units. The current
compiler discovers 753 for the same packet, so each ratio uses its own census
denominator. Earlier documents and `m29a-*` files preserve development results. In particular, the five-module **545/565**
result from 2026-10-03 is a separate targeted maintainer packet and has not been
rerun here. It must not replace either external measurement above.

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
    -EvidencePath "$scratch/external.json" -RefreshBaseline
```

Both commands acquire exact hashes from the checked-in packets. Public module
probes execute acquired source in clean child processes; external assessment
never imports or executes its acquired workloads. `-RefreshBaseline` deliberately
records new measured counts. Omit it for comparison against the committed counts.
The public refresh requires the complete module packet on the baseline host.
It keeps the comparison to the previous Hybrid results and saves the run report
before replacing the Hybrid baseline. The Strict timing budgets remain in place.

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

Earlier speedup tables were removed because their raw samples were not committed.
The [2026-10-16 Linux result](../Benchmarks/PowerShellCompilation/Corpus/m29a-whole-workflow-qualification.json)
still records **346 ms p50 / 1,506 ms p95** against **100 ms / 250 ms** budgets,
and an artifact/source p50 ratio of **1.113** against **0.75**. Performance remains
unqualified. The cause is unresolved; Windows results do not establish a Linux fix.

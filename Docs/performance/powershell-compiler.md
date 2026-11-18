# PowerShell compiler performance

The measurements below describe fixed programs, module versions and hosts. They
are fresh-process measurements with warmed filesystem caches. They establish
neither a language-wide speedup nor performance on every Linux or Windows host.

## Strict startup

All startup times cover process launch through exit. Each sample's output is
checked after timing.
The six-sample corpus runs alternate source and artifact order after one warmup
of each. Runtime inspection adds an untimed host-resolution probe; the later
Windows run also identifies the source host before warmup and checks its
executable, CoreCLR and SMA hashes after timing. The separate NativeAOT comparison rotates nine executable variants over
48 rounds after three warmups; its raw record includes the order and each sample.
Percentiles use nearest rank.

| Run | Deployment | .NET runtime storage | Artifact p50 / p95 (ms) | Source p50 / p95 (ms) |
| --- | --- | --- | ---: | ---: |
| WSL2, 2026-11-17 | Framework-dependent | 10.0.11, ext4 | 25 / 26 | 187 / 189 |
| WSL2, 2026-11-18 | Framework-dependent | 10.0.14, ext4 | 32 / 35 | 242 / 278 |
| Windows x64, 2026-11-18, host identified before timing | Framework-dependent | 10.0.15, NTFS | 168 / 221 | 757 / 1570 |
| WSL2, 2026-11-17 | NativeAOT | Compiled 10.0.14 runtime, executable on ext4 | 3.878 / 4.370 | 273.658 / 309.541 |

The [startup index](../../Benchmarks/PowerShellCompilation/Corpus/Performance/startup-evidence.json)
identifies each producer commit, SDK, host and raw sample record. The Linux host
is Ubuntu 24.04.3 on WSL2 x64. These runs used normal development activity and
the available processor set. CPU placement was not fixed to a single cache or
frequency domain; changes in scheduling, runtime versions and host activity can
change absolute times. The NativeAOT row uses a different deployment and SDK
from the framework-dependent 2026-11-17 row.

The earlier Windows run remains in the index at **79/85 ms**, with **415/579 ms**
for source. Its source-host digest was observed after sampling, so it does not
establish the source executable's identity at launch. The later run records
that identity before timing, checks it afterward and uses a verified private
copy of the compiler. Both runs produced the same Strict executable hash;
the different absolute times do not establish a compiler-code regression.

The historical **346/1,506 ms** startup record is preserved and marked as
superseded. Although the executable was on ext4, hostfxr, hostpolicy, CoreCLR and
JIT loaded from a Windows-mounted 9p filesystem. With the executable unchanged
and all **190 runtime files** byte-identical, the controlled comparison measured
**273.444/298.953 ms** on 9p and **24.571/29.073 ms** on ext4. All 36 9p launches
exceeded 100 ms; none of the 36 ext4 launches did. This establishes the runtime
storage cost; it does not explain or reproduce the original 1,506 ms tail in full.
The [raw A/B record](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-17-runtime-location-ab.json)
includes the original six samples, library paths, file hashes, page faults and
the paired measurements.

Before accepting new Linux startup samples, the corpus runner launches an
untimed host-resolution probe, requires a resolved CoreCLR path and checks the
existing host/runtime/JIT library paths found in the trace with `findmnt`.
It rejects 9p/drvfs, missing CoreCLR and missing mount evidence under both `Enforce` and
`RecordOnly`. A real executable was checked successfully with an ext4 runtime
and [rejected with a 9p runtime](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-linux-guard-rejection.json).
New runs also repeat artifact runtime inspection after the last sample and reject
changed library paths, hashes or filesystems. This detects changes between the
two inspections; it does not prove that no temporary change occurred between them.

## Hybrid modules versus source

The module comparison uses the ten exact versions and probes in the public
Gallery packet. Source and Hybrid run in separate, disposable PowerShell
processes with the same isolated module path. Import and invocation have separate
clocks inside PowerShell; host-process startup, compilation and payload
verification are outside both clocks.

Both phases use eight alternating source/Hybrid pairs per module. The import
phase records the first import and invocation, then five remove/import and
invocation repetitions in that process. These repeated imports retain assemblies
and JIT code while recreating the module. The invocation phase imports once and
records the first call followed by five more calls in that same loaded module.
Results after reimport and results from an already-loaded module stay separate.
Each invocation is the exact packet probe, including its assertions and declared
I/O; it is a bounded representative operation, not a complete application benchmark.
The Archive probe checks that the extracted file exists; it does not compare
its bytes with the input. Passing means the declared packet assertions passed.

Before and after sampling, the runner compares every source file with the
verified Gallery archive and every Hybrid file with the successful build's
inventory. Matching only a manifest is insufficient: the root module, generated
assembly and runtime dependencies must still match. All successful samples must
also pass their invocation probe. The JSON preserves every observation and the
alternating order, including slower results.

All cells below are **source / Hybrid p50 milliseconds**. First imports and
first calls have eight samples per surface; reimports and calls in a loaded
module have forty. The two phases are kept as separate observations, with no
pooling across their different module states. Raw records also retain p95.

| Module | First import | Reimport in the same process | First invocation | Invocation in a loaded module |
| --- | ---: | ---: | ---: | ---: |
| PSFramework 1.14.457 | 1373.543 / 3050.582 | 209.051 / 1104.852 | 22.165 / 21.042 | 5.952 / 7.114 |
| PSScriptAnalyzer 1.25.0 | 193.519 / 216.231 | 37.370 / 42.097 | 98.371 / 95.740 | 1.984 / 3.001 |
| ImportExcel 7.8.10 | 909.964 / 1085.638 | 536.200 / 681.431 | 21.309 / 23.253 | 0.255 / 0.209 |
| platyPS 0.14.2 | 189.582 / 294.503 | 26.864 / 52.894 | 19.832 / 33.829 | 0.097 / 0.144 |
| Pester 6.1.0 | 1021.810 / 1025.881 | 228.765 / 220.810 | 5.561 / 5.996 | 0.366 / 0.434 |
| InvokeBuild 5.14.23 | 198.588 / 247.457 | 8.501 / 11.798 | 193.941 / 202.162 | 76.676 / 116.409 |
| Microsoft.PowerShell.Archive 1.2.6 | 139.948 / 258.836 | 19.470 / 68.861 | 339.112 / 882.208 | 17.741 / 149.830 |
| powershell-yaml 0.4.12 | 220.545 / 327.474 | 9.240 / 44.615 | 67.876 / 153.445 | 0.440 / 21.029 |
| Pode 2.14.1 | 1291.950 / 1455.673 | 908.006 / 988.547 | 10.961 / 13.567 | 1.616 / 1.441 |
| ThreadJob 2.1.0 | 218.351 / 260.793 | 14.906 / 13.750 | 98.050 / 92.666 | 56.486 / 60.226 |

Hybrid first-import p50 is higher in **10 of 10 modules**; reimport p50 is
higher in **8 of 10**. This packet does not establish a general Hybrid speedup.
Short invocation probes have visible variation; small median differences are
observations from this run, not stable rankings or full-workload speedups.
The source and Hybrid export counts match for all ten modules, and every
recorded invocation passes its declared probe.

Both phases used Windows x64, PowerShell **7.6.9**, .NET **10.0.14**, normal
priority and all 32 logical processors of a 16-core Ryzen 9 9950X3D2, with the
High performance power plan. Processor placement was not fixed to one domain.
The host installation prefix is omitted from the public records; executable,
CoreCLR and SMA hashes identify the exact host bytes.

The [import/reimport samples](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-gallery-cached-module-timing.json),
[loaded-module invocation samples](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-gallery-cached-module-invocations.json)
and [compiled file inventory](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-gallery-cached-artifacts.json)
pin the packet, compiler commit and payload hashes.

This table uses compiler `62aa73f9`. The earlier complete
[import run](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-gallery-module-timing.json),
[invocation run](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-gallery-module-invocations.json)
and [inventory](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-gallery-artifacts.json)
use `1650b546`, with parsed-document reuse but without command-plan reuse.
Source timings also moved between these separate runs, so their absolute
differences do not isolate the effect of the compiler change. The paired revision
comparison below supplies that narrower comparison. The
[compiler binary identities](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-compiler-binaries.json)
record the source revisions and hashes of the CLI and both compiler assemblies.

## Assignment preparation cost

The [sampled PSFramework import stack](../../Benchmarks/PowerShellCompilation/Corpus/Performance/psframework-import-stack.txt)
lists `CreateAssignmentTarget`, `Parser.ParseInput`, `AssignTarget` and the
generated `Test-PSFParameterBinding` method as separate inclusive entries.
These flat entries do not establish caller/callee edges. Runtime source
inspection identified whole-document parsing in assignment preparation: it
reparsed the complete source document in each native
invocation, even when earlier calls had already parsed the same document.

The runtime now reuses parsed assignment documents within the same PowerShell
execution context. Weak context and source keys let cached trees be reclaimed
with their runspace and source. Different filenames retain separate source extents; native
assignment compilation still receives a detached target. Parsed type-name
metadata does not cross runspaces, and invocation state is not cached.

The [preparatory diagnostic](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-psframework-cache-diagnostic.json)
keeps the initial before/after observations and their narrower limits. That
single diagnostic pair ran during normal development activity; the complete
module comparison is the basis for source/Hybrid conclusions.

## Hosted operation preparation cost

Before command-plan reuse, the loaded YAML probe measured
**112.802 ms** for Hybrid versus **0.766 ms** for source; the Archive extraction
probe measured **824.990 ms** versus **26.169 ms**. The separate
[YAML stack](../../Benchmarks/PowerShellCompilation/Corpus/Performance/yaml-warm-stack.txt)
and [Archive stack](../../Benchmarks/PowerShellCompilation/Corpus/Performance/archive-warm-stack.txt)
show `NativeCommandRegion.Create`, `Parser.ParseInput`, `NativeAstCompiler.Compile`
and expression compilation in the hosted paths. Their
[profile metadata](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-hosted-operation-profiles.json)
identifies the exact compiler, host and workloads. These sampled stacks diagnose
the preparation cost; their percentages include waiting threads and
must not be read as latency speedups.

Compiled command-region operations already accept the current function context
as an invocation argument. The runtime now reuses these plans within their
defining execution context and script block, partitioned by locals-tuple type
and current language mode. The lookup also distinguishes source identity,
location, capture and partial-output policy, and guarded source-load operands.
Output and error callbacks still belong to each call; failed compilation is not
cached. Weak ownership lets plans be reclaimed with the defining function and
runspace. Entry count within a live owner is not capacity-bounded, and retained
source extents may keep document text alive for that owner's lifetime.

The [alternating revision comparison](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-hybrid-plan-revision-ab.json)
uses the existing verified artifacts from both compiler revisions in separate
processes, with eight alternating pairs per module. Each process imports once
and runs the same probe six times. The loaded-call summaries contain forty
observations per revision. Package bytes, dependency locks and emitted-unit
counts match; the compiler change is command-plan reuse.

| Loaded-module probe | Before p50 / p95 (ms) | After p50 / p95 (ms) | Before/after p50 ratio |
| --- | ---: | ---: | ---: |
| powershell-yaml | 66.817 / 85.663 | 25.753 / 42.449 | 2.59 |
| Microsoft.PowerShell.Archive | 593.814 / 1106.088 | 133.531 / 501.365 | 4.45 |

These improvements apply to repeated calls after preparation. YAML's first-call
p50 in this comparison was higher after the change: **194.274 / 208.630 ms**
before/after. The JSON retains first-import and first-call samples as well as
loaded calls. Neither the paired result nor the complete source comparison
establishes faster Hybrid module startup.

## Next performance milestone: assignment and variable-storage preparation

The current source comparison still measures **21.029 / 0.440 ms** for the
loaded YAML probe and **149.830 / 17.741 ms** for Archive, Hybrid/source.
The current [YAML](../../Benchmarks/PowerShellCompilation/Corpus/Performance/cached-yaml-warm-stack.txt),
[Archive](../../Benchmarks/PowerShellCompilation/Corpus/Performance/cached-archive-warm-stack.txt)
and [PSFramework import](../../Benchmarks/PowerShellCompilation/Corpus/Performance/cached-psframework-import-stack.txt)
exports list `CreateAssignmentTarget`, `NativeAstCompiler.Compile` and
`NativeVariableStorage.Compile` alongside generated function methods. Source
inspection shows that these operations
prepare assignments and variable access within each native invocation, even
when command plans are already available.

Sharing these plans needs additional identity for optimized-variable slot maps
and optimized versus dynamic storage. The command-plan cache does not qualify
that reuse. The next milestone is to specialize assignment and storage plans
while preserving current invocation values, constraints, target evaluation
order, errors and source extents on both supported PowerShell hosts. Compare
the same packet and alternate old/new artifacts before making another claim.

The [profile metadata](../../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-18-cached-runtime-profiles.json)
records the compiler, host, artifact hashes, requested work and completed probe
status. Profiles are separate from the unprofiled timings and include setup and
waiting threads. The bounded PSFramework collection ended before its requested
import sequence completed; it is diagnostic stack evidence, not a functional pass.

## Reproduce

Build the CLI, then run the existing corpus runner with a disposable workspace.
On Linux, install .NET on a native filesystem and verify `DOTNET_ROOT` with
`findmnt -n -o FSTYPE --target "$DOTNET_ROOT"`. The runner additionally checks
the libraries actually resolved by the application.

```powershell
$workspace = Join-Path ([IO.Path]::GetTempPath()) 'powerforge-performance'
$corpusEvidence = Join-Path $workspace 'public.json'
dotnet build ./PowerForge.Cli/PowerForge.Cli.csproj -c Release -f net10.0
./Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1 `
    -WorkspacePath $workspace -EvidencePath $corpusEvidence `
    -PerformancePolicy RecordOnly -KeepRunArtifacts
./Benchmarks/PowerShellCompilation/Corpus/Measure-PublicModuleTiming.ps1 `
    -WorkspacePath $workspace -CorpusEvidencePath $corpusEvidence `
    -EvidencePath (Join-Path $workspace 'module-timing.json')
./Benchmarks/PowerShellCompilation/Corpus/Measure-PublicModuleTiming.ps1 `
    -WorkspacePath $workspace -CorpusEvidencePath $corpusEvidence `
    -InvocationOnly -EvidencePath (Join-Path $workspace 'module-invocation-timing.json')
```

The complete Gallery comparison was measured on Windows; the command can also
run on another supported host, where that host's module compatibility must be
validated. To measure only Strict startup on Linux, add `-SkipModules` and use
`-PerformancePolicy Enforce`. Neither command changes the corpus baselines.
Remove the disposable workspace after saving the JSON evidence.

To compare two compiler revisions, retain each revision's complete corpus report
and workspace, then use `Measure-HybridRevisionTiming.ps1` with
`-BaselineWorkspacePath`, `-BaselineEvidencePath`, `-CandidateWorkspacePath`,
`-CandidateEvidencePath`, `-ModuleId powershell-yaml,Microsoft.PowerShell.Archive`
and `-EvidencePath`. It uses the same payload verification and loaded-module
timing child as the source comparison. The corpus report records compiler
assembly versions and hashes; a missing or inconsistent source revision is
reported as `NotRecorded` rather than inferred from the harness checkout.
Both timing commands require a new output file, preserving existing input and
timing records. Relative paths follow PowerShell's current filesystem location.

For a NativeAOT comparison, build the same application with
`--kind exe --mode Strict --framework net10.0 --rid linux-x64 --optimization NativeAot --self-contained --include-resource report-label.txt --allow-unreviewed-dependencies`.
Use the same process-launch timing boundary and validate its normal output,
resource access and controlled failure before measuring. NativeAOT does not
load a shared .NET runtime; no runtime mount check applies to that deployment.

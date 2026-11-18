# Compile a typed subset of PowerShell

PowerForge is a **typed-subset compiler** for PowerShell. It compiles supported
code into CLR methods when it can determine the types and preserve the operations'
behavior. Start with `powerforge powershell analyze <path>`. After a build, the
manifest shows which methods compiled and where the artifact relies on PowerShell.

## What can compile?

| Construct | Strict support | When it needs PowerShell |
| --- | --- | --- |
| Typed scalar parameters, one-dimensional typed arrays, literal defaults | Supported for process-bindable types | Host-only parameter types and binding features |
| Typed locals, scalar arithmetic and comparisons | Supported for admitted types and conversions | Unknown or changing types and unadmitted operators |
| `if`, `for`, `while`, `do`, array `foreach`, `return` | Supported within the typed rules | Dynamic enumeration and labeled loop control |
| Local function calls | Supported when the complete reachable code compiles | An unsupported reachable function prevents a Strict build |
| Selected CLR members, typed exceptions, bounded `try`/`catch`/`finally` | Supported where analysis proves the contract | Runtime type lookup and unsupported exception behavior |
| Cmdlet calls and multi-stage pipelines in admitted Hybrid functions | Generally unsupported in Strict | Complete emitted method bodies use PowerShell command resolution, binding, streams, and pipeline execution |
| Advanced function parameters and `begin`/`process`/`end` | No general runtime-free binding support | Native-bound Hybrid functions preserve the host's parameter binder and lifecycle; admission still checks each body |
| Functions beside authored classes | No general runtime-free authored-class support | Hybrid retains type initialization; unsupported direct type annotations and expressions stay hosted, while runtime type lookup and calls to retained helpers can remain inside emitted bodies |
| Providers, remoting, dynamic script blocks | No general Strict support | Hybrid or Package uses the supported hosted paths; provider or service behavior needs separate execution proof |
| Dynamic commands, `Invoke-Expression`, authored classes, WPF | No general runtime-free support | Retained source or explicitly supported hosted paths |

This table describes categories, not a promise that every expression in a
category compiles. [The detailed subset](PowerForge.PowerShellCompilation.md#current-typed-subset)
lists the narrower type and binding rules. Strict rejects a build when reachable code falls outside the supported subset.

## Which output should I choose?

| Choice | Use it for | Runtime needed after building |
| --- | --- | --- |
| Strict executable | A small utility whose entire reachable program fits the subset | .NET for framework-dependent output; bundled .NET for self-contained output; neither .NET nor PowerShell for NativeAOT |
| Hybrid executable | Mixed typed code and dynamic script behavior | PowerShell/SMA and .NET; the executable carries its hosted runtime assets |
| Package executable | Delivering a script with broad dynamic behavior | PowerShell/SMA and .NET; packaging embeds the script and runtime assets |
| Binary module, Strict or Hybrid | Commands used from a PowerShell session | A compatible PowerShell host; Strict has no script fallback, Hybrid can retain source |
| CLR library | Calling typed methods directly from .NET | A compatible .NET host; unsupported code has no PowerShell fallback |

Builds need the PowerForge CLI and the selected .NET SDK. Single-file output
targets one OS and architecture. Strict does not automatically mean NativeAOT;
choose the deployment explicitly. The current supported executable targets are
Strict `net10.0` on Windows x64 and Linux x64, framework-dependent or NativeAOT.
Other combinations remain experimental. Packaging does not conceal source.

## Two small Strict programs

From the repository root, build the checked-in examples:

```powershell
powerforge powershell build ./Examples/PowerShellCompilation/Add-Numbers.ps1 --kind exe --mode Strict --framework net10.0 --allow-unreviewed-dependencies --out ./artifacts/add --name Add-Numbers
powerforge powershell build ./Examples/PowerShellCompilation/Sum-Numbers.ps1 --kind exe --mode Strict --framework net10.0 --allow-unreviewed-dependencies --out ./artifacts/sum --name Sum-Numbers
./artifacts/add/Add-Numbers.exe --Left 20 --Right 22  # 42
./artifacts/sum/Sum-Numbers.exe --Count 5             # 15
```

The first program is `param([int]$Left = 20, [int]$Right = 22)` followed by
`return [int]($Left + $Right)`. The second sums integers from 1 through `Count` in a
typed `for` loop. On Linux, launch the generated files without the `.exe` suffix.
The dependency option permits these local examples to build without a
reviewed dependency lock. Use the [project workflow](PowerForge.PowerShellCompilation.md#reproducible-project-workflow)
for repeatable dependency review and delivery.

## What the measurements establish

The [coverage report](PowerForge.PowerShellCompilation.Coverage.md) records the
2026-11-18 reruns of both fixed packets. It distinguishes complete emitted units
from retained source and smaller compiled regions. The seven-workload external
assessment emits **130/196 units (66.33%)** and **130/183 functions (71.04%)**,
with zero parser errors. **One of seven complete workloads passes**: all ten
GitHub helper functions execute in ten scenarios on original, built, and installed
surfaces. Configuration and time are fixed at the module boundary and HTTP uses
a loopback response; live GitHub operations and the other six workloads remain
untested.
The public Gallery packet emits **436/753 (57.90%)** of its units as complete CLR
methods, up from 114/753 (15.14%). Pester supplies the 322 additional methods;
the other modules lose none. All ten Hybrid artifacts pass the checked-in
invocation probes.
Neither packet measures the percentage of the PowerShell language supported.
The maintainer's five-module packet is separate, targeted historical evidence.

The Strict corpus contains six small programs. With .NET and the executable on
WSL2 ext4, the small application started in **25/26 ms p50/p95** on 2026-11-17
and **32/35 ms** on 2026-11-18; the latter source run took **242/278 ms**.
A separate Linux NativeAOT run measured **3.878/4.370 ms**. The
later Windows framework-dependent run measured **168/221 ms**, versus **757/1,570 ms**
for source, with its source host identified before timing. The earlier Windows
**79/85 ms** samples remain available. The earlier Linux **346/1,506 ms** result loaded .NET from a Windows-mounted 9p filesystem and is
superseded. These fresh-process measurements use warmed OS caches and describe
this fixture and host. See the [raw startup records](../Benchmarks/PowerShellCompilation/Corpus/Performance/startup-evidence.json)
and [measurement limits](PowerForge.PowerShellCompilation.Coverage.md#semantic-and-performance-limits).

Hybrid is not a general speedup for these Gallery modules. Its first-import p50
is slower in **10 of 10 modules** in the recorded comparison. The YAML probe in an
already-loaded module measures **0.440 ms for source / 21.029 ms for Hybrid**;
the Archive extraction probe measures **17.741 / 149.830 ms**. That probe checks
the expanded file's existence rather than byte equality. A separate alternating
revision comparison measured **2.59x** and **4.45x** lower loaded-call p50 after
command-plan reuse; that benefit does not establish faster first imports. See the
[complete module comparison](performance/powershell-compiler.md#hybrid-modules-versus-source)
for first use, reimports, loaded-module calls, faster cases and raw samples.

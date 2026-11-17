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
| Cmdlets, providers, remoting, general pipelines, script blocks | Generally unsupported in Strict | Hybrid or Package keeps these operations hosted |
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
2026-11-17 reruns of both fixed packets. It distinguishes complete emitted units
from retained source and smaller compiled regions. The seven-workload external
assessment emits **130/196 units (66.33%)** and **130/183 functions (71.04%)**,
with zero parser errors. It performs **zero complete-workload executions**.
The public Gallery packet emits **114/753 (15.14%)** of its units as complete CLR
methods. All ten Hybrid artifacts pass the checked-in invocation probes.
Neither packet measures the percentage of the PowerShell language supported.
The maintainer's five-module packet is separate, targeted historical evidence.

The Strict corpus contains six small programs. The recorded Linux Strict
startup was **346 ms p50** against a **100 ms** budget. See the
[performance results](PowerForge.PowerShellCompilation.md#measured-performance).

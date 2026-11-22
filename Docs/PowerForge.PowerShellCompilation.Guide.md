# Compile a typed subset of PowerShell

PowerForge is a **typed-subset compiler** for PowerShell. It compiles supported
code into CLR methods when it can determine the types and preserve the operations'
behavior. Start with `powerforge powershell analyze <path>`. After a build, the
manifest shows which methods compiled and where the artifact relies on PowerShell.

## What can compile?

| Construct | Strict support | When it needs PowerShell |
| --- | --- | --- |
| Typed scalar parameters, one-dimensional typed arrays, literal defaults | Supported for process-bindable types. A standalone string parameter can default to `$PWD` in the filesystem working directory | Host-only parameter types, provider locations and other runtime-evaluated defaults |
| Typed locals, scalar arithmetic and comparisons | Supported for admitted types and conversions | Unknown or changing types and unadmitted operators |
| `if`, `for`, `while`, `do`, array `foreach`, `return` | Supported within the typed rules | Dynamic enumeration and labeled loop control |
| Local function calls | Supported when the complete reachable code compiles | An unsupported reachable function prevents a Strict build |
| Selected CLR members, typed exceptions, bounded `try`/`catch`/`finally` | Supported where analysis proves the contract | Runtime type lookup and unsupported exception behavior |
| Pipelines over typed records | Strict `net10.0` executables support bounded `Where-Object`, single-scalar `ForEach-Object`, known scalar `Select-Object -ExpandProperty`, and terminal `Select-Object -First` with a positive Int32 literal. Use an explicitly typed vector to collect supported output or `return` to emit the pipeline result | Hybrid handles general cmdlet invocation, dynamic records and script-block behavior |
| Recursive filesystem and CSV data | Bounded `Get-ChildItem -Recurse` filesystem streams can filter file size and project `Name` and `FullName`. `Import-Csv` with literal headers can feed a directly following `foreach` that interpolates those fields. A supported `Resolve-Path` assignment uses a constrained string target | Other providers, general cmdlet parameters and arbitrary use of an imported row object |
| Advanced parameters and `begin`/`process`/`end` | One or two local stages can bind an exact by-value `ValueFromPipeline` parameter, starting from a typed array, with optional named switches. Two stages deliver matching stable-scalar records synchronously. A terminal `Select-Object -First` stops the producer before it resumes. PowerShell 7 profile `clean` runs for each started stage and requires proven-total statements or an exhaustive local catch | Broader parameter binding, general stage chains, and capturing the result of composed stages use the PowerShell host. Bounded Int32 reductions can also follow pure filters in Strict |
| Module-scoped classes and enums | No general runtime-free authored-type support | Hybrid can compile functions that construct module-defined types, use them in local storage, and expose them in output metadata. PowerShell defines the types and runs their methods |
| Function-local data classes | No general runtime-free authored-class support | Qualified functions preserve lexical identity, typed arrays, property validation, conversions, and static reads. References from separate script blocks require the defining scope |
| Delivered managed dependency types in parameters, casts and metadata | No general dependency-type binding support | Hybrid reads authenticated delivered DLL metadata, including supported closed public generics. PowerShell handles loading, type identity, conversion, attributes and validation |
| Types supplied by a declared Windows system module | Unsupported in Strict | Hybrid locks metadata from qualified system module manifests and assemblies for the matching Windows architecture. PowerShell resolves the declared module and binds the actual objects at invocation |
| Types loaded during module setup | Unsupported in Strict | Hybrid can bind types from an authored literal `Add-Type` load or a locked, directly dot-sourced class file. PowerShell resolves the type when the function is invoked and reports missing types through its binding rules |
| Switch-selected module files | Unsupported in Strict | Hybrid stages each contained literal script returned by a top-level switch and dot-sourced through `Join-Path $PSScriptRoot`. PowerShell selects and loads one of the reviewed files in module scope |
| Calls to bundled executable helper scripts | Strict uses its separate dot-sourced declaration subset | A retained Hybrid entry or loader can use `&` with a contained `.ps1` path derived from the script directory. PowerShell invokes the reviewed file in a child scope with its authored arguments and redirection. Eligible caller functions keep their authored declaration timing |
| Mandatory parameters on retained executable entries | Supported within the process argument types above | Package and Hybrid delegate required parameters to the native binder. A missing mandatory argument produces a PowerShell binding error in the packaged host |
| Borrowed SDK objects, syntax-tree nodes, parameter metadata and completion enums | No general runtime-free host-type support | Native Hybrid binding preserves identity for admitted session, variable, cmdlet, dynamic-parameter and AST types; the host owns conversion and mutation |
| Returned script-block literals and literals in arrays or dictionaries | Unsupported in Strict | Admitted Hybrid module-function bodies compile completely and retain native dynamic scope, module state, closure capture, failures and later invocation; executable roots and functions needing that module owner remain hosted |
| Literal type aliases registered for delivered managed types | Unsupported in Strict | Authenticated delivered registration scripts identify aliases without executing them during compilation. PowerShell resolves the aliases at invocation, including in admitted nested function declarations and casts |
| Local callers of native-bound functions | No general native binding in Strict | The call graph selects native invocation storage for callers and callees, preserving basic extra arguments and advanced parameter binding |
| Multiple assignment to local variables | Unsupported in Strict | Hybrid preserves the number of pipeline records and the assignment order. A single array record stays together |
| Function declarations and file loads | Unsupported in Strict | Hybrid registers each supported body at its declaration and preserves command availability before and after a source file loads |
| Script module startup and `data` statements | No general runtime-free module startup | A qualifying single-file Hybrid module can compile startup assignments, control flow and function declarations. PowerShell binds import arguments, processes dynamic parameters, initializes authored types and runs admitted command and `data` operations in the importing module's scope |
| Script blocks supplied to native callbacks | Unsupported in Strict | Hybrid can compile the surrounding function while PowerShell invokes the callback and handles `$using:` capture, traps and cleanup |
| Literal wildcard and bounded exact `switch` clauses | Within the typed scalar rules | Hybrid preserves host matching and enumeration for supported wildcard literals, numeric literals and exact variable labels |
| Scoped storage and borrowed host values | Within the typed local and module-state rules | Hybrid handles supported nested script-scope index updates, direct environment reads under a conversion and access to selected SDK or directory objects with native identity |
| Simple installed-module names in `#requires -Modules` | Unsupported in Strict | Hybrid keeps the directive in the module script. PowerShell loads required modules before its function declarations run |
| Legacy `ModuleToProcess` module manifests | Subject to the same body checks | Generated modules preserve the legacy root key and its referenced artifact; declaring both root keys is rejected |
| `break` and `continue` targeting a caller's loop | No general runtime-free caller transfer | Native Hybrid functions use PowerShell flow control for unlabeled transfers and admitted literal or computed labels. The caller's matching loop handles the transfer after `finally` runs |
| `exit` in admitted Hybrid functions | Unsupported in Strict | The active PowerShell SDK owns exit conversion and script termination; caller-transfer predicates remain hosted |
| `exit` in standalone executable entries | An Int32 status can exit the process from a typed script or local function after running enclosing `finally` blocks | Package and Hybrid also cover host conversions, nested expressions and trap scopes |
| Plain `Read-Host` in standalone executables | A direct script assignment accepts plain line input with an optional nonempty literal prompt. The redirected runtime-free interface uses UTF-8 | Package and Hybrid provide native console behavior. Masked or secure input, choices and credentials require broader host UI support |
| `Write-Progress` | A single activity and its completion are supported for bounded standalone scripts. Redirected runs keep progress out of success output | Full interactive progress presentation and other host progress records use PowerShell |
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

### Source encoding

A byte order mark controls how the compiler reads a script. Without one, the
PowerShell 7 profile reads UTF-8 and the Windows PowerShell 5.1 profile uses
the active Windows code page. The selected profile also applies to scripts
loaded by literal dot-source statements, module source discovery and literal
paths used to identify bundled resources. Package uses the build host's source
encoding rules when no semantic profile is selected. Selecting a different
profile resolves the source dependencies again. Hybrid modules targeting
Windows PowerShell ship retained runtime scripts with a Unicode byte order
mark when their bytes would otherwise depend on the recipient's code page.
Files carried only as manifest inventory keep their original bytes. Use a UTF-8 BOM for
non-ASCII source when building for Windows PowerShell 5.1 on another operating
system.

### Inspecting Package artifacts

A Package executable embeds PowerShell source and runs it with the bundled
engine. The launcher extracts the script and its bundled files to
`PowerForge/PackagedSources` under the user's local application data directory.
The source is also readable from the managed assembly's resources without
running the executable. The entry script can include generated path setup
and checks around a bundled dot-source call. Package provides an executable
distribution format for the scripts it carries.

Packaged entries preserve compatible `#requires -Version` and
`#requires -PSEdition` directives in the embedded script. Check and build apply
the embedded PowerShell host's supported major and minor versions and edition.
Requirements for modules, assemblies, a particular shell, or elevation block
the build. Script dependencies with `#requires` directives also block the build.

Two clean Windows x64 NativeAOT builds of the
[file-summary example](../Examples/PowerShellCompilation/Get-FileLineSummary.ps1) produced
byte-identical executables with the build cache disabled. The executable
records the PDB filename without the temporary build path. The separate native
PDB files differed between the two builds. Independently repeated Linux x64
NativeAOT builds of that script also produced byte-identical executables.

Before building, restore the SDK-selected runtime pack for your target.
Follow the [runtime pack preparation steps](PowerForge.PowerShellCompilation.md#dependencies-and-runtime-resources).

```powershell
powerforge powershell build ./Examples/PowerShellCompilation/Get-FileLineSummary.ps1 --mode Strict --framework net10.0 --rid win-x64 --self-contained --optimization NativeAot --no-build-cache --allow-unreviewed-dependencies --out ./artifacts/file-summary
./artifacts/file-summary/Get-FileLineSummary.exe --Path ./example.txt --MinimumLength 5
```

Use `linux-x64` and omit the `.exe` suffix on Linux. The example counts all
lines, then reports the number and total character count of lines that meet the
minimum length. It reports a missing file as a terminating error.

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
2026-11-19 reruns of both fixed packets. It distinguishes complete emitted units
from retained source and smaller compiled regions. The seven-workload external
assessment emits **160/196 units (81.63%)** and **160/183 functions (87.43%)**,
with zero parser errors. **Six of seven complete workloads pass** across 31
scenarios on original, built and installed surfaces. They cover the GitHub
helper library, the PowerShell and Scoop installers, GetBIOS, Autopilot and
Locksmith2.
The installers use native module adapters, pinned payloads, owned directories and
process-local registry replacements; PowerShell and CMD shim dispatch are checked.
GetBIOS exercises all four nested vendor helpers and actual reports with owned
WMI/remoting providers. Autopilot exercises its delivered native script-entry
factory with owned CIM sessions and actual CSV files; console execution covers
startup/help. Its explicit executable target is `net10.0` Windows x64. The default
external census still reports Autopilot 0/1 because it has no explicit runtime
contract; that number is separate from the qualified artifact's complete root.
Locksmith2 emits and exercises all 100 owned functions across a read-only lab
audit and four local scenarios on PowerShell 7.6.6. Live hardware, Graph
registration, directory changes and global installations are outside the
measured scenarios.
The public Gallery packet emits **753/753 (100%)** of its units as complete CLR
methods, up from 114/753 (15.14%) and the preceding 720/753 measurement. Pester
emits 491/491 and PSFramework emits 176/176. All ten Hybrid artifacts build,
import, and pass their invocation probes.
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

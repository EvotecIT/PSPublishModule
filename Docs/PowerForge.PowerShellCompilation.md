# PowerShell Compilation

PowerForge can turn a `.ps1` or `.psm1` file into three different artifact shapes:

- a packaged executable that preserves dynamic PowerShell semantics, or a genuinely typed executable for an eligible top-level script;
- an importable binary or hybrid module whose eligible functions are compiled to typed CLR methods;
- a plain CLR library containing eligible typed methods for use from .NET.

These are deliberately separate claims. Packaging makes a script easier to distribute, but does not make its body faster. Typed compilation removes PowerShell's dynamic execution path for a conservative function subset and can improve CPU-bound code. Every build writes a JSON manifest that says which path was used.

## Build artifacts

| Kind | Default mode | Result | PowerShell required at runtime | Typed speedup expected |
| --- | --- | --- | --- | --- |
| `Executable` / `exe` | `Package` | Single-file host with embedded script and PowerShell SDK | Embedded in the application | No |
| `Executable` / `exe` | `Strict` | Runtime-independent typed .NET executable | No | Yes, for eligible CPU-bound work and process startup |
| `BinaryModule` / `dll` | `Strict` | Importable DLL when every function compiles | Yes, as the cmdlet host | Only inside sufficiently coarse compiled work |
| `BinaryModule` / `dll` | `Hybrid` | Module folder with a typed DLL and `.psm1` fallback | Yes | For eligible functions; unsupported functions remain scripts |
| `Library` / `library` | `Hybrid` | CLR DLL with eligible public static methods | No | Yes, when called as CLR code |

Supported target frameworks are:

- executable: `net8.0`, `net10.0`;
- CLR library or binary module: `net472`, `net8.0`, `net10.0`.

The `net472` binary-module lane is tested by importing and invoking the generated DLL in Windows PowerShell 5.1. The modern lanes run in PowerShell 7.

## Use the CLI

Analyze a file or a complete source tree before building:

```powershell
powerforge powershell analyze .\MyModule --mode Hybrid
```

Package a script as an executable:

```powershell
powerforge powershell build .\Invoke-Report.ps1 `
    --kind exe `
    --out .\artifacts `
    --name Invoke-Report

.\artifacts\Invoke-Report.exe --Path C:\Reports --Format Html
```

Compile an eligible top-level script into a PowerShell-free executable:

```powershell
powerforge powershell build .\Measure-Threshold.ps1 `
    --kind exe `
    --mode Strict `
    --out .\artifacts `
    --name Measure-Threshold
```

Strict typed executables accept required, explicitly typed scalar and one-dimensional array parameters. Their generated CLI supports positional values, repeated array options, `--Name value`, `--Name=value`, and `--`. It rejects missing, duplicate, or unknown parameters before invoking compiled code.

The generated host accepts positional arguments, `--Name value`, `--Name=value`, switches and aliases such as `--Force`, common switches on advanced scripts, and `--` to stop named-argument parsing. A non-switch named parameter must have a value; use `--Name=-value` when that value begins with `-`. Output and information records go to stdout in arrival order, while warnings and errors go to stderr. Nonterminating error records do not by themselves change a successful process exit code; a top-level explicit `exit <code>` becomes the process exit code, and a terminating exception fails the process. `$PSScriptRoot` resolves to the packaged artifact directory and `$PSCommandPath` to the running artifact path. Packaging rejects `exit` inside a function, nested script block, trap, or caught region because exception instrumentation would change PowerShell behavior.

Compile a strict binary module:

```powershell
powerforge powershell build .\MathTools.psm1 `
    --kind dll `
    --mode Strict `
    --framework net8.0 `
    --out .\artifacts

Import-Module .\artifacts\MathTools.dll
```

When `MathTools.psd1` exists beside `MathTools.psm1`, the primary artifact is a rewritten manifest in a module directory. `RootModule`, `FunctionsToExport`, and `CmdletsToExport` are remapped so a function that became a binary cmdlet keeps the same public name. Literal top-level `Export-ModuleMember` declarations are preserved across typed and fallback commands; dynamic export expressions are rejected. An omitted `AliasesToExport` entry stays omitted so aliases created by retained module source continue to follow PowerShell's default manifest policy.

Build a hybrid module when only part of the source is eligible:

```powershell
powerforge powershell build .\Operations.psm1 `
    --kind dll `
    --mode Hybrid `
    --out .\artifacts

Import-Module .\artifacts\Operations\Operations.psm1
```

Build a runtime-independent CLR library containing every eligible function:

```powershell
powerforge powershell build .\Calculations.psm1 `
    --kind library `
    --mode Hybrid `
    --out .\artifacts
```

Add `--output json` to either `analyze` or `build` for a stable machine-readable envelope.

## Use the PSPublishModule cmdlet

The cmdlet is a thin PowerShell surface over the same artifact builder:

```powershell
Build-PowerShellArtifact `
    -Path .\Operations.psm1 `
    -Kind BinaryModule `
    -Mode Hybrid `
    -OutputDirectory .\artifacts
```

It supports `-WhatIf`, returns `PowerShellCompilationBuildResult`, and uses the same defaults and manifests as the CLI.

## Modes

`Analyze` parses source and reports one decision per top-level script body or function. It produces no artifact.

`Package` preserves dynamic PowerShell behavior. The current executable lane embeds the source script and PowerShell SDK in a generated .NET host. It is a distribution feature, not typed compilation.

`Hybrid` compiles complete eligible functions and retains diagnostics for everything else. A hybrid binary module removes compiled function definitions from its generated `.psm1`, imports the typed DLL, and keeps unsupported functions on the script path. Literal `$PSScriptRoot` dot-source dependencies are staged recursively with their relative layout, including dependencies reached from manifest runtime hooks. Dynamic, missing, wildcard, working-directory-relative, source-root-escaping, or symbolic-link/junction paths fail before publication. A hybrid CLR library extracts eligible methods without carrying script fallback because it is intended for direct .NET consumption.

`Strict` fails the build when any executable unit needs fallback. For an executable it compiles one eligible top-level script body into a native .NET entrypoint with no PowerShell SDK dependency. For a DLL it guarantees that the artifact contains only behavior covered by the typed compiler contract.

## Current typed subset

Eligibility is whole-function and intentionally conservative. One unsupported construct keeps the complete function on the PowerShell path.

The first subset supports:

- explicitly typed scalar parameters and one-dimensional typed arrays, including preserved `Parameter(Mandatory)` metadata;
- typed or safely inferred local variables;
- explicit `return` values and one terminal implicit-output expression;
- `if`/`elseif`/`else`, `for`, `while`, and `foreach` over typed arrays or an explicitly typed scalar string;
- Boolean logic and scalar comparisons with known compatible types;
- string equality with PowerShell case-sensitive or case-insensitive behavior;
- scalar string `-split` and string-array `-join`;
- lookup-only, case-insensitive string dictionaries created from homogeneous string hashtable literals;
- empty `CmdletBinding()` metadata and `Parameter(Mandatory)` metadata, with mandatory binding preserved by generated binary cmdlets;
- floating-point and decimal arithmetic with compatible operands;
- explicitly typed integral accumulators and loop counters with checked assignment semantics;
- unlabeled `break` and `continue` inside supported loops;
- one-dimensional typed-array and string indexing with PowerShell-compatible negative and missing-index behavior for direct returns and compound-assignment operands;
- statically resolved CLR constructors, static fields/properties, instance fields/properties, and exact method overloads for supported typed arguments.

Member compilation is intentionally exact. The emitter resolves a single CLR member or overload at build time, applies only supported assignable/numeric/one-character conversions, and falls back when resolution is missing or ambiguous. Both the type and selected constructor, method, property, or field must exist in the requested target framework's reference assemblies; analyzer-host-only APIs and general constructed generic types are rejected before `dotnet` compilation. The compiler-owned homogeneous string dictionary is the current narrow generic exception. Null typed arrays preserve PowerShell's zero-length `.Length` behavior, and a nullable inferred string's property access uses PowerShell's empty-string property semantics while method invocation retains CLR null failure behavior.

The analyzer rejects dynamic behavior rather than guessing. Current blockers include:

- command, provider, pipeline, and dynamic command invocation;
- dynamic member names, PowerShell-adapted properties, ambiguous overloads, and general object-property semantics;
- script blocks, closures, runtime scopes such as `$env:`, and untyped parameters;
- parameter attributes beyond empty `CmdletBinding()` and `Parameter(Mandatory)`, default expressions, and `dynamicparam`, `begin`, `process`, or `clean` blocks;
- nonterminal or nested implicit pipeline output;
- PowerShell truthiness conversions, element-wise array comparison, and coercion between incompatible CLR types;
- string relational operators whose culture-aware ordering has not yet been translated;
- explicit conversion expressions, heterogeneous branch return types, and integral division whose PowerShell result type depends on the quotient;
- untyped integral arithmetic that can change CLR type after overflow;
- array concatenation and compound-assignment operand pairs that have no exact static CLR operator;
- source `#requires` directives and runtime-bearing `using module` / `using assembly` statements, which keep the complete source file on the PowerShell runtime path rather than being silently omitted;
- control flow for which the conservative emitter cannot prove declaration or return behavior.

This boundary is expected to expand through semantic proof, not syntax count. New constructs need differential tests against PowerShell before they become eligible.

## Manifest evidence

Each successful build writes `<name>.powerforge-compilation.json`. The manifest records:

- artifact kind, mode, target framework, and runtime identifier;
- whether PowerShell is required and whether script fallback is used;
- compiled method count, runtime-fallback count, omitted-unit count, and coverage percentage;
- SHA-256 for the primary artifact, portable PDBs, and every distributed runtime or hybrid-module file;
- exact source diagnostics and locations for unsupported units.
- byte sizes for the primary artifact and every durable file;
- executable optimization mode and Authenticode signing evidence when requested.

A packaged EXE therefore reports `requiresPowerShellRuntime: true` and `usesPowerShellRuntimeFallback: true`. A strict CLR library reports both values as `false`. A strict binary module requires PowerShell as its cmdlet host but reports no script fallback.

PowerForge stages the complete owned artifact shape and manifest before publication. Rebuilding under the same artifact name replaces prior EXE, DLL, PDB, module-directory, and manifest state together; same-name publication is serialized across threads and processes, and a failed durable commit rolls back to the previous set instead of leaving a new binary beside stale integrity evidence.

## Measured performance

The checked-in benchmark suite validates every result outside the timed operation and compares typed CLR, generated cmdlet, PowerShell function, typed EXE, packaged EXE, and hand-written C# lanes. It also includes a dispatch-amortization workload that performs equivalent arithmetic through many fine cmdlet calls or one coarse generated command.

- the original PowerShell function;
- the generated binary cmdlet called through PowerShell;
- the generated typed CLR method called inside a C# loop;
- equivalent hand-written C#.

The current Windows computation and startup reference run used PowerShell 7.6.4 on .NET 10.0.11, Windows x64, and an AMD64 32-logical-core machine. Duration rows are medians after three warmups, 12 measured samples, and minimum/maximum exclusion. The startup benchmark used two warmups and 10 measured samples. All rows have zero validation failures and pin clean candidate `dba44037` plus generated artifact hashes.

Windows run IDs are `20260823-155934-e6880101` (real function), `20260823-160044-e1a26fa0` (synthetic loop), `20260823-160046-152c8198` (indexed array), `20260823-160049-aab08ba9` (binary dispatch), and `20260823-160051-940517f3` (startup).

| Workload | Calls | PowerShell | Typed CLR | Hand-written C# | Typed vs PowerShell | Typed vs C# |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Real `Get-AllowedAverageMs`, absolute-cap branch | 50,000 | 251.47 ms | 5.96 ms | 2.37 ms | **42.2x faster** | 2.51x slower |
| Real `Get-AllowedAverageMs`, relative-cap branch | 50,000 | 234.80 ms | 5.89 ms | 2.56 ms | **39.9x faster** | 2.30x slower |
| Synthetic triangular-number loop, 1,000 x 1,000 iterations | 1,000 | 41.47 ms | 5.22 ms | 3.24 ms | **7.9x faster** | 1.61x slower |
| Indexed sum over 1,000-element typed array | 1,000 | 58.73 ms | 6.06 ms | 3.05 ms | **9.7x faster** | 1.99x slower |

These results prove a benefit only for eligible computation executed as CLR code. They do not promise that an arbitrary script or a generated cmdlet call is faster.

The binary-cmdlet lane includes PowerShell command lookup, parameter binding, pipeline setup, and `WriteObject` for every call. It took 1,965.61 ms and 1,934.12 ms in the two 50,000-call real scenarios, versus 251.47 ms and 234.80 ms for the original function. The dispatch-amortization workload then performed equivalent work through 1,000 fine cmdlet calls or one coarse command: 49.86 ms versus 4.94 ms, a **10.1x** improvement. The useful product shape is a coarse cmdlet that performs substantial compiled work per invocation, not a tiny arithmetic cmdlet called in a PowerShell loop.

Executable startup proves that typed compilation changes the product result rather than merely its extension. The PowerShell-free typed EXE took 35.63 ms, `pwsh -File` took 205.75 ms, and the runtime-packaged EXE took 465.31 ms. The typed executable is **5.8x faster than `pwsh -File`** and **13.1x faster than packaging** in this one-shot workload. Packaging remains valuable for broad script compatibility and delivery ergonomics, not startup speed.

The optimization and footprint matrix below was rebuilt and executed with the same clean candidate `dba44037` and the `win-x64` runtime identifier.

| Windows x64 artifact | Bytes | Runtime model |
| --- | ---: | --- |
| Typed framework-dependent EXE | 177,358 | installed .NET |
| Typed self-contained trimmed EXE | 12,912,947 | bundled trimmed .NET runtime |
| Typed NativeAOT EXE | 1,313,792 | native, no .NET or PowerShell runtime required |
| Packaged PowerShell EXE | 54,862,022 | embedded PowerShell runtime assets |

The bounded Linux x64 run used PowerShell 7.5.4 on a .NET 9 host, two warmups, six measured samples for computation, 1,000 real-function calls, and 100 loop/dispatch calls. The supported net8 benchmark artifact produced disclosed CS1701 reference-unification warnings under that intermediate host; all measured operations still validated with zero failures. Run IDs are `20260823-120204-58213403`, `20260823-120307-747e5a55`, `20260823-120317-8a323de6`, and `20260823-120327-7f23ef4c`, pinned to clean candidate `7f6a4160`.

On Linux, coarse binary dispatch took 5.47 ms versus 337.55 ms for repeated fine calls (**61.7x**), typed EXE startup took 68.77 ms versus 204.92 ms for `pwsh -File` and 532.68 ms for packaging, and the artifacts were 86,058 bytes framework-dependent, 13,432,873 bytes trimmed self-contained, 1,710,440 bytes NativeAOT, and 45,368,774 bytes packaged. The Linux typed, trimmed, NativeAOT, and packaged executables were all executed successfully on Linux; this is runtime proof, not Windows cross-publish evidence.

Run the same matrix locally:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\PowerShellCompilation\powershell-compilation.benchmark.ps1 `
    -Variable @{ IncludeOptimizedExecutables = $true } `
    -RunMode standard
```

See [the benchmark README](../Benchmarks/PowerShellCompilation/README.md) for the quick smoke command and lane definitions.

## Real-source eligibility

The canonical census uses exact committed source from six PowerShell-first products. It scans only authored module trees (`Public`, `Private`, and PSSharedGoods `Enums`), not dirty working-tree changes, generated modules, examples, tests, build scripts, or website assets. The pinned inputs are Testimo `f7550cf661ebaf97ae38f96b664aee09efd9cbde`, PSWriteHTML `fa88b1bbecc539b59c9a82cd4b95efc6cc951244`, O365Essentials `fad82882ff116c262ffd3c2c3fdb2781a8ddf0f3`, PSSharedGoods `12e9c2520d347df2988286ea1ba3e81e011ef0de`, ADEssentials `b2b1f760853becb773841f744bea196d02aa6c2b`, and PowerInfoBlox `9de3730afbfd61ed6bec59bc78e9e7a8d91b6233`.

That lane contains 1,249 files and 1,340 whole script/function units with no parse-error files. Before the common-module language slice, one unit compiled. The current candidate compiles eight units (0.597%): one PSWriteHTML unit, six PSSharedGoods units, and PowerInfoBlox `Convert-IpAddressToPtrString`. The PowerInfoBlox helper is also built and invoked as a strict generated binary cmdlet with mandatory parameter metadata, while PSSharedGoods `ConvertFrom-OperationType` is differentially checked for known, case-insensitive, and missing dictionary keys. Testimo, O365Essentials, and ADEssentials remain at zero typed units; their current blockers are reported rather than hidden.

Low initial coverage is not hidden by Hybrid mode. It is written to the manifest, and every fallback has a diagnostic explaining what needs compiler support. Diagnostics are deliberately blocker-masked to avoid cascades, so accepting one outer construct can reveal deeper runtime semantics without increasing coverage. Roadmap priority therefore comes from repeated full-corpus passes and executable differential proof, not raw syntax-occurrence counts.

## Security and distribution limits

Packaging and typed compilation are not obfuscation or source protection. A packaged executable contains an embedded script and runtime assets that a determined user can inspect. A typed EXE or DLL is normal managed/native code and remains analyzable.

`Build-PowerShellArtifact -SignArtifact` and CLI `--sign` sign staged Windows `.exe`, `.dll`, `.ps1`, `.psm1`, and `.psd1` files before their SHA-256 and byte-size evidence is recorded. Signing runs in an isolated Windows PowerShell process with a bounded timeout. A missing certificate, provider timeout, or non-valid signature aborts the atomic publication; no unsigned replacement or stale manifest is committed. Concurrent replacements serialize through a durable per-artifact lock file whose exclusive handle defines ownership across Windows and Unix. The broader PowerForge release pipeline remains the owner for packaging, release attestations, NuGet/GitHub publication, and policy-level signing configuration.

The fail-closed signing and atomic-publication contract is covered by automated tests. On 2026-08-23 the internal acceptance run also produced a valid Authenticode-signed typed EXE, a net8 binary module, and a net472 binary module with the maintainer's code-signing certificate and DigiCert timestamp service. Each staged hash matched the final manifest and each artifact executed successfully in its target host. These were local internal proof artifacts only; nothing was published to PSGallery, NuGet, GitHub Releases, or another feed.

The generated EXE carries the PowerShell SDK, so it is much larger than the input script and may start more slowly than an installed `pwsh`. Self-contained publication adds the .NET runtime as well. With `SingleFile = $false`, PowerForge preserves the complete nested publish tree instead of copying only top-level files. Runtime-packaged artifacts must be rebuilt when their embedded PowerShell or .NET dependencies need security updates.

Typed executable compilation currently accepts one top-level script body and rejects local function declarations. `Hybrid` is not a valid executable mode: choose runtime-preserving `Package` or PowerShell-free `Strict`. Source `#requires` directives and runtime-bearing `using` statements are never erased: Strict typed builds reject them, Hybrid modules retain affected functions on the runtime path, and Hybrid libraries omit them with diagnostics. Hybrid module composition preserves namespace `using` and module `param` prologues for mixed `.ps1` or `.psm1` source. Generated typed export shaping requires literal unconditional exports, including colon-attached literal forms such as `-Function:Get-Value`, and contained relative file references; conditional-only export logic remains in the script fallback and executes unchanged. Strict modules reject `ScriptsToProcess` and script-based `NestedModules`; Hybrid records those hooks as runtime fallback. Required contained assemblies, format files, type files, and scripts must exist; named external assemblies remain manifest references rather than local files. Every staged manifest or dot-source path must remain inside the source root without symbolic-link or junction traversal. Binary-module generation routes non-Verb-Noun or otherwise unrepresentable wrappers to Hybrid script fallback and excludes their methods from the generated CLR assembly; Strict mode rejects them. Generated cmdlet output uses PowerShell's normal collection-enumeration contract rather than treating only arrays as pipelines; `OutputType` advertises an array's element type and uses `object` when an enumerable's element type cannot be proven. A plain CLR library contains only eligible methods and no automatic PowerShell fallback host.

Strict typed executables may request `Trimmed` or `NativeAot` optimization. Both require a RID-specific, self-contained, single-artifact build; NativeAOT already emits the native executable directly and does not enable MSBuild's separate single-file bundler. Packaged PowerShell executables are rejected because trimming a dynamic PowerShell runtime is not a safe default. Native AOT is therefore a deployment option only for the proven typed subset, not a promise that arbitrary PowerShell can be converted to native code.

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

The generated host accepts positional arguments, `--Name value`, `--Name=value`, switches and aliases such as `--Force`, common switches on advanced scripts, and `--` to stop named-argument parsing. A non-switch named parameter must have a value; use `--Name=-value` when that value begins with `-`. Output and information records go to stdout in arrival order, warnings and errors go to stderr, and a top-level explicit `exit <code>` becomes the process exit code. `$PSScriptRoot` resolves to the packaged artifact directory and `$PSCommandPath` to the running artifact path. Packaging rejects `exit` inside a function, nested script block, trap, or caught region because exception instrumentation would change PowerShell behavior.

Compile a strict binary module:

```powershell
powerforge powershell build .\MathTools.psm1 `
    --kind dll `
    --mode Strict `
    --framework net8.0 `
    --out .\artifacts

Import-Module .\artifacts\MathTools.dll
```

When `MathTools.psd1` exists beside `MathTools.psm1`, the primary artifact is a rewritten manifest in a module directory. `RootModule`, `FunctionsToExport`, and `CmdletsToExport` are remapped so a function that became a binary cmdlet keeps the same public name. Literal top-level `Export-ModuleMember` declarations are preserved across typed and fallback commands; dynamic export expressions are rejected.

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

`Hybrid` compiles complete eligible functions and retains diagnostics for everything else. A hybrid binary module removes compiled function definitions from its generated `.psm1`, imports the typed DLL, and keeps unsupported functions on the script path. A hybrid CLR library extracts eligible methods without carrying script fallback because it is intended for direct .NET consumption.

`Strict` fails the build when any executable unit needs fallback. For an executable it compiles one eligible top-level script body into a native .NET entrypoint with no PowerShell SDK dependency. For a DLL it guarantees that the artifact contains only behavior covered by the typed compiler contract.

## Current typed subset

Eligibility is whole-function and intentionally conservative. One unsupported construct keeps the complete function on the PowerShell path.

The first subset supports:

- explicitly typed scalar parameters and one-dimensional typed arrays;
- typed or safely inferred local variables;
- explicit `return` values;
- `if`/`elseif`/`else`, `for`, `while`, and `foreach` over typed arrays;
- Boolean logic and scalar comparisons with known compatible types;
- string equality with PowerShell case-sensitive or case-insensitive behavior;
- floating-point and decimal arithmetic with compatible operands;
- explicitly typed integral accumulators and loop counters with checked assignment semantics;
- unlabeled `break` and `continue` inside supported loops;
- one-dimensional typed-array and string indexing with PowerShell-compatible negative indexes;
- statically resolved CLR constructors, static fields/properties, instance fields/properties, and exact method overloads for supported typed arguments.

Member compilation is intentionally exact. The emitter resolves a single CLR member or overload at build time, applies only supported assignable/numeric/one-character conversions, and falls back when resolution is missing or ambiguous. Generated code can reference only types supplied by its shared-framework reference set; analyzer-host-only assemblies and constructed generic types are rejected before `dotnet` compilation. Null typed arrays preserve PowerShell's zero-length `.Length` behavior, while nullable CLR API results retain failure behavior.

The analyzer rejects dynamic behavior rather than guessing. Current blockers include:

- command, provider, pipeline, and dynamic command invocation;
- dynamic member names, PowerShell-adapted properties, ambiguous overloads, and general object-property semantics;
- script blocks, closures, runtime scopes such as `$env:`, and untyped parameters;
- parameter attributes, default expressions, and `dynamicparam`, `begin`, `process`, or `clean` blocks;
- implicit pipeline output outside the currently supported explicit-return subset;
- PowerShell truthiness conversions, element-wise array comparison, and coercion between incompatible CLR types;
- string relational operators whose culture-aware ordering has not yet been translated;
- explicit conversion expressions, heterogeneous branch return types, and integral division whose PowerShell result type depends on the quotient;
- untyped integral arithmetic that can change CLR type after overflow;
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

PowerForge stages the complete owned artifact shape and manifest before publication. Rebuilding under the same artifact name replaces prior EXE, DLL, PDB, module-directory, and manifest state together; a failed durable commit rolls back to the previous set instead of leaving a new binary beside stale integrity evidence.

## Measured performance

The checked-in benchmark suite validates every result outside the timed operation and compares typed CLR, generated cmdlet, PowerShell function, typed EXE, packaged EXE, and hand-written C# lanes. It also includes a dispatch-amortization workload that performs equivalent arithmetic through many fine cmdlet calls or one coarse generated command.

- the original PowerShell function;
- the generated binary cmdlet called through PowerShell;
- the generated typed CLR method called inside a C# loop;
- equivalent hand-written C#.

The current Windows computation and startup reference run used PowerShell 7.6.4 on .NET 10.0.11, Windows x64, and an AMD64 32-logical-core machine. Duration rows are medians after three warmups, 12 measured samples, and minimum/maximum exclusion. The startup benchmark used two warmups and 10 measured samples. All rows have zero validation failures and pin clean candidate `dbf5c14a` plus generated artifact hashes.

Windows run IDs are `20260823-140734-1b40d8c8` (real function), `20260823-140840-4e52f51a` (synthetic loop), `20260823-140843-45f4dbe5` (indexed array), `20260823-140845-f6ded219` (binary dispatch), and `20260823-140847-48d3c89f` (startup).

| Workload | Calls | PowerShell | Typed CLR | Hand-written C# | Typed vs PowerShell | Typed vs C# |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Real `Get-AllowedAverageMs`, absolute-cap branch | 50,000 | 206.63 ms | 5.29 ms | 2.24 ms | **39.1x faster** | 2.37x slower |
| Real `Get-AllowedAverageMs`, relative-cap branch | 50,000 | 226.45 ms | 5.53 ms | 2.40 ms | **40.9x faster** | 2.31x slower |
| Synthetic triangular-number loop, 1,000 x 1,000 iterations | 1,000 | 55.68 ms | 5.27 ms | 3.07 ms | **10.6x faster** | 1.72x slower |
| Indexed sum over 1,000-element typed array | 1,000 | 50.55 ms | 4.43 ms | 2.37 ms | **11.4x faster** | 1.87x slower |

These results prove a benefit only for eligible computation executed as CLR code. They do not promise that an arbitrary script or a generated cmdlet call is faster.

The binary-cmdlet lane includes PowerShell command lookup, parameter binding, pipeline setup, and `WriteObject` for every call. It took 1,803.54 ms and 1,742.44 ms in the two 50,000-call real scenarios, versus 206.63 ms and 226.45 ms for the original function. The dispatch-amortization workload then performed equivalent work through 1,000 fine cmdlet calls or one coarse command: 40.94 ms versus 4.18 ms, a **9.8x** improvement. The useful product shape is a coarse cmdlet that performs substantial compiled work per invocation, not a tiny arithmetic cmdlet called in a PowerShell loop.

Executable startup proves that typed compilation changes the product result rather than merely its extension. The PowerShell-free typed EXE took 34.36 ms, `pwsh -File` took 210.70 ms, and the runtime-packaged EXE took 454.44 ms. The typed executable is **6.1x faster than `pwsh -File`** and **13.2x faster than packaging** in this one-shot workload. Packaging remains valuable for broad script compatibility and delivery ergonomics, not startup speed.

The optimization and footprint matrix below was measured separately on clean candidate `7f6a4160`; the current standard refresh intentionally reused the already-proven artifact modes rather than rebuilding large trimmed and NativeAOT outputs.

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

A scan of TestimoX commit `02b1755109e1661347593a115c1b5ba67132b404` covered 1,665 PowerShell files and 2,086 executable units. The current conservative subset accepts seven units (0.336%): one representative logon script, the two `Get-AllowedAverageMs` benchmark-gate functions, and four real path/administrator helpers. Typed indexing removed `IndexExpressionAst` as a blocker, but it did not increase whole-function eligibility at this commit because every indexed unit still has another unsupported construct. The corpus result is therefore reported as unchanged rather than counting partial syntax support as a performance win.

Low initial coverage is not hidden by Hybrid mode. It is written to the manifest, and every fallback has a diagnostic explaining what needs compiler support. The next useful compiler work is driven by recurring blocker classes in real repositories rather than making isolated syntax examples turn green.

## Security and distribution limits

Packaging and typed compilation are not obfuscation or source protection. A packaged executable contains an embedded script and runtime assets that a determined user can inspect. A typed EXE or DLL is normal managed/native code and remains analyzable.

`Build-PowerShellArtifact -SignArtifact` and CLI `--sign` sign staged Windows `.exe`, `.dll`, `.ps1`, `.psm1`, and `.psd1` files before their SHA-256 and byte-size evidence is recorded. Signing runs in an isolated Windows PowerShell process with a bounded timeout. A missing certificate, provider timeout, or non-valid signature aborts the atomic publication; no unsigned replacement or stale manifest is committed. The broader PowerForge release pipeline remains the owner for packaging, release attestations, NuGet/GitHub publication, and policy-level signing configuration.

The fail-closed signing and atomic-publication contract is covered by automated tests. On 2026-08-23 the internal acceptance run also produced a valid Authenticode-signed typed EXE, a net8 binary module, and a net472 binary module with the maintainer's code-signing certificate and DigiCert timestamp service. Each staged hash matched the final manifest and each artifact executed successfully in its target host. These were local internal proof artifacts only; nothing was published to PSGallery, NuGet, GitHub Releases, or another feed.

The generated EXE carries the PowerShell SDK, so it is much larger than the input script and may start more slowly than an installed `pwsh`. Self-contained publication adds the .NET runtime as well. With `SingleFile = $false`, PowerForge preserves the complete nested publish tree instead of copying only top-level files. Runtime-packaged artifacts must be rebuilt when their embedded PowerShell or .NET dependencies need security updates.

Typed executable compilation currently accepts one top-level script body and rejects local function declarations. Hybrid module composition preserves `using` and module `param` prologues for mixed `.ps1` or `.psm1` source. Generated typed export shaping requires literal unconditional exports and contained relative file references; conditional-only export logic remains in the script fallback and executes unchanged. Strict modules reject `ScriptsToProcess` and script-based `NestedModules`; Hybrid records those hooks as runtime fallback. Binary-module generation also rejects function parameters that collide with PowerShell common or optional common parameters. A plain CLR library contains only eligible methods and no automatic PowerShell fallback host.

Strict typed executables may request `Trimmed` or `NativeAot` optimization. Both require a RID-specific, self-contained, single-artifact build; NativeAOT already emits the native executable directly and does not enable MSBuild's separate single-file bundler. Packaged PowerShell executables are rejected because trimming a dynamic PowerShell runtime is not a safe default. Native AOT is therefore a deployment option only for the proven typed subset, not a promise that arbitrary PowerShell can be converted to native code.

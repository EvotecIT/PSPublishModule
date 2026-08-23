# PowerShell Compilation

PowerForge can turn a `.ps1`, `.psm1`, `.psd1`, or conventional module directory into three different artifact shapes:

- a packaged executable that preserves dynamic PowerShell semantics, or a genuinely typed executable for an eligible top-level script;
- an importable binary or hybrid module whose eligible functions are compiled to typed CLR methods;
- a plain CLR library containing eligible typed methods for use from .NET.

These are deliberately separate claims. Packaging makes a script easier to distribute, but does not make its body faster. Typed compilation removes PowerShell's dynamic execution path for a conservative function subset and can improve CPU-bound code. Every build writes a JSON manifest that says which path was used.

## Build artifacts

| Kind | Default mode | Result | PowerShell required at runtime | Typed speedup expected |
| --- | --- | --- | --- | --- |
| `Executable` / `exe` | `Package` | Single-file host with embedded script and PowerShell SDK | Embedded in the application | No |
| `Executable` / `exe` | `Strict` | Runtime-independent typed .NET executable | No | Yes, for eligible CPU-bound work and process startup |
| `BinaryModule` / `dll` | `Strict` (explicit) | Importable DLL when every function compiles | Yes, as the cmdlet host | Only inside sufficiently coarse compiled work |
| `BinaryModule` / `dll` | `Hybrid` (default) | Module folder with a typed DLL and `.psm1` fallback | Yes | For eligible functions; unsupported functions remain scripts |
| `Library` / `library` | `Hybrid` | CLR DLL with eligible public static methods | No | Yes, when called as CLR code |

Supported target frameworks are:

- executable: `net8.0`, `net10.0`;
- CLR library or binary module: `net472`, `net8.0`, `net10.0`.

The `net472` binary-module lane is tested by importing and invoking the generated DLL in Windows PowerShell 5.1. The modern lanes run in PowerShell 7.

## Use the CLI

For the common case, point PowerForge at the module directory. It selects the matching top-level manifest and root module, infers a hybrid binary-module build, and writes to the module's `artifacts` directory:

```powershell
powerforge powershell build .\MyModule --emit-source
```

The accepted input shapes are:

- `.ps1`: defaults to a packaged executable;
- several loose `.ps1` files: default to a Strict typed library; add `--kind dll` to expose their functions as real binary cmdlets;
- `.psm1`: defaults to a hybrid binary module and uses a same-name sibling `.psd1` when present;
- `.psd1`: resolves a literal `.psm1` `RootModule`;
- directory: prefers a manifest matching the directory name, otherwise accepts one unambiguous top-level manifest or script module.

Directory discovery does not recurse into samples, tests, or nested modules looking for an entrypoint. Multiple plausible top-level entries fail with their candidate names. A manifest root currently needs to be a `.psm1` beside the `.psd1`; an existing binary `RootModule` is rejected because it is already compiled input. A same-name sibling manifest is accepted only when its `RootModule` points back to the selected `.psm1`. Use `--kind` and `--mode` only when overriding the inferred artifact shape or fallback policy.

Unconditional top-level literal `$PSScriptRoot` dot-sourced files share the root module's compilation scope. The resolver also recognizes conventional top-level `Get-ChildItem $PSScriptRoot\Public\*.ps1 -Recurse` / `Private` loader declarations and their `$Import.FullName` dot-source loop without executing module code. Eligible functions in discovered `Public` or `Private` files can therefore become binary cmdlets, while unsupported functions remain in their staged script files. Conditional and function-local dot-sources, `ScriptsToProcess`, and script-based nested modules stay runtime content because PowerShell gives them different scope and loading semantics.

Module inputs cannot be overridden to `Executable`. Use a standalone `.ps1` as the executable entrypoint or build the module as `BinaryModule`; PowerForge does not invent module-to-application startup semantics.

Compile several standalone files without creating a manifest or build configuration:

```powershell
powerforge powershell build .\Public\Get-One.ps1 `
    --path .\Public\Get-Two.ps1 `
    --kind dll `
    --out .\artifacts `
    --emit-source

Build-PowerShellArtifact `
    -Path .\Public\Get-One.ps1, .\Public\Get-Two.ps1 `
    -Kind BinaryModule `
    -EmitSource
```

Loose binary-module file sets are Strict by default because there is no `.psm1` entrypoint in which unsupported functions could remain as fallback. All files must be contained by the first file's directory. A multi-file executable is deliberately rejected for now: an EXE has one `Main`, so that feature needs an explicit entrypoint plus dependency bundling rather than a first-file guess.

Analyze a file or a complete source tree before building:

```powershell
powerforge powershell analyze .\MyModule --mode Hybrid
```

Package a script as an executable:

```powershell
powerforge powershell build .\Invoke-Report.ps1 `
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

The generated host accepts positional arguments, `--Name value`, `--Name=value`, switches and aliases such as `--Force`, common switches on advanced scripts, and `--` to stop named-argument parsing. A non-switch named parameter must have a value; use `--Name=-value` when that value begins with `-`. Pipeline objects use PowerShell's normal formatting system before going to stdout; information records also go to stdout, while warnings and errors go to stderr. Nonterminating error records do not by themselves change a successful process exit code; a top-level explicit `exit <code>` becomes the process exit code, and a terminating exception fails the process. `$PSScriptRoot` resolves to the packaged artifact directory and `$PSCommandPath` to the running artifact path. Packaging rejects `exit` inside a function, nested script block, trap, or caught region because exception instrumentation would change PowerShell behavior. It also rejects `using module` and `using assembly` because those directives are resolved before an embedded script can receive file-backed path metadata.

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

Build a hybrid module when only part of the source is eligible. The kind and mode are inferred from the module input:

```powershell
powerforge powershell build .\Operations `
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
    -Path .\Operations `
    -EmitSource
```

It supports `-WhatIf`, returns `PowerShellCompilationBuildResult`, and uses the same discovery, defaults, overrides, and manifests as the CLI. `-Kind`, `-Mode`, `-Name`, and `-OutputDirectory` remain available when the inferred values are not the desired artifact.

## Modes

`Analyze` parses source and reports one decision per top-level script body or function. It produces no artifact.

`Package` preserves dynamic PowerShell behavior. The current executable lane embeds the source script and PowerShell SDK in a generated .NET host. It is a distribution feature, not typed compilation.

`Hybrid` compiles complete eligible functions and retains diagnostics for everything else. A hybrid binary module removes compiled function definitions from its generated `.psm1`, imports the typed DLL, and keeps unsupported functions on the script path. Literal `$PSScriptRoot` dot-source dependencies are staged recursively with their relative layout, including dependencies reached from manifest runtime hooks. Dynamic, missing, wildcard, working-directory-relative, source-root-escaping, or symbolic-link/junction paths fail before publication. A hybrid CLR library extracts eligible methods without carrying script fallback because it is intended for direct .NET consumption.

Only unconditional top-level literal dot-sourced files participate in the root module's typed source set. Conditional and function-local dot-sources plus manifest runtime hooks are still discovered and staged, but are counted as runtime fallback rather than being flattened into a different scope. Nested script modules and nested manifests keep their relative layout, manifest closure, and export policy.

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
- expandable strings containing statically typed string variables, with null strings rendered as empty text;
- lookup-only, case-insensitive string dictionaries created from homogeneous string hashtable literals;
- empty `CmdletBinding()` metadata and `Parameter(Mandatory)` metadata, with mandatory binding preserved by generated binary cmdlets;
- floating-point and decimal arithmetic with compatible operands;
- explicitly typed integral accumulators and loop counters with checked assignment semantics;
- unlabeled `break` and `continue` inside supported loops;
- one-dimensional typed-array and string indexing with PowerShell-compatible negative and missing-index behavior for direct returns and compound-assignment operands;
- statically resolved CLR constructors, static fields/properties, instance fields/properties, and exact method overloads for supported typed arguments, including defined enum names supplied as string literals.

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
- the resolved root and all authored files in the shared compilation scope;
- whether PowerShell is required and whether script fallback is used;
- compiled method count, runtime-fallback count, omitted-unit count, and coverage percentage;
- SHA-256 for the primary artifact, portable PDBs, and every distributed runtime or hybrid-module file;
- exact source diagnostics and locations for unsupported units;
- byte sizes for the primary artifact and every durable file;
- executable optimization mode and Authenticode signing evidence when requested.

A packaged EXE therefore reports `requiresPowerShellRuntime: true` and `usesPowerShellRuntimeFallback: true`. A strict CLR library reports both values as `false`. A strict binary module requires PowerShell as its cmdlet host but reports no script fallback.

PowerForge stages the complete owned artifact shape and manifest before publication. Rebuilding under the same artifact name replaces prior EXE, DLL, PDB, module-directory, generated-source directory, and manifest state together; same-name publication is serialized across threads and processes, and a failed durable commit rolls back to the previous set instead of leaving a new binary beside stale integrity evidence.

Add `--emit-source` or `-EmitSource` to publish `<name>.generated` as part of the same atomic artifact set. It contains the exact generated `.cs` files, `.csproj`, and a `source-map.json` that maps each generated method to its authored file and line; packaged executables also include the rewritten embedded `Source.ps1`. Generated PowerShell-SDK projects pin the applicable serviced `System.Security.Cryptography.Xml` line (`8.0.4` for net8 and `10.0.11` for net10) so an independently restored inspection build does not fall back to the vulnerable transitive version currently carried by the SDK. The project can be inspected or rebuilt directly:

```powershell
dotnet build .\artifacts\MyModule.generated\MyModule.csproj -c Release
```

Every emitted source file is listed with its role, SHA-256, and size in the compilation manifest. The emitted project includes local `Directory.Build.*`, `Directory.Packages.props`, and `global.json` isolation files so an ancestor repository's MSBuild, central-package, or SDK policy cannot silently change the inspection rebuild. Rebuilding the artifact without source emission removes a prior generated-source directory so stale C# cannot be mistaken for the current binary.

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

The real PowerInfoBlox IPv4-to-PTR helper was measured separately on clean candidate `cf61babd` with 10,000 calls, two warmups, six measured samples, and minimum/maximum exclusion. Run `20260823-173135-cde90279` completed with zero validation failures: PowerShell took 102.81 ms, generated typed CLR 6.34 ms, and hand-written C# 4.41 ms. The generated method was **16.2x faster than PowerShell** and 1.44x slower than the hand-written implementation. Calling the tiny generated binary cmdlet 10,000 times took 356.46 ms, reinforcing that cmdlets should perform coarser work per dispatch.

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

That lane contains 1,249 files and 1,340 whole script/function units with no parse-error files. Before the common-module language slice, one unit compiled. The current candidate compiles nine units (0.672%): one PSWriteHTML unit, six PSSharedGoods units, PowerInfoBlox `Convert-IpAddressToPtrString`, and O365Essentials `Get-ProcessEnvironmentValue`. The PowerInfoBlox helper is also built and invoked as a strict generated binary cmdlet with mandatory parameter metadata, while PSSharedGoods `ConvertFrom-OperationType` is differentially checked for known, case-insensitive, and missing dictionary keys. O365 enum-name overload binding was verified against the original private function. Testimo and ADEssentials remain at zero typed units; their current blockers are reported rather than hidden.

Low initial coverage is not hidden by Hybrid mode. It is written to the manifest, and every fallback has a diagnostic explaining what needs compiler support. Diagnostics are deliberately blocker-masked to avoid cascades, so accepting one outer construct can reveal deeper runtime semantics without increasing coverage. Roadmap priority therefore comes from repeated full-corpus passes and executable differential proof, not raw syntax-occurrence counts.

### Real product rebuild matrix

The module rebuilder was then run from each repository directory using archived committed source, not the maintainers' working trees. All five generated modules imported successfully, preserved the complete exported command-name and alias surface, and produced independently rebuildable emitted C# projects with zero NuGet audit warnings and zero vulnerable packages reported by `dotnet list package --vulnerable --include-transitive`.

| Product | Source files | Units | Typed / fallback | Coverage | Exported surface after rebuild | Complete artifact set | Generated C# |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| PowerInfoBlox `9de3730` | 58 | 58 | 1 / 57 | 1.72% | 45 functions, 6 aliases | 207,473 bytes | 2 files, 39 lines, 1 mapped method |
| PSSharedGoods `12e9c25` | 283 | 285 | 6 / 279 | 2.11% | 186 functions + 5 cmdlets, 35 aliases | 1,347,652 bytes | 2 files, 136 lines, 6 mapped methods |
| PSWriteHTML `fa88b1b` | 242 | 323 | 1 / 322 | 0.31% | 152 functions + 1 cmdlet, 138 aliases | 1,438,860 bytes | 2 files, 30 lines, 1 mapped method |
| O365Essentials `fad8288` | 284 | 286 | 1 / 285 | 0.35% | 213 functions, 1 alias | 759,908 bytes | 2 files, 36 lines, 1 mapped method |
| ADEssentials `b2b1f76` | 265 | 270 | 0 / 270 | 0% | 133 functions, 24 aliases | 1,892,221 bytes | 2 files, 16 lines |

The function/cmdlet split is intentional: for example, PSSharedGoods begins with 191 exported functions and finishes with the same 191 command names, but five eligible functions are now real cmdlets. Differential execution matched for PowerInfoBlox `Convert-IpAddressToPtrString`, PSSharedGoods `ConvertFrom-OperationType`, PSWriteHTML `New-HTMLCarouselStyle`, and O365Essentials `Get-ProcessEnvironmentValue`.

A small seven-sample, 10,000-call dispatch probe measured rebuilt/original medians of 204.30/251.44 ms for PowerInfoBlox (1.23x), 143.90/160.04 ms for PSSharedGoods (1.11x), and 215.20/224.98 ms for PSWriteHTML (1.05x). These are intentionally modest: they measure repeated PowerShell command dispatch, not direct CLR execution. The clean direct-CLR PowerInfoBlox benchmark remains 16.2x faster than its PowerShell function. Together the results point to the next optimization target: compile coarser command regions so useful work amortizes binding and pipeline dispatch.

## Security and distribution limits

Packaging and typed compilation are not obfuscation or source protection. A packaged executable contains an embedded script and runtime assets that a determined user can inspect. A typed EXE or DLL is normal managed/native code and remains analyzable.

`Build-PowerShellArtifact -SignArtifact` and CLI `--sign` sign only build-owned Windows artifacts: the generated executable or library, typed assembly, hybrid module host, and generated primary module manifest. Bundled runtime files and nested/module dependencies keep their original publisher identity. Signing happens before SHA-256 and byte-size evidence is recorded and runs in an isolated Windows PowerShell process with a bounded timeout. A missing certificate, provider timeout, or non-valid signature aborts the atomic publication; no unsigned replacement or stale manifest is committed. Concurrent replacements serialize through a durable per-artifact lock file whose exclusive handle defines ownership across Windows and Unix. The broader PowerForge release pipeline remains the owner for packaging, release attestations, NuGet/GitHub publication, and policy-level signing configuration.

The fail-closed signing and atomic-publication contract is covered by automated tests. On 2026-08-23 the internal acceptance run also produced a valid Authenticode-signed typed EXE, a net8 binary module, and a net472 binary module with the maintainer's code-signing certificate and DigiCert timestamp service. Each staged hash matched the final manifest and each artifact executed successfully in its target host. These were local internal proof artifacts only; nothing was published to PSGallery, NuGet, GitHub Releases, or another feed.

The generated EXE carries the PowerShell SDK, so it is much larger than the input script and may start more slowly than an installed `pwsh`. Self-contained publication adds the .NET runtime as well. With `SingleFile = $false`, PowerForge preserves the complete nested publish tree instead of copying only top-level files. Runtime-packaged artifacts must be rebuilt when their embedded PowerShell or .NET dependencies need security updates.

Typed executable compilation currently accepts one top-level script body and rejects local function declarations. `Hybrid` is not a valid executable mode: choose runtime-preserving `Package` or PowerShell-free `Strict`. Source `#requires` directives and runtime-bearing `using` statements are never erased: Strict typed builds reject them, Hybrid modules retain affected functions on the runtime path, and Hybrid libraries omit them with diagnostics. Hybrid module composition preserves namespace `using` and module `param` prologues for mixed `.ps1` or `.psm1` source. Generated typed export shaping requires literal unconditional exports, including colon-attached literal forms such as `-Function:Get-Value`, and contained relative file references; conditional-only export logic remains in the script fallback and executes unchanged. Strict modules reject `ScriptsToProcess` and script-based `NestedModules`; Hybrid records those hooks as runtime fallback. Required contained assemblies, format files, type files, and scripts must exist; named external assemblies remain manifest references rather than local files. Every staged manifest or dot-source path must remain inside the source root without symbolic-link or junction traversal. Binary-module generation routes non-Verb-Noun or otherwise unrepresentable wrappers to Hybrid script fallback and excludes their methods from the generated CLR assembly; Strict mode rejects them. Generated cmdlet output uses PowerShell's normal collection-enumeration contract rather than treating only arrays as pipelines; `OutputType` advertises an array's element type and uses `object` when an enumerable's element type cannot be proven. Expandable strings currently accept string variables only; subexpressions, non-string runtime conversion, and mixed escaped-dollar interpolation remain fallback. Enum arguments accept only defined names from literal strings. Null-to-reference overload binding remains fallback because PowerShell may convert null to an empty string where direct CLR would preserve null. A plain CLR library contains only eligible methods and no automatic PowerShell fallback host.

Strict typed executables may request `Trimmed` or `NativeAot` optimization. Both require a RID-specific, self-contained, single-artifact build; NativeAOT already emits the native executable directly and does not enable MSBuild's separate single-file bundler. Packaged PowerShell executables are rejected because trimming a dynamic PowerShell runtime is not a safe default. Native AOT is therefore a deployment option only for the proven typed subset, not a promise that arbitrary PowerShell can be converted to native code.

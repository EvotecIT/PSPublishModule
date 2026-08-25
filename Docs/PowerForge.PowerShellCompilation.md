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
| `Executable` / `exe` | `Strict` | PowerShell-free typed .NET executable | No | Yes, for eligible CPU-bound work and process startup |
| `BinaryModule` / `dll` | `Strict` (explicit) | Importable DLL when every function compiles | Yes, as the cmdlet host | Only inside sufficiently coarse compiled work |
| `BinaryModule` / `dll` | `Hybrid` (default) | Module folder with a typed DLL and `.psm1` fallback | Yes | For eligible functions; unsupported functions remain scripts |
| `Library` / `library` | `Hybrid` | CLR DLL with eligible public static methods | No | Yes, when called as CLR code |

Supported target frameworks are:

- executable: `net8.0`, `net10.0`;
- CLR library or binary module: `net472`, `net8.0`, `net10.0`.

The `net472` binary-module lane is tested by importing and invoking the generated DLL in Windows PowerShell 5.1. The modern lanes run in PowerShell 7.

### Runtime and deployment profiles

The target framework and publication profile determine what must already exist on the destination computer. An installed `powershell.exe` or `pwsh` is not used as the runtime for a generated executable.

| Artifact | Target | PowerShell engine used | Destination requirement |
| --- | --- | --- | --- |
| Package EXE | `net8.0` | Embedded PowerShell SDK 7.4.18 | .NET 8 for a framework-dependent build; nothing separately installed for a self-contained build |
| Package EXE | `net10.0` | Embedded PowerShell SDK 7.6.4 | .NET 10 for a framework-dependent build; nothing separately installed for a self-contained build |
| Strict EXE | `net8.0` or `net10.0` | None | Matching .NET runtime for a framework-dependent build; nothing separately installed for a self-contained or NativeAOT build |
| Binary module | `net472` | Windows PowerShell 5.1 Desktop host | Windows PowerShell 5.1 and its .NET Framework runtime |
| Binary module | `net8.0` | PowerShell 7.4 Core host | A compatible PowerShell 7.4 host, which supplies .NET 8 |
| Binary module | `net10.0` | PowerShell 7.6 Core host | A compatible PowerShell 7.6 host, which supplies .NET 10 |
| CLR library | `net472`, `net8.0`, or `net10.0` | None | A consuming process on the matching CLR family |

Framework-dependent executables are the smallest normal build, but require the matching .NET runtime. Self-contained builds carry that runtime and are therefore larger and platform-specific. Single-file publication still targets one runtime identifier such as `win-x64` or `linux-x64`; it does not make one binary portable across operating systems. NativeAOT removes both the PowerShell and installed-.NET requirements, but is available only to Strict typed executables and must be built for each target platform and architecture.

PowerShell 5.1 compatibility currently means a `net472` generated binary module loaded by Windows PowerShell. PowerForge does not currently produce a Windows PowerShell 5.1 packaged EXE. A Strict EXE is also not a hidden choice between PowerShell 5.1, 7.4, and 7.6: no PowerShell engine runs after a successful Strict compilation.

### Choosing the runtime model

| Model | Best fit | Compatibility boundary | Distribution and security tradeoff |
| --- | --- | --- | --- |
| Package EXE | Existing scripts that need broad dynamic PowerShell behavior | Runs the embedded script through the bundled PowerShell SDK | Largest artifact and attack surface; embedded source is inspectable; rebuild when bundled PowerShell or .NET dependencies need security updates; generated-host similarity can contribute to antivirus reputation or heuristic detections |
| Strict EXE | Deliberately typed utilities whose complete reachable program fits the supported compiler contract | Every reachable entrypoint statement and local function must compile | No PowerShell runtime or embedded script; smaller framework-dependent and NativeAOT options; still ordinary analyzable code and not immune to antivirus false positives |
| Hybrid binary module | Real modules with a mixture of compiler-friendly and dynamic functions | Eligible functions become cmdlets while unsupported functions continue through PowerShell | Preserves broad module behavior, but requires a matching PowerShell host and carries the combined maintenance surface of generated code plus retained scripts |
| Strict binary module | Modules intentionally constrained to the typed subset | Every exported implementation must compile, while PowerShell remains the cmdlet host | No script fallback, but still depends on the target PowerShell/.NET host contract |
| CLR library | Typed functions intended for direct .NET consumption | Only eligible methods are emitted; no PowerShell fallback is carried | Normal managed-library deployment and analysis rules apply |

Code signing establishes publisher identity and artifact integrity; it does not make arbitrary generated programs inherently trustworthy to antivirus products. Use a certificate only for code owned and distributed by that certificate's publisher. Do not submit private packaged executables to public malware-analysis services unless sharing the embedded source and dependencies with that service is acceptable.

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

Loose binary-module file sets are Strict by default because there is no `.psm1` entrypoint in which unsupported functions could remain as fallback. All files must be contained by the first file's directory.

A multi-file executable has one explicit root script. The root `param()` block remains the process argument contract, and PowerForge follows reachable unconditional literal dot-sources recursively from that entrypoint. Every supplied `--path` must belong to that contained dependency closure; unrelated files are rejected instead of being bundled speculatively:

```powershell
powerforge powershell build .\Tool.ps1 `
    --path .\Private\Helpers.ps1 `
    --entry-point .\Tool.ps1 `
    --kind exe `
    --mode Package `
    --out .\artifacts

Build-PowerShellArtifact `
    -Path .\Tool.ps1, .\Private\Helpers.ps1 `
    -EntryPoint .\Tool.ps1 `
    -Kind Executable `
    -Mode Package
```

The dependency tree is extracted with its relative layout before the embedded script runs, so `$PSScriptRoot`, `$PSCommandPath`, and nested literal dot-sources retain file-backed behavior. Dynamic, escaping, missing, or linked dependencies fail closed. Multi-file Strict EXEs remain unsupported until local PowerShell function declarations and calls have a typed entrypoint contract.

### Dependencies and runtime resources

`analyze` and every successful build manifest include a detailed dependency/resource plan, while `census` aggregates the same evidence for each product. Detailed items record their stable kind, discovery source, selection reason, relative path, byte size, existence, and artifact disposition. The resource summary reports included, excluded, required, inferred, and unclassified file counts and sizes, so a successful-looking binary cannot silently lose files its source expected.

| Input dependency | BinaryModule output | Package EXE | Strict EXE | CLR Library |
| --- | --- | --- | --- | --- |
| Root source and literal `.ps1` closure | Eligible functions compile; Hybrid script remains beside the DLL | Root is embedded; reachable dot-sources are embedded and extracted with their relative layout | Complete reachable graph must compile | Eligible functions compile; unsupported functions are omitted in Hybrid |
| `FormatsToProcess`, `TypesToProcess`, `ScriptsToProcess`, local `RequiredAssemblies`, local `NestedModules`, and `FileList` | Contained manifest closure is copied with its relative layout; Strict rejects script runtime hooks | Module inputs are not executable entrypoints | Module inputs are not executable entrypoints | Non-script required files are copied beside the DLL |
| Explicit `IncludeResource` / `--include-resource` | Copied with its source-root-relative path | Embedded and extracted into a contained source-root-relative runtime layout | Copied beside the EXE | Copied beside the DLL |
| High-confidence literal `$PSScriptRoot` file path | Inferred and copied in `Declared` mode | Inferred, embedded, and extracted in `Declared` mode | Inferred and copied in `Declared` mode | Inferred and copied in `Declared` mode |
| Optional module-root payload | Included only in `CompleteModule` mode or by an explicit declaration | A single script never sweeps sibling content | A single script never sweeps sibling content | Included only in `CompleteModule` mode or by an explicit declaration |
| `RequiredModules` and named external `RequiredAssemblies` | Preserved as external host requirements; not embedded | Not resolved or bundled automatically | Not resolved or bundled automatically | Remain consumer requirements |

A generated artifact with selected payload is an artifact set, not necessarily one physical file. Binary modules, Strict EXEs, and CLR libraries copy selected CSS, JavaScript, images, managed assemblies, native libraries, templates, data, manifests, and type/format data beside the primary artifact with their relative paths intact. Package EXEs instead embed selected resources and extract them with the reachable dot-source closure into a private contained runtime layout. Exact inferred resource references are rewritten to that layout. An explicit include or `CompleteModule` selection also gives the packaged root script the extracted `$PSScriptRoot`, so declared dynamic paths such as `Join-Path $PSScriptRoot $name` resolve without leaving sidecars beside the durable EXE. `$PSCommandPath` remains the running artifact path, and parameter defaults retain EXE-backed path metadata. PowerForge signs only build-owned generated files; adjacent vendor assemblies retain their publisher identity. `SingleFile` includes Package-mode selected resources, while adjacent Strict/DLL payload remains a multi-file artifact set.

Resource selection is policy-driven:

- `Declared` is the default. It includes manifest-required files, explicit includes, and high-confidence contained file literals such as `Get-Content "$PSScriptRoot/Templates/report.html"`.
- `CompleteModule` includes all contained module-root payload except explicit exclusions. Use it for a staged module directory, not an unchecked repository root.
- `None` disables inference and broad optional selection; manifest-required files and explicit includes still apply.
- `FileList` is authoritative. A missing entry or an exclusion that matches a manifest-required file fails closed.
- `IncludeResource` and `ExcludeResource` accept contained paths, directories, and `*`, `?`, or `**` globs. An unmatched pattern, include/exclude collision, link, root escape, case collision, or selected output overlap fails with a diagnostic.
- `Resources`, `Resource`, `Lib`, `Libraries`, and `runtimes` are classification hints only. `Vendor`, `Templates`, `Web`, `Data`, or any other folder works the same way.
- Dynamic resource paths are left unclassified and require an explicit include. In a Package EXE, that declaration also selects extracted-root semantics for the script body; undeclared dynamic paths continue to refer to the durable artifact directory. A single `.ps1` build never sweeps neighboring folders automatically.

For example:

```powershell
powerforge powershell analyze .\MyModule `
    --include-resource 'Templates/**' `
    --include-resource 'Vendor' `
    --exclude-resource 'Vendor/**/*.pdb'

Build-PowerShellArtifact -Path .\MyModule `
    -ResourceMode Declared `
    -IncludeResource 'Templates/**', 'Vendor' `
    -ExcludeResource 'Vendor/**/*.pdb'
```

PowerForge does not turn a module into an EXE or infer an application entrypoint from exported functions. A standalone `.ps1` that imports another module does not cause that module and its complete resource tree to be bundled; module dependency acquisition remains an explicit deployment concern.

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

Strict typed executables accept explicitly typed scalar, switch, and one-dimensional array parameters. Their generated CLI preserves required parameters, aliases, `AllowNull`, and the supported `ValidateNotNull`, `ValidateNotNullOrEmpty`, `ValidateSet`, `ValidateRange`, and `ValidatePattern` metadata. It supports exact names, aliases, unambiguous abbreviations, positional values, repeated array options, `--Name value`, `--Name=value`, switches, and `--`. Missing, duplicate, ambiguous, unknown, or validation-failing parameters are rejected before invoking compiled code.

The generated host accepts positional arguments, `--Name value`, `--Name=value`, switches and aliases such as `--Force`, common switches on advanced scripts, and `--` to stop named-argument parsing. A non-switch named parameter must have a value; use `--Name=-value` when that value begins with `-`. Pipeline objects use PowerShell's normal formatting system before going to stdout; information records also go to stdout, while warnings and errors go to stderr. Nonterminating error records do not by themselves change a successful process exit code; a top-level explicit `exit <code>` becomes the process exit code, and a terminating exception fails the process. `$PSCommandPath` resolves to the running artifact path. `$PSScriptRoot` normally resolves to the durable artifact directory, while a Package build with explicit or complete-module resources intentionally resolves the script body against the private extracted root; parameter-binding path metadata remains artifact-backed. Packaging rejects `exit` inside a function, nested script block, trap, or caught region because exception instrumentation would change PowerShell behavior. It also rejects `using module` and `using assembly` because those directives are resolved before an embedded script can receive file-backed path metadata.

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

Add `--output json` to either `analyze` or `build` for a stable machine-readable envelope. Analyzer diagnostics include a stable `featureId`, while `dependencies` explains what the selected artifact shape will compile, preserve, copy, embed, leave external, or reject.

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

Binary modules can also compile typed control flow around deliberately bounded PowerShell command regions. Direct `Write-Verbose`, `Write-Debug`, and `Write-Warning` calls use the generated `PSCmdlet` stream APIs. Adjacent top-level command pipelines, parameter-only conditional command blocks, explicit discard assignments such as `$null = Invoke-Operation`, and a safe terminal command-result tail are grouped into one PowerShell invocation so binding and dispatch are amortized across the region. Parameters and typed locals created before the region are passed explicitly. Stream and command-region host requirements propagate through eligible local function graphs, so typed callers do not lose a callee's PowerShell host contract. A tail cannot write back to a CLR parameter or local, and a nested script block that captures unresolved module, environment, or automatic state is not eligible. The complete function stays on the Hybrid script path when either boundary cannot be preserved. The module-scoped dispatcher is cleared when the hybrid module is removed. Runtime-free CLR libraries and Strict typed EXEs never enable these PowerShell-backed regions.

Only unconditional top-level literal dot-sourced files participate in the root module's typed source set. Conditional and function-local dot-sources plus manifest runtime hooks are still discovered and staged, but are counted as runtime fallback rather than being flattened into a different scope. Nested script modules and nested manifests keep their relative layout, manifest closure, and export policy.

`Strict` fails the build when any executable unit needs fallback. For an executable it compiles the entry script and its reachable literal dot-source closure into a .NET entrypoint plus direct static helper methods, with no PowerShell SDK dependency. Multi-file input requires an explicit entrypoint; dynamic calls, external commands, recursion, ambiguous binding, and unreachable requested files fail instead of silently selecting a runtime path. For a DLL, Strict guarantees that the artifact contains only behavior covered by the typed compiler contract.

## Current typed subset

Eligibility is whole-function and intentionally conservative. One unsupported construct keeps the complete function on the PowerShell path.

The current subset supports:

- explicitly typed scalar, `SwitchParameter`, and one-dimensional typed-array parameters;
- preserved function and parameter aliases plus `Parameter(Mandatory)`, `AllowNull`, `ValidateNotNull`, `ValidateNotNullOrEmpty`, `ValidateSet`, `ValidateRange`, and `ValidatePattern` metadata;
- bounded `$PSBoundParameters.ContainsKey('CanonicalParameterName')` queries, including metadata propagation across typed local calls and runtime-free Strict executable argument binding;
- typed or safely inferred local variables;
- explicit `return` values and one terminal implicit-output expression;
- `if`/`elseif`/`else`, conservative scalar `switch`, `for`, `while`, `foreach` over typed arrays or an explicitly typed scalar string, ordered CLR exception catches with bounded `finally` blocks, typed CLR exception throws, and bare rethrow inside a supported catch;
- Boolean logic and scalar comparisons with known compatible types;
- string equality with PowerShell case-sensitive or case-insensitive behavior;
- scalar string `-split` and string-array `-join`;
- expandable strings containing statically typed string variables, with null strings rendered as empty text;
- case-insensitive homogeneous string dictionaries created from ordinary or `[ordered]` string hashtable literals, including lookup and simple index assignment, plus conservative `IDictionary` parameter lookup and mutation;
- empty `CmdletBinding()` metadata and `Parameter(Mandatory)` metadata, with mandatory binding preserved by generated binary cmdlets;
- floating-point and decimal arithmetic with compatible operands;
- explicitly typed integral accumulators and loop counters with checked assignment semantics;
- unlabeled `break` and `continue` inside supported loops;
- untyped array literals and nonempty `@(...)` expressions that preserve PowerShell's observable `object[]` type, explicitly typed one-dimensional array assignments including context-typed empty `@()`, and typed-array or string indexing with PowerShell-compatible negative and missing-index behavior;
- simple indexed assignment to one-dimensional typed arrays, including negative index normalization, and assignment to a statically resolved writable CLR property or field on a typed local or parameter;
- statically resolved CLR constructors, static fields/properties, instance fields/properties, and exact method overloads for supported typed arguments, including defined enum names supplied as string literals;
- genuine binary-module `PSObject` values constructed from bounded `[pscustomobject]@{ Name = Value }` literals with `PSNoteProperty` members;
- direct local function graphs across Strict executables and generated binary modules, including positional, named, alias, unique-abbreviation, switch, mandatory, omitted-default, and supported validation-metadata binding;
- PowerShell stream calls and bounded top-level command/pipeline regions that can capture parameters and typed locals or own a safe terminal dynamic tail when generating a binary module.

Member compilation is intentionally exact. The emitter resolves a single CLR member or overload at build time, applies only supported assignable/numeric/one-character conversions, and falls back when resolution is missing or ambiguous. Both the type and selected constructor, method, property, or field must exist in the requested target framework's reference assemblies; analyzer-host-only APIs and general constructed generic types are rejected before `dotnet` compilation. The compiler-owned homogeneous string dictionary is the current narrow generic exception. Null typed arrays preserve PowerShell's zero-length `.Length` behavior, and a nullable inferred string's property access uses PowerShell's empty-string property semantics while method invocation retains CLR null failure behavior.

The analyzer rejects dynamic behavior rather than guessing. Current blockers include:

- commands and pipelines outside the bounded binary-module region contract, including nested closures over unresolved runtime variables;
- dynamic member names, PowerShell-adapted properties, ambiguous overloads, and general object-property semantics;
- script blocks, closures, runtime scopes such as `$env:`, and untyped parameters;
- unsupported parameter attributes, PowerShell default expressions, and `dynamicparam`, `begin`, `process`, or `clean` blocks;
- dynamic `$PSBoundParameters` access, noncanonical or computed keys, dynamic/string throw operands, and `[pscustomobject]` construction outside generated binary modules;
- nonterminal or nested implicit pipeline output;
- PowerShell truthiness conversions, element-wise array comparison, and coercion between incompatible CLR types;
- string relational operators whose culture-aware ordering has not yet been translated;
- explicit conversion expressions, heterogeneous branch return types, and integral division whose PowerShell result type depends on the quotient;
- untyped integral arithmetic that can change CLR type after overflow;
- array concatenation and compound-assignment operand pairs that have no exact static CLR operator;
- source `#requires` directives and runtime-bearing `using module` / `using assembly` statements, which keep the complete source file on the PowerShell runtime path rather than being silently omitted;
- control flow for which the conservative emitter cannot prove declaration or return behavior.

This boundary is expected to expand through semantic proof, not syntax count. New constructs need differential tests against PowerShell before they become eligible.

## Strict eligibility and realistic coverage

Strict executable compilation is deliberately all-or-nothing. The root script is `Main`, its top-level `param()` block is the application argument contract, and every reachable statement and local function in its contained literal dot-source closure must have an equivalent typed lowering. One unsupported command, dynamic lookup, closure, coercion, or control-flow shape rejects the build rather than quietly placing PowerShell back into a supposedly runtime-free executable.

That means an arbitrary existing automation script is unlikely to qualify for Strict today. The real-product matrix later in this document currently compiles between 1.48% and 26.57% of whole functions in Hybrid modules. A small purpose-built CLI can qualify completely because its entrypoint and helper graph can be designed around the supported subset; a command-heavy administration product usually cannot. Coverage should therefore be read as three separate outcomes:

- **Strict program coverage:** the complete reachable application graph is eligible, so a PowerShell-free EXE can be produced;
- **Hybrid function coverage:** complete eligible functions become generated cmdlets while other functions remain scripts;
- **Hybrid region coverage:** typed code can surround bounded PowerShell command regions so fewer, coarser runtime dispatches are needed.

PowerForge does not aim to reimplement the complete PowerShell language and runtime. Dynamic scope, providers, remoting, arbitrary command discovery, ETS adaptation, host interaction, and every coercion rule would effectively require another PowerShell engine. The useful goal is a well-specified typed subset that grows according to real-product impact, while unsupported behavior remains explicit and correct.

A Hybrid executable is not implemented today. It is the natural future bridge for scripts that cannot become runtime-free: package the PowerShell runtime for unsupported behavior, compile eligible local function graphs or command regions into a companion assembly, and route calls across an explicit boundary. Such an artifact could improve selected workloads and startup organization, but it must continue to report `requiresPowerShellRuntime: true` and must never be presented as Strict compilation.

## Compiler architecture and feature growth

The current implementation is already staged rather than being a text replacement engine:

1. input discovery resolves the root script or module and its contained authored dependency closure without executing it;
2. the PowerShell parser produces ASTs and source extents;
3. the analyzer creates file and unit plans, parameter metadata, target-framework checks, capabilities, and fail-closed diagnostics;
4. the transpiler builds eligible local function graphs and rejects cycles, ambiguous identities, or incompatible dependencies;
5. semantic policy components handle members, operators, assignments, binding, objects, command islands, and generated-type availability;
6. target emitters generate a typed executable, CLR library, binary cmdlets, or Hybrid module composition;
7. the artifact builder compiles, optionally signs, records hashes and source maps, and atomically publishes the complete artifact set.

The emitter is split by semantic responsibility—control flow, local calls, arrays, collections, validation, objects, operators, and advanced binding—and reusable capability flags prevent a Strict target from accidentally using PowerShell-backed behavior. This structure has supported the current feature waves without putting compiler logic into the CLI or cmdlet surfaces.

There is still an important scaling limit: several paths currently analyze the PowerShell AST and then lower it directly to C#. A substantial new feature can require coordinated changes to eligibility analysis, type inference, graph propagation, emission, diagnostics, and target capabilities. That is manageable for the present conservative subset, but it should not become the long-term extension model for broad coverage.

The next architectural milestone is a typed bound intermediate representation between the PowerShell AST and generated C#. Each bound node should carry:

- its resolved CLR type and PowerShell-specific conversion rule;
- its source extent for diagnostics and generated-source mapping;
- observable effects such as pipeline output, stream use, mutation, exception flow, or PowerShell runtime dispatch;
- required target capabilities, for example pure CLR, cmdlet host, bound-parameter state, PowerShell objects, or a command region;
- an explicit fallback reason when the semantic contract cannot be proven.

With that boundary, a feature is bound once and target emitters consume the same proven semantic model. Strict EXEs accept only pure-CLR nodes; binary modules may admit cmdlet-host nodes; Hybrid artifacts may additionally admit bounded runtime regions. The transition can be incremental: new high-value features can use the bound representation first, and existing emitters can be migrated by semantic area instead of stopping current compilation work for a rewrite.

Every newly eligible language feature should satisfy the same acceptance packet:

1. define the supported PowerShell semantics and the deliberate rejection boundary;
2. bind static types, effects, required capabilities, and source locations before emission;
3. compare results and failure behavior with Windows PowerShell 5.1 and the supported PowerShell 7 lanes where the source feature applies;
4. cover Strict rejection, Hybrid fallback, and each target framework that can observe different CLR behavior;
5. inspect the emitted C# and preserve source-map evidence;
6. rerun the real-product census and record which complete functions or regions became eligible;
7. benchmark only workloads large enough to distinguish compiled work from host or cmdlet dispatch overhead.

This makes coverage growth reviewable and maintainable. Success is not the number of AST node types recognized; it is more useful real-product work crossing a proven semantic boundary without changing PowerShell-visible behavior.

## Manifest evidence

Each successful build writes `<name>.powerforge-compilation.json`. The manifest records:

- artifact kind, mode, target framework, and runtime identifier;
- the resolved root and all authored files in the shared compilation scope;
- whether PowerShell is required and whether script fallback is used;
- compiled method count, runtime-fallback count, omitted-unit count, and coverage percentage;
- SHA-256 for the primary artifact, portable PDBs, and every distributed runtime or hybrid-module file;
- exact source diagnostics and locations for unsupported units;
- byte sizes for the primary artifact and every durable file;
- the complete discovered dependency/resource plan and each item's delivery disposition;
- executable optimization mode and Authenticode signing evidence when requested.

A packaged EXE therefore reports `requiresPowerShellRuntime: true` and `usesPowerShellRuntimeFallback: true`. A strict CLR library reports both values as `false`. A strict binary module requires PowerShell as its cmdlet host but reports no script fallback.

PowerForge stages the complete owned artifact shape and manifest before publication. Rebuilding under the same artifact name replaces prior EXE, DLL, PDB, module-directory, generated-source directory, manifest, and exactly the resource files recorded by the previous manifest. Removed resources are deleted, unrelated neighboring files are preserved, unowned collisions fail, same-name publication is serialized across threads and processes, and a failed durable commit rolls back to the previous set instead of leaving a new binary beside stale integrity evidence.

Disposable compiler projects live under a dedicated `PowerForge/powershell-compilation` temporary root, carry an ownership marker and active lock, and are deleted after normal builds. Stale cleanup removes only marked, unlocked, non-retained compiler workspaces older than the cleanup threshold. `KeepBuildWorkspace` adds an explicit retention marker; unrelated or legacy `ps-*` directories are never scavenged by name alone.

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

The repeatable `powershell-compilation-typed-local-calls` lane exercises one entry script and one dot-sourced helper through both `pwsh -File` and a Strict executable. A quick dirty-candidate smoke run (`20260824-093600-31e9f538`) used 2,000 helper calls and recorded 245.04 ms for PowerShell versus 37.89 ms for the typed executable, or 6.47x. It had zero validation failures, but it is directional development evidence rather than the clean release baseline above.

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

The canonical census uses exact committed source from six PowerShell-first products. It scans only authored module trees (`Public`, `Private`, and PSSharedGoods `Enums`), not dirty working-tree changes, generated modules, examples, tests, build scripts, or website assets. The pinned inputs are PowerInfoBlox `9de3730afbfd61ed6bec59bc78e9e7a8d91b6233`, PSSharedGoods `12e9c2520d347df2988286ea1ba3e81e011ef0de`, PSWriteHTML `fa88b1bbecc539b59c9a82cd4b95efc6cc951244`, O365Essentials `fad82882ff116c262ffd3c2c3fdb2781a8ddf0f3`, ADEssentials `b2b1f760853becb773841f744bea196d02aa6c2b`, and PSWriteWord `1fdee837c3fcbc1fdb5c67a9843526bd532c2728`.

That archived six-product lane contains 1,249 files and 1,340 whole script/function units with no parse-error files. Before the common-module language slice, one unit compiled; the parent candidate compiled nine. The PowerInfoBlox helper is also built and invoked as a strict generated binary cmdlet with mandatory parameter metadata, while PSSharedGoods `ConvertFrom-OperationType` is differentially checked for known, case-insensitive, and missing dictionary keys. O365 enum-name overload binding was verified against the original private function.

The CLI now makes this a repeatable regression gate rather than a one-off research script. It records per-product discovery, typed/fallback coverage, parse errors, analyzer duration, dependency summaries, stable missing-feature impact, and frequent co-blocker pairs. `--write-baseline` creates a JSON baseline; `--baseline` returns a failing exit code when a product disappears, typed coverage decreases, fallback increases, or parse errors increase:

```powershell
$root = if ($env:EVOTEC_GITHUB_ROOT) { $env:EVOTEC_GITHUB_ROOT } else { 'C:\Support\GitHub' }

powerforge powershell census `
    (Join-Path $root 'PowerInfoBlox\PowerInfoBlox.psd1') `
    --path (Join-Path $root 'PSSharedGoods\PSSharedGoods.psd1') `
    --path (Join-Path $root 'PSWriteHTML\PSWriteHTML.psd1') `
    --path (Join-Path $root 'O365Essentials\O365Essentials.psd1') `
    --path (Join-Path $root 'ADEssentials\ADEssentials.psd1') `
    --path (Join-Path $root 'PSWriteWord\PSWriteWord.psd1') `
    --framework net10.0 `
    --write-baseline .\artifacts\powershell-compilation-census.json `
    --output json
```

The current six-product frontier candidate reports 1,263 authored files, 1,353 units, 104 emitted typed methods, 1,249 fallback units, and zero parse errors, or 7.69% typed coverage. The per-product typed/fallback split is PowerInfoBlox 2/56, PSSharedGoods 16/269, PSWriteHTML 5/318, O365Essentials 76/210, ADEssentials 4/266, and PSWriteWord 1/130. This is the actual binary-module graph emitted after export shaping, dependency closure, collision handling, and cmdlet-shape checks, not the larger analyzer-only eligibility count. It measures Hybrid compilation opportunity rather than runtime-free Strict coverage.

Low initial coverage is not hidden by Hybrid mode. It is written to the manifest, and every fallback has a diagnostic explaining what needs compiler support. Diagnostics are deliberately blocker-masked to avoid cascades, so accepting one outer construct can reveal deeper runtime semantics without increasing coverage. Roadmap priority therefore comes from repeated full-corpus passes and executable differential proof, not raw syntax-occurrence counts.

The machine-readable `frontier` separates four questions that raw occurrence counts mix together:

- `occurrences`: visible diagnostics assigned to the stable feature ID;
- `affectedUnits`: distinct fallback functions or script bodies reporting it;
- `visibleSoleBlockerUnits`: units where it is the only feature blocker visible in this pass;
- `candidateCompleteProductsUnlocked`: entire census roots with no other currently visible fallback feature.

`candidateCoveragePercentage` adds only visible sole-blocker units to current typed coverage. It is a counterfactual planning signal, not a promise: accepting an outer AST construct can reveal a deeper blocker that was deliberately masked. `coBlockers` shows which features commonly need to land together. Features are ranked lexically by complete-product candidates, visible sole-blocker units, affected units, occurrences, and stable ID; PowerForge does not invent an effort score.

The current five-product run produces this leading planning frontier:

| Feature ID | Occurrences | Affected units | Visible sole-blocker units | Products | Candidate coverage |
| --- | ---: | ---: | ---: | ---: | ---: |
| `command.register-argumentcompleter` | 229 | 82 | 78 | 2 | 13.45% |
| `parameter.type` | 1,518 | 566 | 57 | 6 | 11.90% |
| `runtime.scope` | 1,642 | 418 | 36 | 6 | 10.35% |
| `parameter.metadata` | 1,617 | 357 | 34 | 6 | 10.20% |
| `parameter.default` | 829 | 357 | 31 | 6 | 9.98% |
| `function.graph` | 29 | 29 | 29 | 3 | 9.83% |
| `command.new-htmltab` | 22 | 22 | 22 | 1 | 9.31% |
| `expression.conversion` | 710 | 243 | 10 | 6 | 8.43% |
| `syntax.subexpression` | 746 | 197 | 8 | 6 | 8.28% |

This changes feature planning materially. For example, parameter types have far more total impact than `Register-ArgumentCompleter`, but the latter is the only visible blocker for more units in the current Hybrid module graph. That does not mean it should be implemented as a runtime-free Strict intrinsic: the recommendation attached to each feature still distinguishes a safe intrinsic, a PowerShell-backed command region, an authoring change, or behavior that should remain Package/Hybrid-only.

### Real product rebuild matrix

The module rebuilder was then run from `git archive` snapshots at the exact commits above, not the maintainers' working trees. All five generated modules imported successfully, preserved the complete exported command-name and alias surface, and produced independently rebuildable emitted C# projects. Every emitted project rebuilt with zero compiler warnings and zero errors.

| Product | Source files | Units | Typed / fallback | Coverage | Exported commands before / after | Complete module set | Generated C# |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| PowerInfoBlox `9de3730` | 58 | 58 | 2 / 56 | 3.45% | 51 / 51 | 203,956 bytes | 2 files, 61 lines, 2 mapped methods |
| PSSharedGoods `12e9c25` | 283 | 285 | 16 / 269 | 5.61% | 226 / 226 | 1,338,876 bytes | 2 files, 381 lines, 16 mapped methods |
| PSWriteHTML `fa88b1b` | 242 | 323 | 5 / 318 | 1.55% | 291 / 291 | 1,431,543 bytes | 2 files, 159 lines, 5 mapped methods |
| O365Essentials `fad8288` | 284 | 286 | 76 / 210 | 26.57% | 214 / 214 | 783,806 bytes | 2 files, 2,159 lines, 76 mapped methods |
| ADEssentials `b2b1f76` | 265 | 270 | 4 / 266 | 1.48% | 157 / 157 | 1,890,077 bytes | 2 files, 151 lines, 4 mapped methods |

The function/cmdlet split is intentional: eligible functions become real cmdlets while Hybrid fallback functions keep their script definitions. Differential execution matched for PowerInfoBlox `Convert-IpAddressToPtrString`, PSSharedGoods `ConvertFrom-OperationType`, PSWriteHTML `New-HTMLCarouselStyle`, and O365Essentials `Get-ProcessEnvironmentValue`. The newly eligible private PowerInfoBlox `ConvertTo-InfobloxMicrosoftDHCPServer` also changed from a script function to a generated cmdlet while preserving its `PSCustomObject` type and `_struct`/`ipv4addr` property values.

The resource-policy acceptance build selected `Resources/**` for PSWriteHTML and preserved 239 files totaling 19,582,067 bytes: 59 CSS files, 174 JavaScript files, and six other resources. A net472 PSWriteWord build selected the arbitrary `Lib/**` path and preserved three files totaling 342,224 bytes, including two managed vendor DLLs. Both builds used explicit patterns rather than folder-name behavior, and all 625 temporary proof files (27,790,013 bytes including generated artifacts) were removed after measurement.

A small seven-sample, 10,000-call dispatch probe measured rebuilt/original medians of 204.30/251.44 ms for PowerInfoBlox (1.23x), 143.90/160.04 ms for PSSharedGoods (1.11x), and 215.20/224.98 ms for PSWriteHTML (1.05x). These are intentionally modest: they measure repeated PowerShell command dispatch, not direct CLR execution. The clean direct-CLR PowerInfoBlox benchmark remains 16.2x faster than its PowerShell function. A 2026-08-24 quick smoke run completed all seven shared benchmark suites with zero validation failures. It measured a coarse generated command at 2.71 ms versus 10.81 ms for equivalent fine-grained binary-cmdlet dispatch (**3.99x**), and a multi-file typed local-call EXE at 39.23 ms versus 259.36 ms for `pwsh -File` (**6.61x**). Runs `20260824-134510-7959872a` and `20260824-134535-c549415e` used three samples on a changing worktree, so they are directional evidence rather than clean release baselines.

## Security and distribution limits

Packaging and typed compilation are not obfuscation or source protection. A packaged executable contains an embedded script and runtime assets that a determined user can inspect. A typed EXE or DLL is normal managed/native code and remains analyzable.

`Build-PowerShellArtifact -SignArtifact` and CLI `--sign` sign only build-owned Windows artifacts: the generated executable or library, typed assembly, hybrid module host, and generated primary module manifest. Bundled runtime files and nested/module dependencies keep their original publisher identity. Signing happens before SHA-256 and byte-size evidence is recorded and runs in an isolated Windows PowerShell process with a bounded timeout. A missing certificate, provider timeout, or non-valid signature aborts the atomic publication; no unsigned replacement or stale manifest is committed. Concurrent replacements serialize through a durable per-artifact lock file whose exclusive handle defines ownership across Windows and Unix. The broader PowerForge release pipeline remains the owner for packaging, release attestations, NuGet/GitHub publication, and policy-level signing configuration.

The fail-closed signing and atomic-publication contract is covered by automated tests. On 2026-08-23 the internal acceptance run also produced a valid Authenticode-signed typed EXE, a net8 binary module, and a net472 binary module with the maintainer's code-signing certificate and DigiCert timestamp service. Each staged hash matched the final manifest and each artifact executed successfully in its target host. These were local internal proof artifacts only; nothing was published to PSGallery, NuGet, GitHub Releases, or another feed.

The generated EXE carries the PowerShell SDK, so it is much larger than the input script and may start more slowly than an installed `pwsh`. Self-contained publication adds the .NET runtime as well. With `SingleFile = $false`, PowerForge preserves the complete nested publish tree instead of copying only top-level files. Runtime-packaged artifacts must be rebuilt when their embedded PowerShell or .NET dependencies need security updates.

Strict typed executable compilation accepts one `.ps1` entrypoint and its contained literal dot-source dependency closure. Top-level `param()` remains the process argument contract, while source functions become direct static CLR methods. Local function calls enforce the supported validation metadata and deliberately reject recursion, splatting, redirection, external commands, dynamic command names, and incompatible argument conversion. `Hybrid` is not a valid executable mode: choose runtime-preserving `Package` or PowerShell-free `Strict`. Source `#requires` directives and runtime-bearing `using` statements are never erased: Strict typed builds reject them, Hybrid modules retain affected functions on the runtime path, and Hybrid libraries omit them with diagnostics. Hybrid module composition preserves namespace `using` and module `param` prologues for mixed `.ps1` or `.psm1` source. Generated typed export shaping requires literal unconditional exports, including colon-attached literal forms such as `-Function:Get-Value`, and contained relative file references; conditional-only export logic remains in the script fallback and executes unchanged. Strict modules reject `ScriptsToProcess` and script-based `NestedModules`; Hybrid records those hooks as runtime fallback. Required contained assemblies, format files, type files, and scripts must exist; named external assemblies remain manifest references rather than local files. Every staged manifest or dot-source path must remain inside the source root without symbolic-link or junction traversal. Binary-module generation routes non-Verb-Noun or otherwise unrepresentable wrappers to Hybrid script fallback and excludes their methods from the generated CLR assembly; Strict mode rejects them. Generated cmdlet output uses PowerShell's normal collection-enumeration contract rather than treating only arrays as pipelines; `OutputType` advertises an array's element type and uses `object` when an enumerable's element type cannot be proven. Expandable strings currently accept string variables only; subexpressions, non-string runtime conversion, and mixed escaped-dollar interpolation remain fallback. Enum arguments accept only defined names from literal strings. Null-to-reference overload binding remains fallback because PowerShell may convert null to an empty string where direct CLR would preserve null. A plain CLR library contains only eligible methods and no automatic PowerShell fallback host.

The typed boundary also preserves several less-visible PowerShell contracts. Indexing a null `IDictionary` yields null. Generated cmdlets for simple functions consume surplus positional arguments, while advanced functions retain advanced binding behavior. An array-returning local function stays on the Hybrid script path when a direct consumer would observe PowerShell pipeline scalarization. Observable `SwitchParameter` members or CLR identity likewise stay on the script path even though safe boolean control flow compiles. Hybrid composition keeps cross-file declaration timing conservative and removes its private dispatcher state before authored wildcard variable exports are evaluated.

Strict typed executables may request `Trimmed` or `NativeAot` optimization. Both require a RID-specific, self-contained, single-artifact build; NativeAOT already emits the native executable directly and does not enable MSBuild's separate single-file bundler. Packaged PowerShell executables are rejected because trimming a dynamic PowerShell runtime is not a safe default. Native AOT is therefore a deployment option only for the proven typed subset, not a promise that arbitrary PowerShell can be converted to native code.

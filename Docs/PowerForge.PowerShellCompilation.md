# PowerShell Compilation

Last updated: 2026-08-30

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

Target support is narrower than build availability. PowerForge currently marks only Strict `net10.0` framework-dependent and NativeAOT executables for `win-x64` and `linux-x64` as `Supported`, after executing both profiles on their target hosts. Portable managed artifacts retain the `PortableManaged` support level. Named-RID `net8.0`, self-contained, trimmed, Package/Hybrid, macOS, and Arm64 profiles remain `Experimental` until that exact framework, deployment model, RID, and host behavior pass the same closure and execution gate. ReadyToRun is benchmark-only and cannot be selected as a public target.

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

### Reproducible project workflow

For a repeatable artifact matrix, create one portable project manifest instead of repeating command switches in a repository-specific build script:

```powershell
powerforge powershell project init .\src\Sample.psd1 `
    --project .\powerforge.psproject.json `
    --name Sample

powerforge powershell project analyze   .\powerforge.psproject.json
powerforge powershell project explain   .\powerforge.psproject.json
powerforge powershell project recommend .\powerforge.psproject.json
powerforge powershell project lock      .\powerforge.psproject.json
powerforge powershell project restore   .\powerforge.psproject.json
powerforge powershell project restore   .\powerforge.psproject.json --offline
powerforge powershell project build     .\powerforge.psproject.json
powerforge powershell project test      .\powerforge.psproject.json
powerforge powershell project diagnose  .\powerforge.psproject.json
powerforge powershell project pack      .\powerforge.psproject.json
powerforge powershell project install   .\powerforge.psproject.json
```

The manifest maps source/resource policy, one named semantic profile, provider packages and trust, an exact artifact matrix, dependency/provider lock paths, an optional ABI baseline, and diagnostic/IR policy onto the existing compiler contracts. Each target must have a unique kind/mode/TFM/RID/architecture/deployment identity. No source or module identity changes compiler behavior. `SemanticProfileId` is an effective compiler input: it participates in target hashing, binding/lowering, compatible `#requires` policy, provider resolution, caches, package variants, artifacts, and diagnostics. When compatibility fields omit it, `net472` selects the Windows PowerShell 5.1 profile and modern targets select the PowerShell 7.6 profile; explicit profiles and target contracts remain authoritative. Unknown profiles fail closed, and behaviorally different profiles do not share compilation identity.

`restore` acquires exact NuGet identities into `.powerforge/environment/packages`, records the reviewed dependency locks plus a target-specific complete `packages.lock.json` closure, and verifies NuGet's canonical signed/unsigned content identity, the downloaded archive bytes, the resolved assets graph, and the extracted files consumed by MSBuild. `restore --offline` clears package sources and proves the already acquired environment can satisfy those same locks. `build` injects any reviewed direct package reference absent from the generated template, runs the generated compilation project in locked mode, reconciles its complete actual assets graph with that target lock, and records the closure-lock SHA-256 in the durable compiler manifest. It rejects project drift, environment-evidence drift, missing or changed closure locks, modified archives or extracted package payloads, extra or missing actual packages, provider-lock drift, or any dependency change. Tool-owned `.powerforge` state and declared artifact roots are supplied as generated-output roots, so restore packages and previous outputs cannot become authored resource input.

`test` first revalidates the current project, target, locks, complete build inventory, and every artifact hash before executing the declared surface. `pack` requires matching passed test evidence and produces a deterministic qualified ZIP whose authenticated descriptor includes the exact target, semantic profile, dependency/provider locks, ABI, SBOM, provenance, test identity, complete file inventory, and artifact hash. `install` validates that descriptor, extracts into an immutable project-local root, compares every installed byte with the archive, verifies the primary artifact identity, and repeats the declared EXE, clean module import, or CLR metadata test. Existing matching installations are reusable and tampered content fails closed. The installation directory identity is a complete 256-bit Base64URL SHA-256 derived from both the full target-contract hash and full authenticated artifact-set hash, so different manifests, resources, or sidecars cannot collide merely because their primary binary matches.

`recommend` is advisory only. Without a supplied boundary profile it reports static eligibility and suggests the next measurement. With `--boundary-profile <profile.json>`, it can recommend coarsening an expensive typed/hosted boundary, retaining hosted execution, or evaluating a Strict candidate. It never edits source, changes the project target, or describes eligible units as PowerShell language coverage.

Use `powerforge powershell support --output json` for the canonical qualified support matrix. The current toolchain channel is `Preview`: portable managed outputs and the target-host-qualified Windows/Linux x64 Strict profiles are advertised, while macOS, Arm64, self-contained, and trimmed exact profiles remain experimental until their own semantic, lock, install, target-host, and performance packet passes.

Project, target-contract, dependency-lock, provider-lock, compiler-manifest, explanation, diagnostic, cache, and ABI evidence carry explicit schema or semantic-profile identities. Unknown schema versions fail instead of being guessed. During preview, an intentional incompatible change requires a new schema or semantic profile plus migration guidance. The planned stable policy accepts an older schema through at least two minor release trains before removal unless retaining it would violate a security or correctness invariant. Additive diagnostic fields do not change a semantic or ABI identity; changed behavior does. Semantic profiles already participate in compilation identity; full profile promotion additionally requires the exact-host oracle evidence described in the roadmap. Public package publication, upgrade, and rollback proof remain a separate explicitly authorized release lane and are never inferred from a source checkout.

For the common case, point PowerForge at the module directory. It selects the matching top-level manifest and root module, infers a hybrid binary-module build, and writes to the module's `artifacts` directory:

```powershell
powerforge powershell build .\MyModule --allow-unreviewed-dependencies --emit-source
```

Artifact builds require a separately reviewed dependency graph by default. Capture the `dependencyGraph` from `powerforge powershell analyze <path> --output json`, review and store that graph, then pass the raw graph JSON with `--dependency-lock <graph.json>` or the equivalent `-DependencyLock` cmdlet object. For a local development build only, `--allow-unreviewed-dependencies` / `-AllowUnreviewedDependencies` is the explicit opt-out; manifest schema 12 records `dependencyLockReviewed: false` so that result cannot be mistaken for a reviewed build.

The accepted input shapes are:

- `.ps1`: defaults to a packaged executable;
- several loose `.ps1` files: default to a Hybrid typed library that emits eligible functions and reports omissions; choose Strict explicitly when every function must compile;
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
    --allow-unreviewed-dependencies `
    --emit-source

Build-PowerShellArtifact `
    -Path .\Public\Get-One.ps1, .\Public\Get-Two.ps1 `
    -Kind BinaryModule `
    -AllowUnreviewedDependencies `
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
    --allow-unreviewed-dependencies `
    --out .\artifacts

Build-PowerShellArtifact `
    -Path .\Tool.ps1, .\Private\Helpers.ps1 `
    -EntryPoint .\Tool.ps1 `
    -Kind Executable `
    -Mode Package `
    -AllowUnreviewedDependencies
```

Package and Hybrid executables extract the retained dependency tree with its relative layout before the embedded script runs, so `$PSScriptRoot`, `$PSCommandPath`, and nested literal dot-sources retain file-backed behavior. Strict executables instead compile the explicit entrypoint and its reachable contained dot-source closure into one runtime-free program. Every reachable unit must lower successfully; dynamic, escaping, missing, linked, or unrelated dependencies fail closed.

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

A generated artifact with selected payload is an artifact set, not necessarily one physical file. Binary modules, Strict EXEs, and CLR libraries copy selected CSS, JavaScript, images, managed assemblies, native libraries, templates, data, manifests, and type/format data beside the primary artifact with their relative paths intact. Package EXEs instead embed selected resources and extract them with the reachable dot-source closure into a private contained runtime layout. Exact inferred resource references are rewritten to that layout. An explicit include or `CompleteModule` selection gives the packaged script body an extracted `$PSScriptRoot` and `$PSCommandPath`, so declared dynamic paths such as `Join-Path $PSScriptRoot $name` resolve without leaving sidecars beside the durable EXE. Extraction uses a per-user build-identity cache that remains available after the parent process exits, so asynchronous children can continue consuming embedded scripts and resources. Parameter defaults retain EXE-backed path metadata even when the script body uses the extracted entry path. PowerForge signs only build-owned generated files; adjacent vendor assemblies retain their publisher identity. `SingleFile` includes Package-mode selected resources, while adjacent Strict/DLL payload remains a multi-file artifact set.

Resource selection is policy-driven:

- `Declared` is the default. It includes manifest-required files, explicit includes, and high-confidence contained file literals such as `Get-Content "$PSScriptRoot/Templates/report.html"`.
- `CompleteModule` includes all contained module-root payload except explicit exclusions. Use it for a staged module directory, not an unchecked repository root.
- `None` disables inference and broad optional selection; manifest-required files and explicit includes still apply.
- `FileList` is authoritative. A missing entry or an exclusion that matches a manifest-required file fails closed.
- `IncludeResource` and `ExcludeResource` accept contained paths, directories, and `*`, `?`, or `**` globs. An unmatched pattern, include/exclude collision, link, root escape, case collision, selected output overlap, or inaccessible explicitly selected directory fails closed with a diagnostic. `CompleteModule` likewise requires every contained directory to be enumerable; only undeclared optional inventory is best effort.
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
    -ExcludeResource 'Vendor/**/*.pdb' `
    -AllowUnreviewedDependencies
```

PowerForge does not turn a module into an EXE or infer an application entrypoint from exported functions. A standalone `.ps1` that imports another module does not cause that module and its complete resource tree to be bundled; module dependency acquisition remains an explicit deployment concern.

Analyze a file or a complete source tree before building:

```powershell
powerforge powershell analyze .\MyModule --mode Hybrid
```

Use `explain` when you need a stable reason rather than the full build plan. Human output lists file-level blockers, missing-dependency causes, and typed, runtime-fallback, or rejected unit decisions with their causal diagnostics. JSON output uses relocation-safe unit identities and redacts machine-specific absolute paths:

```powershell
powerforge powershell explain .\MyModule --mode Strict
powerforge powershell explain .\MyModule --mode Hybrid --output json
```

The explanation schema includes a compatibility version and semantic fingerprint. The fingerprint excludes relocation coordinates and traversal order while preserving semantic order such as parameter position, so equivalent inputs can be compared across supported hosts without pretending that every byte of presentation JSON must match. Final artifact explanations add inferred types and value states, provider/dependency resolution, lowering choices, final fallback/rejection causes, and artifact disposition.

Successful builds carry a portable statement-level failure map and a deterministic diagnostic audit trail. The audit records build-cache reasons, dependency-lock state, public-ABI state, fallback crossings, and selected provider contracts. To map a captured runtime failure back to authored source, provide the artifact manifest and local failure log:

```powershell
powerforge powershell diagnose .\artifacts\Tool.powerforge-compilation.json `
    --failure .\runtime-failure.log
```

The result is available as human output or `--output json`. It reports the compiler/runtime stage, stable reason, authored relative path, unit identity, source line/column, diagnostic code, and typed/hosted boundary. Absolute paths, authored source text, parser objects, environment state, and common secret assignments are removed from portable diagnostics.

Package a script as an executable:

```powershell
powerforge powershell build .\Invoke-Report.ps1 `
    --out .\artifacts `
    --allow-unreviewed-dependencies `
    --name Invoke-Report

.\artifacts\Invoke-Report.exe --Path C:\Reports --Format Html
```

Build a managed Hybrid executable when complete local functions are eligible but the entry script or dependencies still need hosted PowerShell semantics:

```powershell
powerforge powershell build .\Invoke-MixedReport.ps1 `
    --kind exe `
    --mode Hybrid `
    --allow-unreviewed-dependencies `
    --out .\artifacts `
    --name Invoke-MixedReport
```

Eligible functions become registered generated cmdlets in the packaged host. The entry script and unsupported units remain embedded source, and the manifest reports the typed, hosted, fallback, and static crossing-site counts. This is a managed/current-host delivery foundation, not a claim that Hybrid is NativeAOT-ready or that any cross-published RID is supported.

Compile an eligible top-level script into a PowerShell-free executable:

```powershell
powerforge powershell build .\Measure-Threshold.ps1 `
    --kind exe `
    --mode Strict `
    --allow-unreviewed-dependencies `
    --out .\artifacts `
    --name Measure-Threshold
```

Strict typed executables accept process-bindable scalar, enum, nullable, URI/version, date/time, GUID, switch, and one-dimensional array parameters. Their generated CLI preserves required parameters, aliases, explicit or source-order positions, `AllowNull`, `AllowEmptyString`, `AllowEmptyCollection`, and the supported `ValidateNotNull`, `ValidateNotNullOrEmpty`, `ValidateSet`, `ValidateRange`, and `ValidatePattern` metadata. It supports exact names, aliases, unambiguous abbreviations, positional values, repeated array options, `--Name value`, `--Name=value`, switches, and `--`. Missing, duplicate, ambiguous, unknown, or validation-failing parameters are rejected before invoking compiled code. PowerShell parameter sets, pipeline binding, host-only types, and discovery-only metadata are rejected on this runtime-independent surface rather than silently ignored.

Generated binary cmdlets apply PowerShell script-function invariant-culture conversion to numeric, date/time, and duration parameters before CLR property binding. This keeps accepted and rejected input independent of the caller's current culture, matching the authored advanced function rather than compiled-cmdlet culture defaults.

The generated host accepts positional arguments, `--Name value`, `--Name=value`, switches and aliases such as `--Force`, common switches on advanced scripts, and `--` to stop named-argument parsing. A non-switch named parameter must have a value; use `--Name=-value` when that value begins with `-`. Duplicate aliases and aliases that collide with an authored or automatic parameter name are rejected before host generation, matching PowerShell's metadata boundary. Pipeline objects use PowerShell's normal formatting system before going to stdout; information and warning records also go to stdout, while errors go to stderr. Nonterminating error records do not by themselves change a successful process exit code; a top-level explicit `exit <code>` becomes the process exit code, and a terminating exception fails the process. `$PSCommandPath` normally resolves to the running artifact path and `$PSScriptRoot` to its durable directory. A Package build with explicit or complete-module resources instead resolves the script body's `$PSCommandPath` and `$PSScriptRoot` to the private extracted entry and root; parameter-binding path metadata remains artifact-backed. Packaging rejects `exit` inside a function, nested script block, trap, or caught region because exception instrumentation would change PowerShell behavior. It also rejects `using module` and `using assembly` because those directives are resolved before an embedded script can receive file-backed path metadata.

Compile a strict binary module:

```powershell
powerforge powershell build .\MathTools.psm1 `
    --kind dll `
    --mode Strict `
    --framework net8.0 `
    --allow-unreviewed-dependencies `
    --out .\artifacts

Import-Module .\artifacts\MathTools.dll
```

When `MathTools.psd1` exists beside `MathTools.psm1`, the primary artifact is a rewritten manifest in a module directory. `RootModule`, `FunctionsToExport`, and `CmdletsToExport` are remapped so a function that became a binary cmdlet keeps the same public name. Literal top-level `Export-ModuleMember` declarations are preserved across typed and fallback commands; dynamic export expressions are rejected. An omitted `AliasesToExport` entry stays omitted so aliases created by retained module source continue to follow PowerShell's default manifest policy.

Build a hybrid module when only part of the source is eligible. The kind and mode are inferred from the module input:

```powershell
powerforge powershell build .\Operations `
    --allow-unreviewed-dependencies `
    --out .\artifacts

Import-Module .\artifacts\Operations\Operations.psm1
```

Build a runtime-independent CLR library containing every eligible function:

```powershell
powerforge powershell build .\Calculations.psm1 `
    --kind library `
    --mode Hybrid `
    --allow-unreviewed-dependencies `
    --out .\artifacts
```

Add `--output json` to either `analyze` or `build` for a stable machine-readable envelope. Analyzer diagnostics include a stable `featureId`, while `dependencies` explains what the selected artifact shape will compile, preserve, copy, embed, leave external, or reject.

Analyze, explain, and build consume the same normalized target contract. Compatibility options such as `--framework`, `--rid`, `--self-contained`, `--optimization`, and `--no-single-file` construct it; `--target-contract .\target.json` instead supplies an explicit integrity-checked contract to all three commands. A stored schema-v1 or schema-v2 contract is first verified against its declared support level and original hash rules, then migrated to schema v2, reclassified against the current support policy, and rehashed; a support-policy promotion or demotion therefore does not make an otherwise authentic stored request unreadable. NativeAOT planning resolves the runtime-pack version owned by the selected SDK and rejects a missing exact pack instead of silently choosing a newer ambient same-major pack. Successful artifacts emit target-contract, toolchain, dependency-lock, SBOM/provenance, and file-hash evidence. The CLI and cmdlet use a verified content-addressed build cache by default; use `--cache-directory <path>` / `-BuildCacheDirectory` to select its owner or `--no-build-cache` / `-UseBuildCache:$false` for a deliberately uncached build. Cache keys include the normalized restore graph, actual resolved package bytes, selected SDK/reference-pack bytes, target, reviewed graph lock, compiler identity, build host, and generated inputs. Restore verifies a copied payload before atomic promotion; unsafe reparse-point roots/ancestors and malformed or changed entries are misses, never trusted hits.

## Use the PSPublishModule cmdlet

The cmdlet is a thin PowerShell surface over the same artifact builder:

```powershell
Build-PowerShellArtifact `
    -Path .\Operations `
    -AllowUnreviewedDependencies `
    -EmitSource
```

It supports `-WhatIf`, returns `PowerShellCompilationBuildResult`, and uses the same discovery, defaults, overrides, and manifests as the CLI. `-Kind`, `-Mode`, `-Name`, and `-OutputDirectory` remain available when the inferred values are not the desired artifact.

### Convert a module during `Build-Module`

`Build-Module` can compile any staged script module into its delivered binary-module shape. The source tree remains script-first; compilation happens after merge, manifest, formatting, and resource preparation, then the normal signing, documentation, validation, test, package, publish, and install phases consume the generated module.

```powershell
Build-Module -ModuleName 'Any.Module' -Path $repositoryRoot -Settings {
    New-ConfigurationBuild `
        -Enable `
        -CompilePowerShell `
        -PowerShellCompilationMode Hybrid `
        -PowerShellCompilationAllowUnreviewedDependencies
}
```

`Hybrid` is the migration default: eligible functions become binary cmdlets and unsupported behavior remains explicit script fallback. `Strict` fails unless every executable unit can be emitted without fallback. `ModulePipelineResult.PowerShellCompilationResult` reports analyzed and emitted units, semantic and shaping fallback, runtime-routed units, omissions, exact coverage percentage, and the staged assembly path. These counts come from one final unit-disposition ledger, so a typed CLR unit may also be runtime-routed when it contains a bounded hosted command region. A generated cmdlet that hosts an advanced-function lifecycle is recorded as a binary surface with retained hosted semantics, not as typed CLR emission.

Release builds should pass a separately reviewed dependency graph through `-PowerShellCompilationDependencyLock`. `-PowerShellCompilationAllowUnreviewedDependencies` is the explicit local/development opt-out shown above. Resource inclusion remains declarative through `-PowerShellCompilationResourceMode`, `-PowerShellCompilationIncludeResource`, and `-PowerShellCompilationExcludeResource`; no module name or folder convention changes compiler behavior.

Use `-PowerShellCompilationEmitIrSnapshots` when a reviewed build needs a diffable semantic-only bound/lowered IR file. Use `-PowerShellCompilationExpectedPublicAbiSha256 <sha256>` with Strict compilation to fail closed when the generated public ABI differs from a reviewed baseline. The same controls are `-EmitIrSnapshots` / `-ExpectedPublicAbiSha256` on `Build-PowerShellArtifact` and `--emit-ir` / `--expected-abi-sha256` on the CLI.

`Build-Module` produces a module, so this opt-in deliberately produces a DLL-backed binary module. A standalone application has a different entrypoint and deployment contract; use `Build-PowerShellArtifact -Kind Executable` (or the equivalent CLI command) with an explicit `.ps1` entry point when several scripts participate.

## Modes

`Analyze` parses source and reports one decision per top-level script body or function. It produces no artifact.

`Package` preserves dynamic PowerShell behavior. The current executable lane embeds the source script and PowerShell SDK in a generated .NET host. It is a distribution feature, not typed compilation.

`Hybrid` compiles complete eligible functions and retains diagnostics for everything else. A hybrid binary module removes compiled function definitions from its generated `.psm1`, imports the typed DLL, and keeps unsupported functions on the script path. Literal `$PSScriptRoot` dot-source dependencies are staged recursively with their relative layout, including dependencies reached from manifest runtime hooks. Dynamic, missing, wildcard, working-directory-relative, source-root-escaping, or symbolic-link/junction paths fail before publication. A hybrid CLR library extracts eligible methods without carrying script fallback because it is intended for direct .NET consumption.

Binary modules can also compile typed control flow around deliberately bounded PowerShell command regions. Direct success, verbose, debug, warning, information, host, and nonterminating-error calls use generated `PSCmdlet` stream APIs with their real command-specific value parameter names. `Write-Host` remains a distinct host sink and emits a `HostInformationMessage` in an information record tagged `PSHOST`; it is not flattened into ordinary `Write-Information`. Their parameter contract preserves parameter sets, mandatory/position flags, pipeline and property-name binding, remaining arguments, literal help, hidden parameters, empty-value markers, wildcard discovery, and a conservative set of PowerShell-host types. An implicit end body with pipeline-bound parameters is emitted through `EndProcessing`, so it runs once with the final bound value just as the authored advanced function does. PowerShell 7 Hybrid modules can additionally host canonical `begin`, per-record `process`, `end`, and PowerShell 7.3+ `clean` lifecycle blocks through one steppable pipeline; the original record is preserved and `StopProcessing()` stays prompt. Authored cleanup is idempotent across normal, failure, stop, and disposal paths while the owning runspace remains usable. A terminal closed or broken runspace instead receives idempotent pipeline disposal without claiming that the authored `clean` block executed. Adjacent top-level command pipelines, parameter-only conditional command blocks, explicit discard assignments such as `$null = Invoke-Operation`, and a safe terminal command-result tail are grouped into one PowerShell invocation so binding and dispatch are amortized across the region. Parameters and typed locals created before the region are passed explicitly. An explicit typed assignment such as `[string[]] $items = Invoke-Operation` can capture success output from one bounded region and resume typed execution: zero outputs become null, one output becomes a scalar, multiple outputs become an array, and PowerShell's public conversion primitive applies the declared target type. Untyped targets, redirections, unresolved state, and nested local-function dispatch remain fallback rather than guessing at write-back semantics. Stream, command-region, and capture-host requirements propagate through eligible local function graphs, so typed callers do not lose a callee's PowerShell host contract. A terminal tail cannot write back to a CLR parameter or local, and a nested script block that captures unresolved module or dynamic scope is not eligible. The complete function stays on the Hybrid script path when either boundary cannot be preserved. The module-scoped dispatcher is cleared when the hybrid module is removed. Runtime-free CLR libraries and Strict typed EXEs never enable these PowerShell-backed regions.

Only unconditional top-level literal dot-sourced files participate in the root module's typed source set. Conditional and function-local dot-sources plus manifest runtime hooks are still discovered and staged, but are counted as runtime fallback rather than being flattened into a different scope. Nested script modules and nested manifests keep their relative layout, manifest closure, and export policy.

`Strict` fails the build when any executable unit needs fallback. For an executable it compiles the entry script and its reachable literal dot-source closure into a .NET entrypoint plus direct static helper methods, with no PowerShell SDK dependency. Multi-file input requires an explicit entrypoint; dynamic calls, external commands, uncontracted recursion, ambiguous binding, and unreachable requested files fail instead of silently selecting a runtime path. For a DLL, Strict guarantees that the artifact contains only behavior covered by the typed compiler contract.

## Current typed subset

Eligibility is whole-function and intentionally conservative. One unsupported construct keeps the complete function on the PowerShell path.

The current subset supports:

- capability-classified CLR, PowerShell-host, process-bindable, `SwitchParameter`, nullable, enum, and one-dimensional typed-array parameters;
- preserved function and parameter aliases plus parameter-set, position, pipeline, remaining-argument, literal-help, empty-value, wildcard, and validation metadata on hosts that implement those contracts;
- bounded `$PSBoundParameters.ContainsKey('CanonicalParameterName')` queries, including metadata propagation across typed local calls and runtime-free Strict executable argument binding;
- target-backed `$PSEdition`, `$PSVersionTable.PSVersion.Major`, and PowerShell Core `$IsCoreCLR`, `$IsWindows`, `$IsLinux`, and `$IsMacOS` facts in runtime-free artifacts; the selected semantic profile fixes the version-major value, while the complete live `$PSVersionTable.PSVersion` object remains host-bound; read-only `$env:NAME` access lowers to CLR environment lookup; generated binary cmdlets can additionally inject the live `$PSVersionTable.PSVersion`, `$WhatIfPreference`, supported action/confirm preferences, one read-only `$Error` snapshot, and one- or two-argument `$PSCmdlet.ShouldProcess(...)` contracts, including explicitly bound common-parameter overrides and the host-specific Windows PowerShell 5.1 versus PowerShell 7 `-Debug` preference behavior;
- typed or safely inferred local variables;
- explicit `return` values and one terminal implicit-output expression;
- `if`/`elseif`/`else`, conservative scalar `switch`, `for`, `while`, `foreach` over typed arrays or an explicitly typed scalar string, ordered CLR exception catches with bounded `finally` blocks, typed CLR exception throws, and bare rethrow inside a supported catch;
- Boolean logic and scalar comparisons with known compatible types;
- string equality with PowerShell case-sensitive or case-insensitive behavior;
- scalar type tests, regex match/replace, wildcard match, and membership operators, with PowerShell-host primitives enabled only for generated binary modules where their runtime contract is available;
- scalar string `-split` and string-array `-join`;
- integral bitwise and shift operators with PowerShell-compatible small-integer promotion, plus target-compatible explicit conversions that use a compile-time literal when possible and PowerShell's public conversion primitive only in a PowerShell-backed host;
- expandable strings containing statically typed string variables, with null strings rendered as empty text;
- case-insensitive homogeneous string dictionaries created from ordinary or `[ordered]` string hashtable literals, including lookup and simple index assignment; generated binary modules additionally preserve bounded heterogeneous `Hashtable`/`OrderedDictionary` values plus adapted `IDictionary` member lookup and assignment;
- conservative `CmdletBinding` positional/default-set/ShouldProcess metadata and parameter binding metadata, with host-only behavior enabled only for generated binary cmdlets;
- floating-point and decimal arithmetic with compatible operands;
- explicitly typed integral accumulators and loop counters with checked assignment semantics;
- unlabeled `break` and `continue` inside supported loops;
- untyped array literals and nonempty `@(...)` expressions that preserve PowerShell's observable `object[]` type, explicitly typed one-dimensional array assignments including context-typed empty `@()`, typed-array concatenation with scalar/array and null operands, and typed-array, `IList`/`ArrayList`, or string indexing with PowerShell-compatible negative and missing-index behavior;
- simple indexed assignment to one-dimensional typed arrays, lists, and dictionaries, including negative index normalization, plus assignment to a statically resolved writable CLR property or field on a typed local or parameter;
- statically resolved CLR constructors, static fields/properties, instance fields/properties, and exact method overloads for supported typed arguments, including defined enum names supplied as string literals;
- genuine binary-module `PSObject` values constructed from bounded `[pscustomobject]@{ Name = Value }` literals with one statically known note-property shape, direct known-property reads/writes, exact `PSObject.Properties['Name'].Value` access, and exact `Add-Member -NotePropertyName/-NotePropertyValue` mutation; arbitrary ETS and identity-observing methods remain fallback;
- direct local function graphs across Strict executables and generated binary modules, including positional, named, alias, unique-abbreviation, switch, mandatory, omitted-default, and supported validation-metadata binding; a direct self-recursive function additionally requires one target-compatible `[OutputType]` contract that matches its inferred body type, while mutual or otherwise uncontracted cycles stay on fallback. Calls into a local function that invokes `ShouldProcess` also stay on the PowerShell command path so the inner command identity and `ConfirmImpact` are not replaced by the outer generated cmdlet;
- PowerShell stream calls and bounded top-level command/pipeline regions that can capture parameters and typed locals, return success output into an explicit typed assignment, or own a safe terminal dynamic tail when generating a binary module.

Member compilation is intentionally exact. The semantic binder resolves a single CLR member or overload, applies only supported assignable/numeric/one-character conversions, and falls back when resolution is missing or ambiguous. Both the type and selected constructor, method, property, or field must exist in the requested target framework's reference assemblies; analyzer-host-only APIs and general constructed generic types are rejected before lowering. Dictionary literals use BCL representations in runtime-free artifacts: homogeneous string indexing retains a scalar string contract and heterogeneous values remain object-valued. A statically typed `IDictionary` dot-member read stays object-valued and performs dynamic key-first lookup through that dictionary's comparer, followed by a statically resolved CLR-member fallback. This is not general Extended Type System support or a promise that a mutable dictionary value keeps its literal type. Generated binary modules additionally model adapted dictionary writes through their hosted PowerShell capability. Null typed arrays preserve PowerShell's zero-length `.Length` behavior, and a nullable inferred string's property access uses PowerShell's empty-string property semantics while method invocation retains CLR null failure behavior.

The analyzer rejects dynamic behavior rather than guessing. Current blockers include:

- commands and pipelines outside the bounded binary-module region contract, including nested closures over unresolved runtime variables;
- dynamic member names, unbounded PowerShell-adapted properties, ambiguous overloads, and general object-property semantics;
- script blocks, unproven closures, mutable script/global/private/variable-provider scope, environment-provider mutation, and untyped parameters;
- automatic or preference variables outside the explicit read-only intrinsic set, arbitrary `$PSVersionTable` keys, and `$PSCmdlet` interactions other than the bounded `ShouldProcess` overloads;
- dynamic or host-incompatible parameter attributes, PowerShell default expressions, `dynamicparam`, and lifecycle blocks outside the explicitly supported PowerShell 7 Hybrid/version matrix;
- dynamic `$PSBoundParameters` access, noncanonical or computed keys, dynamic/string throw operands, and `[pscustomobject]` construction outside generated binary modules;
- nonterminal or nested implicit pipeline output;
- PowerShell truthiness outside a hosted target, element-wise array comparison, and coercion between incompatible CLR types without an explicit target capability;
- string relational operators whose culture-aware ordering has not yet been translated;
- conversion expressions whose target is unavailable or whose dynamic semantics require a host capability not present in the artifact, heterogeneous branch return types, and integral division whose PowerShell result type depends on the quotient;
- untyped integral arithmetic that can change CLR type after overflow;
- array concatenation and compound-assignment operand pairs that have no exact static CLR operator;
- source `#requires` directives and runtime-bearing `using module` / `using assembly` statements, which keep the complete source file on the PowerShell runtime path rather than being silently omitted;
- control flow for which binding and analysis cannot prove declaration, output, or return behavior.

This boundary is expected to expand through semantic proof, not syntax count. New constructs need differential tests against PowerShell before they become eligible.

## Strict eligibility and realistic coverage

Strict executable compilation is deliberately all-or-nothing. The root script is `Main`, its top-level `param()` block is the application argument contract, and every reachable statement and local function in its contained literal dot-source closure must have an equivalent typed lowering. One unsupported command, dynamic lookup, closure, coercion, or control-flow shape rejects the build rather than quietly placing PowerShell back into a supposedly runtime-free executable.

That means an arbitrary existing automation script is unlikely to qualify for Strict today. The refreshed exact-pinned real-product matrix later in this document currently emits between 2.31% and 21.63% of whole functions in Hybrid modules. A small purpose-built CLI can qualify completely because its entrypoint and helper graph can be designed around the supported subset; a command-heavy administration product usually cannot. Coverage should therefore be read as three separate outcomes:

- **Strict program coverage:** the complete reachable application graph is eligible, so a PowerShell-free EXE can be produced;
- **Hybrid function coverage:** complete eligible functions become generated cmdlets while other functions remain scripts;
- **Hybrid region coverage:** typed code can surround bounded PowerShell command regions so fewer, coarser runtime dispatches are needed.

PowerForge does not aim to reimplement the complete PowerShell language and runtime. Dynamic scope, providers, remoting, arbitrary command discovery, ETS adaptation, host interaction, and every coercion rule would effectively require another PowerShell engine. The useful goal is a well-specified typed subset that grows according to real-product impact, while unsupported behavior remains explicit and correct.

A managed Hybrid executable is now implemented as that bridge: it packages the hosted runtime/source closure, registers eligible local functions as generated cmdlets from the typed assembly, and routes the retained entry script through the same package host. Its manifest records that runtime evaluation is allowed, identifies embedded source and dependency closure, and exposes static boundary counts. The benchmark profiler measures actual typed/hosted crossings, reports nanoseconds per crossing and the estimated share of fine-boundary runtime attributable to crossing overhead, and emits a coarsening advisory. NativeAOT-hosted Hybrid delivery and named-RID promotion remain open; the artifact must never be presented as Strict compilation.

## Compiler architecture and feature growth

The implementation sequence, ownership rules, migration gates, and active checklists are maintained in the [PowerShell Compilation Architecture Roadmap](PowerForge.PowerShellCompilation.Roadmap.md).

The current implementation is a semantic pipeline rather than a text replacement engine:

1. input discovery resolves the root script or module and its contained authored dependency closure without executing it;
2. the PowerShell parser produces syntax plus neutral source documents and spans;
3. the semantic binder creates immutable symbols, scopes, functions, statements, expressions, type facts, value state, effects, capabilities, and fail-closed diagnostics;
4. deterministic analysis passes compute definite assignment, output type/cardinality, call graphs, recursion, effects, capabilities, and fallback through fixed points;
5. lowering selects typed CLR operations, generated-cmdlet operations, bounded hosted regions, and target-specific runtime primitives;
6. the C# backend renders lowered nodes only; it has no PowerShell AST reference and performs no semantic inference;
7. graph and binary-cmdlet shaping consume the same semantic result before the artifact builder compiles, optionally signs, records hashes/source maps/ABI evidence, and atomically publishes the artifact set.

Every bound node carries:

- its resolved CLR type and PowerShell-specific conversion rule;
- its source extent for diagnostics and generated-source mapping;
- observable effects such as pipeline output, stream use, mutation, exception flow, or PowerShell runtime dispatch;
- required target capabilities, for example pure CLR, cmdlet host, bound-parameter state, PowerShell objects, or a command region;
- an explicit fallback reason when the semantic contract cannot be proven.

With that boundary, a feature is bound once and backends consume the same proven semantic model. Strict EXEs accept only pure-CLR nodes; binary modules may admit cmdlet-host nodes; Hybrid artifacts may additionally admit bounded runtime regions. The former direct AST-to-C# emitter and its partial implementations were deleted after the existing behavior migrated; there is no compatibility switch that can silently bypass the IR.

Canonical command and pipeline semantics have one implementation route within the built-in bounded provider contract. Milestones 14, 15, and 17 are complete within their documented profiles; Milestones 16 and 18 remain partial at their broader exit gates. The external provider SDK now loads independently built, exact-lock executable adapters through one versioned ABI in both Strict and Hybrid builds, and reconciles publisher/license/signature claims with canonical package metadata and signer policy. Complete provider conformance, useful provider-family implementations, and the wider trust ecosystem remain open. Named semantic profiles now govern target identity, binding, lowering, compatible `#requires`, provider selection, cache identity, package variants, artifacts, and diagnostics. Oracle schema 3 records the exact executable hash/version/length, runtime/build/release identity, OS, architecture, culture, feature switches, bounded ordered typed/null/cardinality output, streams/errors, encoding, filesystem effects, and final `LASTEXITCODE` state; replay and promotion validate structural/profile invariants against an immutable reviewed catalog for Windows PowerShell 5.1.26100.9168, PowerShell 7.4.19, and PowerShell 7.6.5, while a read-only scheduled lane proposes affected reviews for newer patch tags without advancing pins. Nineteen native minimized cases cover every currently promoted family and execute on all three pinned hosts. Compatible `#requires`, profile-fixed `$PSVersionTable.PSVersion.Major`, bounded compiler-owned dictionary flow, compile-time-safe literal conversion, stable-string interpolation, local function graphs, typed/defaulted parameters, bounded parameter-validation metadata, exact/alias/abbreviated parameter binding, index/member assignment targets, ordered typed catch filters, bounded scalar regex-switch matching, bounded allocated typed-array `ForEach-Object` enumeration with compiler-owned `$_`/`$PSItem`, bounded typed-executable begin/process/end lifecycle invocation, bounded local `Get-Help` Name/Synopsis metadata, typed integral compound arithmetic, comparison operators, and logical operators now pass the artifact-hash/inventory-bound runtime-free Strict-executable observer against the exact PowerShell 7.6.5 pin: all 19 cases (100%) have runtime-free differential evidence. The lifecycle slice accepts one explicitly typed stable-scalar `ValueFromPipeline` parameter, a compiler-allocated typed input array, and explicit begin/process/end blocks; other lifecycle shapes and binary-module lifecycle commands remain on the hosted path. The runtime-free help slice accepts one statically named compiled local function and exposes immutable Name/Synopsis strings from the canonical comment-help binder; help discovery, formatting, options, and other properties remain hosted. Nullable array inputs remain hosted until `AutomationNull` identity is explicit. A framed nullable/string/culture-sensitive Strict observation protocol and directly sequenced child-process effects also remain open. This is oracle-case coverage, not a percentage of the PowerShell language. New command families must still add one deterministic binder/registry owner, typed stage/cardinality contracts, and lowering support rather than coordinated special cases in analysis, emission, shaping, and census.

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
- the target-specific NuGet closure-lock SHA-256 consumed by project builds;
- byte sizes for the primary artifact and every durable file;
- the complete discovered dependency/resource plan and each item's delivery disposition;
- executable optimization mode and Authenticode signing evidence when requested;
- a semantic explanation fingerprint, portable statement/boundary failure map, deterministic cache/dependency/ABI/fallback/provider audit trail, and the local-only retention/redaction policy;
- optional semantic-only bound/lowered IR evidence and its integrity hash when explicitly requested.

The manifest includes the immutable final unit-disposition ledger used by coverage, explain output, census, reproduction hashes, and boundary profiling. Dependency causes are attached only to the affected source unit; manifest-wide or otherwise non-unit runtime requirements remain delivery causes. Diagnostic hashes include portable file identity as well as code, feature, location, and message. For module-pipeline delivery, the same canonical finalizer runs after the last mutation in staging, packed ZIP, unpacked folder, managed repository package, and installed-module roots. Producer-local paths are replaced with portable relative identities, file hashes are recomputed against the delivered root, and machine-local checkpoint authority is excluded. Authenticode counts are carried into a copied delivery only for byte-identical files from the verified signed source root, so an installation version rewrite or unpacked post-copy replacement cannot retain stale positive signing evidence.

A packaged EXE therefore reports `requiresPowerShellRuntime: true` and `usesPowerShellRuntimeFallback: true`. A strict CLR library reports both values as `false`. A strict binary module requires PowerShell as its cmdlet host but reports no script fallback.

PowerForge stages the complete owned artifact shape and manifest before publication. Rebuilding under the same artifact name replaces prior EXE, DLL, PDB, module-directory, generated-source directory, manifest, and exactly the resource files recorded by the previous manifest. Removed resources are deleted, unrelated neighboring files are preserved, unowned collisions fail, same-name publication is serialized across threads and processes, and a failed durable commit rolls back to the previous set instead of leaving a new binary beside stale integrity evidence.

Disposable compiler projects live under a dedicated `PowerForge/powershell-compilation` temporary root, carry an ownership marker and active lock, and are deleted after normal builds. Stale cleanup removes only marked, unlocked, non-retained compiler workspaces older than the cleanup threshold. `KeepBuildWorkspace` adds an explicit retention marker; unrelated or legacy `ps-*` directories are never scavenged by name alone.

Add `--emit-source` or `-EmitSource` to publish `<name>.generated` as part of the same atomic artifact set. It contains the exact generated `.cs` files, `.csproj`, and a `source-map.json` that maps each generated method to its authored file and line; packaged executables also include the rewritten embedded `Source.ps1`. Generated PowerShell-SDK projects pin the applicable serviced `System.Security.Cryptography.Xml` line (`8.0.4` for net8 and `10.0.11` for net10) so an independently restored inspection build does not fall back to the vulnerable transitive version currently carried by the SDK. The project can be inspected or rebuilt directly:

```powershell
dotnet build .\artifacts\MyModule.generated\MyModule.csproj -c Release
```

Every emitted source file is listed with its role, SHA-256, and size in the compilation manifest. The emitted project includes local `Directory.Build.*`, `Directory.Packages.props`, and `global.json` isolation files so an ancestor repository's MSBuild, central-package, or SDK policy cannot silently change the inspection rebuild. Rebuilding the artifact without source emission removes a prior generated-source directory so stale C# cannot be mistaken for the current binary.

Add `--emit-ir`, `-EmitIrSnapshots`, or the `Build-Module` configuration equivalent to publish `<name>.powerforge-ir.json`. This file contains stable symbol/document identities, resolved types, output cardinality/value states, capabilities, effects, disposition, and bound/lowered node kinds. It never contains authored source text, parser AST objects, literal values, absolute paths, or hosted executable source. Its hash, the failure map, audit trail, redaction policy, decision trace, diagnostics, source map, ABI, dependency lock, target, providers, compiler, and SDK are all bound into reproduction evidence.

Diagnostics are local-only and are never uploaded automatically. Manifest, trace, audit, map, and optional IR evidence follow the artifact lifetime. Failed-build or crash bundles remain user-managed and should normally be removed after seven days when no longer needed. Generated source is intentionally a separate explicit opt-in because it contains reconstructable implementation details and, for packaged artifacts, may include authored source.

## Measured performance

The checked-in benchmark suite validates every result outside the timed operation and compares typed CLR, generated cmdlet, PowerShell function, typed EXE, packaged EXE, and hand-written C# lanes. It also includes a dispatch-amortization workload that performs equivalent arithmetic through many fine cmdlet calls or one coarse generated command.

- the original PowerShell function;
- the generated binary cmdlet called through PowerShell;
- the generated typed CLR method called inside a C# loop;
- equivalent hand-written C#.

The current Windows computation and startup reference packet used PowerShell 7.6.4, Windows x64, and an AMD64 32-logical-core machine. The optimized ReadyToRun lane pinned stable same-major .NET SDK 10.0.303, and the startup metadata now records that SDK alongside every optimized artifact hash and size. Duration rows are medians after three warmups, 12 measured samples, and minimum/maximum exclusion; startup used two warmups and 10 measured samples. Every row has zero validation failures and pins clean candidate `009901b0bbf56285a1ab291e2a1bff760e05a4c9` plus generated artifact hashes.

Windows run IDs are `20260829-000416-818e0b53` and `20260829-000530-2e8dee77` (real functions), `20260829-000611-1abe7ff0` (synthetic loop), `20260829-000614-2f8f557b` (indexed array), `20260829-000616-799275f9` (dispatch and boundary profile), `20260829-000618-1a782491` (startup), and `20260829-000630-abc01afb` (local calls).

| Workload | Calls | PowerShell | Typed CLR | Hand-written C# | Typed vs PowerShell | Typed vs C# |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Real `Get-AllowedAverageMs`, absolute-cap branch | 50,000 | 203.11 ms | 6.67 ms | 3.43 ms | **30.4x faster** | 1.94x slower |
| Real `Get-AllowedAverageMs`, relative-cap branch | 50,000 | 212.24 ms | 6.42 ms | 3.39 ms | **33.1x faster** | 1.89x slower |
| IPv4-to-PTR conversion helper | 50,000 | 512.14 ms | 14.86 ms | 8.22 ms | **34.5x faster** | 1.81x slower |
| Synthetic triangular-number loop, 1,000 x 1,000 iterations | 1,000 | 37.89 ms | 4.78 ms | 3.24 ms | **7.9x faster** | 1.47x slower |
| Indexed sum over 1,000-element typed array | 1,000 | 40.48 ms | 5.64 ms | 3.68 ms | **7.2x faster** | 1.53x slower |

These results prove a benefit only for eligible computation executed as CLR code. They do not promise that an arbitrary script or a generated cmdlet call is faster.

The repeatable `powershell-compilation-typed-local-calls` lane exercises one entry script and one dot-sourced helper through both `pwsh -File` and a Strict executable. The clean run used 20,000 helper calls and recorded 354.83 ms for PowerShell versus 35.45 ms for the typed executable, or **10.0x**.

The binary-cmdlet lane includes PowerShell command lookup, parameter binding, pipeline setup, and `WriteObject` for every call. It took 2,138.31 ms and 2,095.53 ms in the two 50,000-call threshold scenarios, versus 203.11 ms and 212.24 ms for the original functions. The dispatch-amortization workload then performed equivalent work through 1,000 fine cmdlet calls or one coarse command: 46.86 ms versus 4.81 ms, a **9.7x** improvement. The useful product shape is a coarse cmdlet that performs substantial compiled work per invocation, not a tiny arithmetic cmdlet called in a PowerShell loop.

Executable startup proves that typed compilation changes the product result rather than merely its extension. The PowerShell-free typed EXE took 44.82 ms, `pwsh -File` took 186.41 ms, and the runtime-packaged EXE took 664.84 ms. The typed executable is **4.2x faster than `pwsh -File`** and **14.8x faster than packaging** in this one-shot workload. Packaging remains valuable for broad script compatibility and delivery ergonomics, not startup speed.

The optimization and footprint matrix below was rebuilt and executed with the same clean candidate and the `win-x64` runtime identifier. ReadyToRun remains a measured experiment rather than a selectable public target.

| Windows x64 artifact | Bytes | Runtime model |
| --- | ---: | --- |
| Typed framework-dependent EXE | 190,158 | installed .NET |
| Typed ReadyToRun EXE | 57,344 | installed .NET; benchmark-only |
| Typed self-contained trimmed EXE | 13,444,295 | bundled trimmed .NET runtime |
| Typed NativeAOT EXE | 2,880,000 | native, no .NET or PowerShell runtime required |
| Packaged PowerShell EXE | 54,709,527 | embedded PowerShell runtime assets |

The Hybrid boundary profile measured 1,750 crossings at 14,682.9 ns per crossing with a 0.9785 estimated boundary-overhead ratio; the corresponding non-boundary work share was approximately 0.0215. It correctly advised coarsening the boundary or retaining hosted execution; this is workload evidence, not a universal cutoff.

The 2026-08-30 M22 quick qualification packet ran twice from clean implementation commit `99c1ad0fc9a25aae5b71eb6f308cebf339fa2fbe`; every lane reported zero validation failures. The first clean build-cost row was 3,437.79 ms and the warm repeat was 2,064.29 ms, exposing the expected variance of a two-sample smoke run. The repeat allocated 44,160,528 managed bytes, changed process working set by 2,799,616 bytes, and produced a 7,168-byte primary artifact (`20260830-120136-38ce8123`). Binary-module import was 38.90 ms with 139,339 managed bytes allocated (`20260830-120141-ab5d8a68`). These quick rows verify instrumentation, budgets, and lack of a persistent regression; the full clean reference packet above remains the promotion evidence.

Target-host certification separately executed the same Strict `net10.0` framework-dependent and NativeAOT workload on Windows x64 and Ubuntu 24.04 x64. Both hosts produced exact Unicode output (`Zażółć-東京|résource-Łódź-東京|10`), rejected invalid arguments with exit code 1, preserved resources, and passed executable-format, architecture, import, permission, and dependency-closure inspection. Windows cancellation used process termination and Linux cancellation produced SIGTERM exit 143. Windows PowerShell 5.1 independently revalidated the Windows artifacts. macOS remains experimental because the approved EvoMini authentication path did not establish a session; no macOS support claim is inferred from cross-publishing.

Run the same matrix locally:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\PowerShellCompilation\powershell-compilation.benchmark.ps1 `
    -Variable @{ IncludeOptimizedExecutables = $true } `
    -RunMode local
```

See [the benchmark README](../Benchmarks/PowerShellCompilation/README.md) for the quick smoke command and lane definitions.

## Current expansion readiness

The compiler is ready for targeted semantic expansion through its canonical parser → binder → bound IR → lowering → backend route. It is not yet ready for a broad “compile arbitrary PowerShell natively” promise or a public stable-channel push. The baseline-gated public Hybrid packet builds, imports, and invokes 10/10 modules while safely retaining or runtime-routing all 735 authored units, but it emits no CLR units. The separate six-workload assessment emits 4/185 units (2.16%) and 4/173 functions (2.31%); its census still performs 0/6 complete-workload executions, while a separate opt-in qualification proves original/generated invocation parity for 2/2 selected emitted commands from unrelated workloads. Hybrid is therefore useful today as a fail-safe migration/delivery architecture; broad typed-emission benefit across multiple scenario families remains the next product proof.

The prerequisite ownership work is now in place: semantic profiles are effective inputs, the provider SDK has a locked executable ABI and canonical package/signer policy, both corpus lanes share bounded acquisition and enforce their baselines, and the active backend/bootstrapper owners were split below the 1,000-line ceiling. Coverage may widen only through these owners. The remaining release gates are exact-host structured oracle coverage, full provider conformance and useful provider families, emitted Hybrid benefit across at least three unrelated scenario families, richer Strict application evidence, and the signed/remote management ecosystem matrix.

WMI, CIM, CDXML, directory-style binary modules, native/process calls, and COM are present with deliberately different guarantees. Hybrid fixtures prove that PowerShell-hosted routes can be retained safely. A direct C# local CIM query proves part of the typed management-adapter foundation. They do not yet prove that arbitrary PowerShell WMI/CIM/CDXML commands are emitted as runtime-free CLR. The generic executable-provider ABI is now available, but Strict management support still needs a real management provider, generated-artifact execution for the complete operation family, supported authentication and transport contracts, and local/remote target-host failure and cleanup tests. Unimplemented certificate authentication was removed from the CIM enum; undefined or incompatible authentication requests now fail validation rather than mapping silently to defaults.

## Portable generic acceptance corpus

PowerForge carries a self-contained, product-neutral compiler corpus under `Benchmarks/PowerShellCompilation/Corpus`. The Hybrid module exercises parameter metadata and defaults, operators, typed recursion, runtime-state injection, command-result capture, read-only environment access, known object shapes, and mutable list flows. Its committed net10 census baseline records a portable source fingerprint and 8/8 post-emission functions (100%) with no retained-source fallback or eligible-function loss. One emitted function also records its bounded hosted command region as runtime-routed; typed coverage and runtime routing are intentionally independent measures. The fixed Strict packet contains four programs totaling 18/18 emitted units. Its four-file application contributes 11 compiled methods and matches direct execution on both `win-x64` and `linux-x64` under the enforced baseline.

This is the stable acceptance surface for generic compiler contracts. It runs from any checkout without neighboring repositories:

```powershell
powerforge powershell census `
    .\Benchmarks\PowerShellCompilation\Corpus\HybridModule\Generic.Compiler.Corpus.psd1 `
    --framework net10.0 `
    --baseline .\Benchmarks\PowerShellCompilation\Corpus\census-baseline.net10.json
```

See [the corpus README](../Benchmarks/PowerShellCompilation/Corpus/README.md) for the contract split. External repositories can be added, replaced, or removed as scale workloads without changing the compiler design. The fixed public packet remains the acceptance gate; a separate exact-hash external assessment packet records low-coverage frontier inputs without silently redefining that gate.

## Arbitrary-source eligibility

The committed acceptance surface is the product-neutral corpus under `Benchmarks/PowerShellCompilation/Corpus`. It contains generic module, multi-file, resource, lifecycle, command-provider, and executable contracts. Compiler behavior is derived from PowerShell syntax, semantic IR, dependency graphs, target contracts, and explicit resource policy—never from a repository name, module name, neighboring checkout, or conventional product-specific path.

The census command accepts any caller-supplied script, manifest, or module roots. Inputs may be added, replaced, randomized, or removed without changing compiler behavior:

```powershell
powerforge powershell census `
    .\path\to\first-module `
    --path .\path\to\another-script.ps1 `
    --framework net10.0 `
    --write-baseline .\artifacts\powershell-compilation-census.json `
    --output json
```

The census records discovery, typed/fallback coverage, parse errors, analyzer duration, dependency summaries, stable missing-feature impact, and frequent co-blocker pairs. A baseline fails when an input disappears, typed coverage decreases, fallback increases, parse errors increase, or normalized source identity changes. External source trees are private, replaceable regression workloads; they are not committed compiler configuration and cannot authorize a special-case intrinsic.

Low coverage is not hidden by Hybrid mode. Every fallback is reported with a diagnostic. The machine-readable `functionFrontier` separates occurrences, affected units, visible sole blockers, and complete-input candidates so roadmap priority comes from repeated generic semantic shapes rather than a named module. Strict mode remains the proof boundary for a module with no authored runtime fallback.

The checked-in external assessment runner adds reproducible acquisition around the same census. Each workload supplies only evidence metadata: HTTPS payload, immutable revision or package version, SHA-256, license status, entry point, and scenario family. The assessment runner never imports or executes the payload. Its baseline requires the same source fingerprint and discovered surface, rejects new parser errors or fallback regressions, and allows reviewed emission gains. A separate opt-in qualification command requires explicit external-execution consent, builds one exact acquired module, asserts a selected unit was emitted rather than runtime-routed, invokes original and generated commands in separate child processes, and records only output hashes and parity. Neither lane certifies an unexecuted complete workload.

The initial six-workload assessment covers certificate-service administration, CIM/device registration, WMI/CIM/remoting/report generation, a cross-platform installer, and a Windows package bootstrapper. It discovers 154 source files, 185 executable units, and 173 authored functions with zero parser-error files. Current post-emission results are 4/185 units (2.16%) and 4/173 functions (2.31%), with zero complete-workload executions because this is a census-only lane. The separate qualification currently proves 2/2 selected emitted commands from two unrelated workloads in one scenario family, not a complete workload or three-family benefit. These ratios describe only this pinned packet; they are not PowerShell-language coverage and do not predict arbitrary-script success.

## Security and distribution limits

Packaging and typed compilation are not obfuscation or source protection. A packaged executable contains an embedded script and runtime assets that a determined user can inspect. A typed EXE or DLL is normal managed/native code and remains analyzable.

`Build-PowerShellArtifact -SignArtifact` and CLI `--sign` sign only build-owned Windows artifacts: the generated executable or library, typed assembly, hybrid module host, and generated primary module manifest. Bundled runtime files and nested/module dependencies keep their original publisher identity. Signing happens before SHA-256 and byte-size evidence is recorded and runs in an isolated Windows PowerShell process with a bounded timeout. A missing certificate, provider timeout, or non-valid signature aborts the atomic publication; no unsigned replacement or stale manifest is committed. Concurrent replacements serialize through a durable per-artifact lock file whose exclusive handle defines ownership across Windows and Unix. The broader PowerForge release pipeline remains the owner for packaging, release attestations, NuGet/GitHub publication, and policy-level signing configuration.

The fail-closed signing and atomic-publication contract is covered by automated tests. On 2026-08-23 the internal acceptance run also produced a valid Authenticode-signed typed EXE, a net8 binary module, and a net472 binary module with the maintainer's code-signing certificate and DigiCert timestamp service. Each staged hash matched the final manifest and each artifact executed successfully in its target host. These were local internal proof artifacts only; nothing was published to PSGallery, NuGet, GitHub Releases, or another feed.

The generated EXE carries the PowerShell SDK, so it is much larger than the input script and may start more slowly than an installed `pwsh`. Self-contained publication adds the .NET runtime as well. With `SingleFile = $false`, PowerForge preserves the complete nested publish tree instead of copying only top-level files. Runtime-packaged artifacts must be rebuilt when their embedded PowerShell or .NET dependencies need security updates.

Strict typed executable compilation accepts one `.ps1` entrypoint and its contained literal dot-source dependency closure. Top-level `param()` remains the process argument contract, while source functions become direct static CLR methods. Local function calls enforce the supported validation metadata and deliberately reject mutual or uncontracted recursion, splatting, redirection, external commands, dynamic command names, and incompatible argument conversion; direct self-recursion needs a single verified `[OutputType]` contract. A managed Hybrid executable uses the same explicit entrypoint boundary but registers eligible local functions as generated cmdlets and retains the entry script plus unsupported dependencies for hosted execution. It is runtime-preserving delivery, not a Strict or NativeAOT artifact. Source `#requires` directives are accepted only when their version and PSEdition requirements are compatible with the selected semantic profile; module, assembly, host/elevation, snap-in, unknown, or higher-version requirements reject Strict and remain explicitly hosted in Hybrid. Runtime-bearing `using` statements are never erased. Hybrid module composition preserves namespace `using` and module `param` prologues for mixed `.ps1` or `.psm1` source. Generated typed export shaping requires literal unconditional exports, including colon-attached literal forms such as `-Function:Get-Value`, and contained relative file references. Conditional export logic remains in the Hybrid script fallback and executes unchanged; Strict binary modules reject it because the export contract requires PowerShell execution. Strict modules also reject `ScriptsToProcess` and script-based `NestedModules`; Hybrid records those hooks as runtime fallback. Required contained assemblies, format files, type files, and scripts must exist; named external assemblies remain manifest references rather than local files. Every staged manifest or dot-source path must remain inside the source root without symbolic-link or junction traversal. Binary-module generation routes non-Verb-Noun or otherwise unrepresentable wrappers to Hybrid script fallback and excludes their methods from the generated CLR assembly; Strict mode rejects them. Generated cmdlet output uses PowerShell's normal collection-enumeration contract rather than treating only arrays as pipelines; `OutputType` advertises an array's element type and uses `object` when an enumerable's element type cannot be proven. Expandable strings currently accept string variables only; subexpressions, non-string runtime conversion, and mixed escaped-dollar interpolation remain fallback. Enum arguments accept only defined names from literal strings. Null-to-reference overload binding remains fallback because PowerShell may convert null to an empty string where direct CLR would preserve null. A plain CLR library contains only eligible methods and no automatic PowerShell fallback host.

The typed boundary also preserves several less-visible PowerShell contracts. Indexing a null `IDictionary` yields null. Generated cmdlets for simple functions consume surplus positional arguments, while advanced functions retain advanced binding behavior. An array-returning local function stays on the Hybrid script path when a direct consumer would observe PowerShell pipeline scalarization. Observable `SwitchParameter` members or CLR identity likewise stay on the script path even though safe boolean control flow compiles. Hybrid composition keeps cross-file declaration timing conservative and removes its private dispatcher state before authored wildcard variable exports are evaluated.

Strict typed executables may request `Trimmed` or `NativeAot` optimization. Both require a RID-specific, self-contained, single-artifact build; NativeAOT already emits the native executable directly and does not enable MSBuild's separate single-file bundler. Packaged PowerShell executables are rejected because trimming a dynamic PowerShell runtime is not a safe default. Native AOT is therefore a deployment option only for the proven typed subset, not a promise that arbitrary PowerShell can be converted to native code.

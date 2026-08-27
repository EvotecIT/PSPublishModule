# PowerShell Compilation Architecture Roadmap

Last updated: 2026-08-27

This roadmap is the execution plan for growing PowerForge PowerShell compilation without turning the analyzer, transpiler, command handling, or C# emitter into increasingly coupled catch-all components.

The branch contains a substantial typed semantic compiler core: immutable bound IR, deterministic analysis, lowering, a lowered-only C# method backend, generated-project publication, help/module-contract flow, deterministic command/dependency inventories, and a PowerShell-hosted advanced-function lifecycle path. The original structural-veto, transpiler call-graph, Strict executable-shaping, and method-level source-map blockers have been removed. A 2026-08-27 exact-head review nevertheless reopened the architecture-completion gate: uncertifiable Strict closure does not yet fail the build, builds do not consume a reviewed dependency lock, command-family contracts are mostly hosted metadata rather than typed/runtime-free semantics, and lifecycle hosting still reparses AST and reconstructs pipeline input. The roadmap below preserves the completed foundation while making those remaining boundaries explicit.

The companion [PowerShell Compilation guide](PowerForge.PowerShellCompilation.md) documents current behavior, artifact modes, supported syntax, measured performance, census evidence, and distribution limits. This file records completed architecture gates as well as the remaining implementation plan.

This roadmap does not schedule a package, gallery, NuGet, or GitHub release. Those remain separate decisions after source work is complete.

## Product north star

PowerForge should offer four honest outcomes from one compiler front end:

1. **Package** arbitrary compatible source with a PowerShell runtime when semantic compilation is not required.
2. **Hybrid** compile proven regions and retain explicit PowerShell-backed behavior for the rest.
3. **Strict managed** produce a runtime-free CLR DLL or EXE whose accepted behavior lowers to ordinary typed .NET code.
4. **Strict native** publish the same runtime-free program with NativeAOT for a specific operating system and architecture.

“Python territory” means reaching the same practical product territory available to a mature scripting ecosystem: keep a source-first authoring experience, package broad dynamic programs, accelerate eligible hot regions, expose typed library APIs, and produce self-contained native executables when the complete program fits a stricter contract. It does not mean claiming that arbitrary dynamic PowerShell can become native code without a PowerShell runtime.

For this roadmap, **native-like** means all of the following:

- accepted Strict code does not load `System.Management.Automation`, start `pwsh`, or carry an embedded script fallback;
- bound operations lower to direct CLR operations or a small runtime-free support library, not to string evaluation or dynamic PowerShell dispatch;
- generated C# is readable, deterministic, source-mapped, and available as a diagnostic artifact before considering direct IL emission;
- eligible compute approaches the throughput and allocation profile of equivalent generated or hand-written C#;
- NativeAOT is a deployment backend for an already runtime-free program, not a way to hide unsupported PowerShell semantics;
- unsupported behavior is diagnosed precisely in Analyze/Hybrid and rejects Strict compilation.

The product succeeds when users can predict which of these outcomes they are getting and why. File extensions alone are not evidence of compilation.

## Status legend

- `[x]` complete and proven by current source or executable evidence
- `[ ]` required work
- **Partial** means useful implementation exists but the milestone exit gate is not yet satisfied
- **Blocked** means the milestone must not receive breadth work until an earlier gate closes
- **Current** identifies the milestone that should receive implementation effort now
- A milestone is complete only when its exit gate is satisfied

## Current position

Implementation snapshot `8fc558c0739c6e9ec81fe5bfcbc4a44849ac0936` on `feature/powershell-compilation-roadmap` contains `origin/main` and was re-proven on 2026-08-27 after the architecture and Milestones 8–10 implementation wave. The results below prove a clean branch candidate, not a default-branch merge, published package, or released product.

The compiler already provides:

- [x] Package and Strict EXE paths plus Strict/Hybrid binary-module and CLR-library paths
- [x] Strict executable build paths for framework-dependent, self-contained, trimmed, and NativeAOT publication, with fail-closed trim/AOT warning policy
- [ ] Fail Strict publication when the delivered artifact format or runtime dependency cannot be certified; NativeAOT closure and target-host promotion remain open
- [x] durable generated C# project emission for inspection and independent rebuilds
- [x] post-emission typed/fallback coverage, source-fingerprint baselines, and statement/range-level generated source mapping
- [x] capability-aware parameter contracts and supported validation metadata
- [x] omitted-versus-explicitly-bound literal defaults
- [x] typed local function graphs, conversions, operators, and control flow
- [x] bounded runtime-state intrinsics
- [x] typed code around bounded PowerShell command regions
- [x] deterministic semantic/dependency/deployment graph snapshots covering known manifest and static-source edges, managed/native assets, processes, literal COM activation, runtime content, policy, and artifact disposition
- [ ] consume one reviewed dependency lock during build, reject source/dependency drift, and resolve exact transitive module identity without importing or executing source
- [x] a product-neutral acceptance corpus and replaceable real-module census inputs
- [x] PowerShell 5.1 and supported PowerShell 7 differential coverage for the established compiler surface; hosted lifecycle target/version coverage remains partial
- [x] net472, net8.0, and net10.0 compiler build lanes

The companion guide owns the exact current benchmark numbers and runtime proof. This roadmap must not copy those figures as timeless claims; every performance or platform promotion uses a fresh clean-worktree run pinned to a source revision, toolchain, target framework, runtime identifier, and benchmark run ID.

The lowered C# method backend does not reference SMA AST types, and the former direct AST-to-C# emitter was deleted. Eligibility, call graphs, executable binding, and source mapping now consume canonical semantic or lowered contracts. The callable ABI, delivered-closure publication gate, dependency-lock handoff, command-family lowering, and hosted lifecycle adapter still need the closure work listed below.

Architecture closure is therefore **Partial / Current**. The next work is owner-level remediation in Milestones 6, 8, 9, and 10, followed by clean-target dependency/interop proof. Value/object-flow breadth remains blocked until those publication, lock, and lifecycle boundaries are trustworthy.

## Artifact ladder

The modes are different products with different guarantees. They must not be collapsed into a single “compiled” label.

| Artifact | Current state | Requires PowerShell/SMA at runtime | Native/managed result | Primary value |
| --- | --- | --- | --- | --- |
| Package EXE | Available | Yes, complete script | Managed host; optionally self-contained | Broad compatibility and delivery |
| Hybrid binary module | Available | Yes, only fallback and bounded hosted regions | Typed cmdlets plus retained script | Incremental acceleration inside PowerShell |
| Strict binary module | Available | Yes, as the cmdlet host; no script fallback | Managed DLL | Importable compiled PowerShell command surface |
| Strict CLR library | Candidate with clean-consumer proof and a versioned ABI manifest; semantic output/null/cardinality contracts remain provisional | Designed not to; managed inspection exists, publication certification incomplete | Managed DLL | Direct use from C# and other CLR hosts |
| Strict managed EXE | Candidate; managed artifact inspection exists, but an uncertifiable delivered format does not yet fail publication | Designed not to; fail-closed certification incomplete | JIT-compiled managed executable | Small runtime-free CLI/application |
| Strict NativeAOT EXE | Build path available; the verifier cannot yet certify opaque native output or bundled native dependencies, and target-host promotion is incomplete | Designed not to; certification incomplete | RID-specific native executable | No installed PowerShell or .NET requirement, low startup/footprint potential |
| Hybrid EXE | Planned | Yes, explicit fallback only | Packaged host plus typed assembly | Broad script compatibility with coarse compiled acceleration |
| Native shared library | Deferred | No | Platform ABI such as `.dll`, `.so`, or `.dylib` | Add only for a real non-.NET embedding consumer |

A C# DLL means a managed CLR library with a documented .NET API. It is not a native shared library. Native shared-library export introduces a platform ABI, marshalling, lifetime, and error-contract product of its own and must not be implied by NativeAOT executable support.

## Explicit target contract

`Strict`, `Hybrid`, and an artifact kind are not enough to describe semantic compatibility. Every compilation plan and artifact manifest should carry an explicit target contract with these dimensions:

- PowerShell semantic profile, including the behavior family being targeted rather than only a target framework;
- execution model: `RuntimeFree`, `PowerShellHosted`, or `Mixed`;
- artifact model: executable, CLR library, or PowerShell binary module;
- deployment model: framework-dependent, self-contained, trimmed, ReadyToRun experiment, or NativeAOT;
- target framework and, when platform-specific, runtime identifier and architecture;
- culture, encoding, filesystem, and operating-system assumptions that are compile-time facts versus runtime inputs;
- allowed capabilities, including command regions, host streams, module hosting, managed/native dependencies, COM, filesystem/provider access, reflection, and dynamic invocation.

The modern runtime-free profile should default to the documented PowerShell 7-compatible contract already used by Strict execution. A `net472` binary module executes inside Windows PowerShell 5.1 and is validated against that host. Where 5.1 and 7 differ, PowerForge either selects one named profile, emits a target-specific lowering, or rejects the construct. It must not claim one artifact is simultaneously identical to incompatible behaviors.

## End-state definition

PowerForge should compile every source region whose PowerShell-visible behavior can be proven for the selected target. It should use a bounded PowerShell command region when host execution is required and the boundary can be represented safely. It should retain truthful Hybrid fallback or reject Strict compilation when semantics cannot be proven.

The goal is not a second PowerShell runtime. Dynamic scope, arbitrary provider behavior, uninspectable command discovery, unrestricted ETS adaptation, and host-dependent behavior may legitimately remain runtime-backed.

The architecture is successful when:

- parsing owns syntax;
- binding owns semantic meaning;
- analysis owns relationships and propagated facts;
- lowering selects typed, hosted, or fallback execution;
- backends render decisions already made;
- reporting consumes the same semantic result instead of rediscovering eligibility;
- adding a feature has one obvious owner;
- adding a command family does not depend on registration or source order;
- new semantic owners normally remain below 800 lines and no touched non-generated compiler source/test file exceeds the 1,000-line hard ceiling.

## Non-negotiable design rules

### One semantic owner

A conversion, operator, command, pipeline stage, runtime-state value, or parameter-binding rule is defined once. Analyzer, emitter, artifact builder, CLI, and census code must not carry parallel implementations.

### No AST semantics in backends

PowerShell AST objects stop at parsing and binding. Backends may receive neutral source spans and an exact source slice for a bounded runtime region, but they do not inspect AST nodes to infer type, cardinality, effects, or support.

### No emitter inference

An emitter may choose formatting and target-specific syntax. It may not infer local types, return types, output cardinality, command meaning, required host state, or fallback behavior.

### Deterministic registration

Command and semantic handlers are selected by canonical identity and supported invocation shape. They are not tried in registration order. Duplicate registrations and ambiguous matches fail validation.

### Delete migrated legacy paths

The transition is incremental, but not permanently dual-path. Every migrated semantic area removes its corresponding direct AST-to-C# path. New language features use the bound representation only.

### Generic compiler behavior

External modules are regression workloads, not compiler design targets. Product names, command names, annotations, or module-specific workarounds do not become compiler intrinsics.

### Artifact proof over internal proof

Unit tests protect pure semantic rules. Completion also requires generated artifact execution, PowerShell differential behavior, export/help verification where relevant, and census evidence.

### Generated C# before direct IL

Readable C# remains the canonical executable lowering until evidence shows that Roslyn/MSBuild prevents a required semantic or performance result. Generated source, PDBs, sequence points, diagnostics, and independently rebuildable projects are product features. Direct IL is a later backend optimization, not an architectural shortcut around binding or lowering.

### Runtime-free means runtime-free

Strict runtime-free artifacts must have a mechanically verified dependency closure with no `System.Management.Automation`, PowerShell SDK, embedded source fallback, `pwsh` child process, `ScriptBlock`, or string-evaluation path. A feature that needs any of these is hosted, Hybrid, or rejected.

### NativeAOT is a backend constraint

NativeAOT does not expand the supported PowerShell language. It further restricts an already Strict program. Reflection, dynamic code generation, assembly discovery, serialization, globalization, native dependencies, and trimming behavior must be represented as capabilities and validated before AOT publication.

### Source is data at compile time

Parsing, binding, census, and code generation do not execute the input program, import its modules for side effects, run profile scripts, or invoke discovered commands merely to infer their behavior. Optional external metadata acquisition is explicit, isolated, lockable, and recorded as a build input.

### Dependency closure is explicit

Source eligibility and artifact completeness are separate decisions over the same graph. A compiled function is not ready when its required module, managed assembly, native asset, COM registration, format/type data, or runtime resource is unresolved. Every dependency has one deterministic identity, transitive closure, target capability, and artifact disposition.

## Target pipeline

```text
source discovery
      |
      v
PowerShell parser
      |
      v
semantic binder
      |
      v
immutable typed bound IR
      |
      v
analysis passes
  - type and conversion propagation
  - value state and output cardinality
  - control flow and definite assignment
  - function call graph
  - effects and mutation
  - capability requirements
  - fallback planning
      |
      v
lowering
  - typed CLR operations
  - generated cmdlet operations
  - bounded PowerShell regions
      |
      v
backends
  - C# source and source maps
  - Strict managed executable or CLR library
  - Strict NativeAOT executable
  - binary module or Hybrid host
  - diagnostics and census
      |
      v
artifact builder and atomic publication
```

## Ownership boundaries

### `PowerForge.PowerShell`

Owns PowerShell-specific behavior:

- source parsing and AST adaptation;
- semantic binding;
- PowerShell type, conversion, truthiness, null, and enumeration rules;
- parameter and advanced-function semantics;
- pipeline and command semantics;
- PowerShell module classification, static module metadata, host requirements, and module-to-command binding;
- PowerShell-specific managed/native/COM capability boundaries;
- runtime-state and host requirements;
- bound IR, analysis passes, and PowerShell-specific lowering;
- generated cmdlet and Hybrid boundary behavior.

### `PowerForge`

Owns host-neutral behavior where it already fits the dependency direction:

- stable public compilation plans and results;
- artifact-neutral models;
- host-neutral dependency/deployment graph, provenance, and artifact-disposition models;
- build, filesystem, integrity, and artifact orchestration;
- reporting models that do not require SMA types.

Do not move PowerShell semantics into `PowerForge` merely to make the IR look generic. The IR is an internal PowerShell compiler model, not a speculative general compiler framework.

### Runtime-free support substrate

When the same emitted helper is needed by more than one backend, introduce one compiler-owned runtime-free support substrate, provisionally `PowerForge.CompiledRuntime`. Its shape is determined during the IR migration rather than copied into templates ad hoc.

It owns executable implementations of proven PowerShell-compatible primitives such as truthiness, scalarization, enumeration, comparison, conversion, numeric promotion, wildcard/regex behavior, stream records, and error shaping. It must:

- have no dependency on SMA or a PowerShell host;
- avoid unbounded `dynamic`, reflection emit, runtime compilation, and string evaluation;
- be trim- and NativeAOT-safe with analyzer warnings treated as errors;
- expose an internal, versioned ABI to generated artifacts;
- multi-target only the CLR families required by real artifacts;
- remain small enough that generated code still performs direct CLR operations for ordinary typed cases.

The binder remains the semantic owner. The support substrate implements decisions already present in bound/lowered nodes; it does not rediscover PowerShell meaning at runtime. Whether helpers are statically linked, source-included, or referenced as a package is an artifact decision, but all forms originate from the same implementation and version.

### `PowerForge.Cli` and PSPublishModule

Remain thin surfaces:

- parse user input;
- map options to engine requests;
- invoke the shared engine;
- format results and diagnostics.

They do not decide eligibility, conversions, command semantics, artifact contents, or fallback behavior.

## Bound IR contract

Every bound node has a stable node identity and carries:

- a neutral source span: file, offsets, line, and column;
- a PowerShell semantic type;
- a CLR representation type when one is available;
- null, missing, and no-output behavior;
- scalar or collection cardinality;
- observable effects;
- required target capabilities;
- execution disposition;
- a stable fallback reason when typed behavior cannot be proven.

### Type information

The type model must distinguish:

- known CLR type;
- collection and element type;
- nullability and missing-value behavior;
- `PSObject` or adapted-object representation;
- statically known property shape;
- required PowerShell conversion;
- dynamic or unknown type with a reason.

`System.Type` alone is not the PowerShell type model.

### Value state

The IR represents these states explicitly:

- ordinary value;
- `$null`;
- `AutomationNull.Value`;
- no pipeline output;
- uninitialized or missing;
- unknown.

### Cardinality

The IR distinguishes:

- no output;
- exactly one scalar;
- zero or one;
- a fixed collection;
- an enumerated pipeline stream;
- zero or more;
- unknown.

### Effects

Effects are structured data, not an expanding group of unrelated booleans:

- local, parameter, script, module, or global mutation;
- success output;
- verbose, warning, debug, information, error, or progress output;
- host interaction;
- runtime-state read or write;
- external command dispatch;
- provider or filesystem access;
- terminating and nonterminating exception behavior;
- dynamic invocation.

### Execution disposition

Binding and fallback planning assign one of:

- `Typed`;
- `PowerShellCommandRegion`;
- `WholeFunctionFallback`;
- `StrictRejected`.

Each non-typed disposition includes a stable feature identifier, a precise explanation, its source span, and the causal semantic requirement.

## Managed CLR ABI

A generated DLL needs a stable consumer contract, not merely public methods that happen to compile. The compiler must define two related but separate surfaces:

- an internal generated ABI used by compiled functions, cmdlet wrappers, and Hybrid dispatch;
- an opt-in C#-friendly public ABI for functions whose complete signature and output contract can be proven.

The public ABI records a deterministic mapping from PowerShell command identity to CLR namespace, type, method, and generated DTO identities. It also records:

- parameter-set and overload mapping;
- required, optional, switch, remaining-argument, and omitted-versus-bound behavior;
- nullability, collection element type, output cardinality, and exception contract;
- sync versus future async/cancellation behavior;
- semantic-profile and compiler-runtime ABI versions;
- any behavior intentionally unavailable to direct CLR callers, such as PowerShell common parameters or host streams.

Functions with heterogeneous success output, unresolved `PSObject` shape, host-only streams, dynamic parameters, or ambiguous parameter-set mapping do not receive a misleading typed public API. They remain internal, use an explicitly less-typed contract when the user requests one, or are omitted/rejected with diagnostics.

Generated source and the artifact manifest are the compatibility evidence. A change in normalized public signatures or generated DTO schema is an ABI change even if compilation still succeeds.

## Semantic fidelity contract

Binding, lowering, and any real runtime-free support owner must explicitly cover the PowerShell behaviors most likely to produce fast but wrong code:

- numeric literal typing, overflow promotion, arithmetic, comparison, and culture;
- `$null`, missing, `AutomationNull.Value`, no output, scalarization, and collection enumeration;
- case-insensitive string, wildcard, regex, dictionary, and member-name behavior;
- expandable strings, formatting, encoding, line endings, and native-process argument rules;
- truthiness and conditional conversion;
- parameter binding, validation order, omitted defaults, switch identity, and error category;
- success and non-success streams, terminating versus nonterminating errors, and exit codes;
- lexical, script, module, dynamic, and closure scope;
- property/method binding across CLR objects, dictionaries, `PSCustomObject`, and bounded ETS shapes;
- pipeline lifecycle, record cardinality, and ordering;
- platform facts, environment state, filesystem/provider behavior, and resource lifetime.

Every accepted Strict construct has a named semantic contract and a differential oracle. “It emits valid C#” is not an acceptance criterion.

## Type acquisition and gradual compilation

Native-like code depends on types that are proven rather than guessed from one observed run. Type information is acquired in this order:

1. explicit parameter, variable, cast, and return constraints in authored PowerShell;
2. literal and operator semantics;
3. CLR constructor, property, method, generic, and overload metadata;
4. versioned command-family contracts;
5. interprocedural fixed-point inference across the reachable local function graph;
6. explicit compiler annotations only when standard PowerShell cannot express a useful stable contract.

`[OutputType()]` is useful contract evidence but not proof by itself because ordinary PowerShell does not enforce it. Validation attributes narrow accepted input but do not silently change the underlying type. Profile-guided observations and census data may prioritize work; they never specialize a Strict artifact around values seen during training.

The compiler should expose why a value became typed, widened, or unknown. One unresolved value does not automatically poison an entire Hybrid program: analysis finds the smallest safe typed region and the explicit boundary around it. Strict remains all-or-nothing over the reachable program closure.

PowerForge-specific annotations are a last resort. They must remain inert and valid in ordinary PowerShell, describe a reusable semantic fact rather than force an emitter, and be checked against actual runtime behavior. Do not require users to rewrite normal scripts into disguised C# merely to satisfy the compiler.

## Command and pipeline architecture

Commands are not handled through a growing ordered `if` or `switch` spread across compiler stages.

```text
command AST
    |
    v
canonical command and alias resolution
    |
    v
deterministic command semantic registry
    |
    v
command-family binder
    |
    v
bound command or pipeline node
```

The registry is keyed by resolved command identity. A command-family binder returns one definitive result:

- a supported semantic shape;
- an unsupported shape with a fallback reason;
- or an invalid invocation diagnostic.

It does not return “try the next handler.” Duplicate owners are an architecture error.

### Stream commands

One stream-semantic owner handles:

- `Write-Output`;
- `Write-Verbose`;
- `Write-Warning`;
- `Write-Debug`;
- `Write-Information`;
- `Write-Error`.

It produces a `BoundWriteStream` with stream kind, bound value, error behavior, cardinality, and effects. Emitters only render that node.

`Write-Host` remains separate because it is host/UI interaction rather than an ordinary stream write. `Write-Progress` also retains its own host capability and record lifecycle.

### Projection commands

`Select-Object` belongs to a projection-semantic owner. Literal property selection, expansion, and bounded `First`, `Last`, or `Skip` shapes can produce a typed `BoundProjection`. Dynamic calculated properties or unsupported parameter combinations become a command region or fallback with an explicit reason.

### Pipeline transformations

Typed pipeline stages have separate semantic owners:

- filtering: `Where-Object`;
- mapping: `ForEach-Object`;
- projection: `Select-Object`;
- ordering: `Sort-Object`;
- later grouping and aggregation where their contracts are proven.

`$_` and `$PSItem` bind to explicit pipeline symbols. They are not magic variable-name checks inside a backend.

### General commands

Commands without typed semantics lower to a `BoundPowerShellCommandRegion` when the boundary is safe. The region records:

- the exact source slice;
- bound input values;
- captured output and cardinality;
- stream behavior;
- required host capabilities;
- state crossing the boundary.

This is the generic path for AD, CIM, remoting, filesystem, registry, and third-party commands. Their names do not become compiler intrinsics.

### Runtime-free command adapters

A command can participate in Strict runtime-free compilation only through a deterministic, versioned command-family contract. A runtime-free adapter declares:

- canonical command identity and supported parameter shapes;
- semantic profile and version;
- bound input/output type, cardinality, streams, errors, effects, and dependencies;
- the CLR operation or support-runtime API used for lowering;
- trimming, NativeAOT, platform, and security capabilities;
- explicit unsupported shapes.

This is the path for carefully proven families such as JSON, CSV, filesystem, HTTP, or text processing. It is not permission to make every cmdlet name a special case. Prefer the platform or existing Evotec reusable owner, and keep the compiler adapter as contract plus lowering. The compiler never executes the command during analysis.

A general third-party compiler-plugin model is deferred until built-in adapters prove the registration, versioning, trust, and diagnostic contracts. Loading arbitrary analyzer packages is code execution during the build and needs an explicit security and compatibility design.

## Module and dependency architecture

PowerForge already inventories local source, manifests, `RequiredModules`, `RequiredAssemblies`, `NestedModules`, native libraries, and runtime content. The redesign must turn that inventory into three connected but distinct graphs:

1. **Semantic graph** — source units, functions, commands, CLR members, types, effects, and fallback propagation.
2. **Dependency graph** — modules, assemblies, native libraries, resources, versions, identities, and transitive edges.
3. **Deployment graph** — what is compiled, referenced, hosted, embedded, copied, restored, externally required, or rejected for the selected artifact.

One flat file list cannot answer all three questions. A dependency node records:

- canonical identity: module name/GUID/version, assembly identity, package identity, CLSID/ProgID, native library name, or content path;
- discovery edge: manifest `RequiredModules`, `NestedModules`, `RequiredAssemblies`, `ScriptsToProcess`, `TypesToProcess`, `FormatsToProcess`, `FileList`, `using module`, `using assembly`, `#requires`, literal `Import-Module`, dot-source, CLR reference, native load, or explicit build input;
- source location, hash, publisher/signature, repository/feed provenance, and license/redistribution disposition where known;
- target framework, PowerShell edition/version, OS, architecture, and runtime capabilities;
- semantic role and available metadata;
- transitive dependencies, cycles, conflicts, and load order;
- artifact disposition and the reason for it.

### Module classification

The resolver classifies every reachable module before lowering:

| Module shape | Analyze | Hybrid/Package | Strict runtime-free |
| --- | --- | --- | --- |
| Contained script module with source | Parse manifests/source and build a semantic graph | Compile eligible regions and preserve hosted fallback | Compile only when the complete reachable source contract is supported |
| Binary PowerShell module | Read manifest and static assembly/cmdlet metadata without importing it into the compiler process | Load through the selected PowerShell host; typed code may surround bounded calls | Reject command use unless a separate runtime-free adapter exists |
| Mixed script/binary module | Analyze each component and its shared exports/dependencies | Compose typed, binary-hosted, and script-fallback behavior explicitly | Require every reachable component to have a runtime-free lowering |
| CDXML/CIM, implicit-remoting, workflow, or dynamic-proxy module | Record host, endpoint, provider, and dynamic metadata requirements | Host through PowerShell when target capabilities match | Reject unless a purpose-built runtime-free owner exists |
| Managed library used directly from PowerShell | Read reference metadata and bind statically provable CLR calls | Compile direct CLR calls; preserve unresolved PowerShell behavior | Compile when the assembly closure and target contract are compatible |
| Native library or external executable | Record RID, architecture, calling/process contract, and deployment inputs | Host or invoke through an explicit bounded boundary | Allow only through a proven native/process adapter with exact lifetime and error semantics |

Module names such as `ActiveDirectory` are workloads, not compiler intrinsics. A conventional AD script should be able to compile typed validation, transformation, filtering, and result shaping around a hosted `Get-AD*`/`Set-AD*` command region. The resulting Hybrid artifact still requires a compatible Active Directory module, PowerShell host, operating system, RSAT/server capability, and reachable service. Strict compilation rejects that command path unless a separate runtime-free AD capability is deliberately designed and validated outside the generic compiler.

### Deterministic module resolution and restore

Analysis must not depend on whichever module happens to win `PSModulePath` lookup on the build machine. Resolution uses an explicit root set and produces a lockable graph containing exact module GUID/version, source/feed, path or package hash, PowerShell edition, architecture, and transitive identities.

The workflow separates:

1. **Resolve** metadata without executing module initialization.
2. **Restore** missing modules/packages only when the user explicitly requests acquisition from allowed repositories.
3. **Analyze** the locked inputs.
4. **Build** without changing the resolved graph.
5. **Validate** the real artifact in a clean target environment.

PowerForge must not silently download, update, or bundle a module during analysis. Version ambiguity, dependency cycles, incompatible GUID/version constraints, architecture mismatch, missing native assets, or conflicting assembly identities fail with a causal graph diagnostic.

Bundling is not the default for external modules. The deployment plan chooses one of:

- compile contained source;
- reference an exact managed assembly;
- preserve and host an exact module;
- embed/copy a redistributable dependency and its transitive assets;
- restore into a private artifact-local module root;
- require a target-installed module/runtime capability;
- reject the dependency for this target.

License, publisher, signature, servicing, and redistribution constraints are recorded separately from technical resolvability. An installed Windows/RSAT module must not be copied into an EXE merely because the compiler can find its files.

### Binary wrappers over managed libraries

Many PowerShell modules are thin cmdlet/function surfaces over ordinary .NET libraries. PowerForge should distinguish three paths:

- **Hosted cmdlet path:** preserve PowerShell parameter binding, dynamic parameters, common parameters, streams, `ShouldProcess`, and module state by invoking the binary module inside PowerShell.
- **Direct CLR path:** bind authored `[Namespace.Type]` calls or a proven adapter directly to the public managed API and include the exact assembly dependency closure.
- **Generated adapter path:** expose a versioned runtime-free command adapter only when cmdlet semantics and the underlying CLR operation have a reviewed one-to-one contract.

PowerForge does not automatically bypass a cmdlet wrapper just because its implementation assembly is visible. The wrapper may own validation, security, retries, cancellation, streams, provider context, or lifecycle semantics that a direct method call would lose.

Managed resolution must account for target-framework compatibility, assembly unification, load context, binding redirects for `net472`, package/runtime assets, satellite assemblies, configuration, and native dependencies. A root DLL without its transitive closure is not a complete artifact.

## Native and COM interop boundaries

Native libraries and COM are capability families, not ordinary CLR calls.

### Native libraries and processes

A runtime-free native adapter declares RID/architecture, library identity and hash, entry points, calling conventions, marshalling, ownership, thread safety, cancellation, error translation, and unload/process lifetime. Native files are selected from the dependency graph and validated on the target host. A matching filename beside the artifact is not sufficient proof.

External executables use a separate process contract covering arguments, quoting, environment, stdin/stdout/stderr encoding, exit codes, signals, timeout, cancellation, credentials, and child cleanup. Hybrid may retain authored PowerShell native-command semantics; Strict requires a proven process adapter.

### COM

COM support should begin as an explicit Windows-only hosted capability:

- Package/Hybrid can retain `New-Object -ComObject`, `[type]::GetTypeFromProgID`, or equivalent activation inside a PowerShell region;
- the target contract records Windows, process bitness, ProgID/CLSID, registration requirement, apartment state, interactive/session constraints, and external installation;
- COM objects do not cross a typed/hosted boundary as unconstrained `object`; operations remain in one hosted region or cross through a reviewed typed DTO boundary;
- the host owns STA/MTA selection, thread affinity, cancellation, cleanup, and exception translation;
- build and tests never activate COM merely to discover its API.

Future Strict managed COM support may use a known PIA/type library or generated interop for a finite interface set. It requires explicit interface identity, marshalling, apartment, lifetime, registration-free/deployment, bitness, and target-host tests. NativeAOT COM support remains rejected until the selected .NET/platform toolchain and generated interop path are proven for that exact contract. Do not fall back to late-bound `dynamic` and call it native compilation.

COM automation is not cross-platform. A script using Word, Excel, WMI-era COM, or another registered server can still gain Hybrid acceleration around the COM region, but the artifact must report its Windows/runtime/application dependencies plainly.

## Hybrid and fallback ergonomics

Fallback is a supported execution plan, not an analyzer failure hidden behind a warning. The planner selects the smallest safe disposition across both the call graph and dependency graph:

1. direct typed operation;
2. typed function or region;
3. bounded hosted command/module/COM region;
4. whole-function script fallback;
5. whole-entrypoint Package execution when boundaries cannot be represented safely;
6. Strict rejection when the requested artifact forbids the required host.

Each boundary has a `BoundaryContract` that describes inputs, outputs, cardinality, streams, errors, cancellation, state mutation, object lifetime, host/runspace, module requirements, and thread/apartment requirements. Primitive values, proven CLR types, and generated DTOs can cross directly. Unbounded `PSObject`, provider objects, live directory sessions, COM RCWs, runspace-bound objects, and other host-owned state remain inside the hosted region unless a reviewed adapter normalizes them.

Fallback propagation is causal. If function `A` stays hosted because it calls `B`, which needs module `C`, the diagnostic chain reports all three nodes and the missing capability. Adding or resolving an unrelated module must not change registration order or select a different handler.

The user-facing plan should answer:

- what compiled and at what granularity;
- what remained hosted and why;
- which module/runtime/platform dependencies must exist on the target;
- which dependencies are bundled versus external;
- what values and state cross each boundary;
- whether boundary cost is likely to dominate the compiled work;
- what source or contract change would unlock a stricter artifact.

Users should not need compiler annotations for ordinary Hybrid behavior. Optional policy can force a function to remain hosted or require it to compile, but policy never overrides an unsafe boundary or missing dependency.

## Hybrid executable architecture

A Hybrid EXE is the compatibility bridge between Package and Strict. It is not a weaker Strict artifact.

The host packages the selected PowerShell runtime and authored fallback closure, loads a generated typed assembly, and exposes compiled functions through one explicit dispatcher/command boundary. The manifest reports typed regions, runtime fallback units, boundary crossings, embedded source, and `requiresPowerShellRuntime: true`.

The implementation gate is deliberately coarse:

- compile complete local function graphs or large regions, not individual arithmetic expressions that cross the host boundary repeatedly;
- define argument, output, stream, error, cancellation, and runtime-state transfer once;
- preserve authored help and source identity for fallback functions;
- resolve and preflight the locked hosted-module graph before executing the entrypoint;
- isolate private bundled modules from ambient `PSModulePath` while leaving declared external modules external;
- never allow typed code and fallback code to mutate the same unresolved scope implicitly;
- benchmark boundary cost and require representative work to amortize it;
- keep Package as the correct answer when no region benefits from compilation.

Hybrid EXE work starts only after the bound IR can describe the same typed/fallback plan already used by Hybrid modules. It must reuse that owner rather than grow a second executable-only analyzer or dispatcher.

## Managed and native delivery architecture

The C# backend produces one deterministic generated project. Publication then selects a delivery backend:

- framework-dependent managed build;
- self-contained managed build;
- trimmed self-contained build;
- optional ReadyToRun benchmark lane, promoted only if it adds measured value;
- NativeAOT build for a supported RID.

The NativeAOT lane requires:

- a complete runtime-free reachable closure;
- zero trim/AOT analyzer warnings in compiler-owned source;
- no accidental reflection or dynamic-code roots;
- explicit globalization, serialization, COM, native-library, and resource behavior;
- native symbol and source-map strategy for crash diagnosis;
- execution on the target OS/architecture, because cross-publication is not runtime proof;
- a dependency and vulnerability manifest for compiler runtime, SDK, and native inputs.

NativeAOT is currently proven for a narrow Strict executable subset. Promotion to a generally supported artifact requires repeatable Windows, Linux, and macOS proof for the RIDs PowerForge names as supported. Architectures without a runnable validation host remain experimental even when `dotnet publish` succeeds.

## Compiler user experience

The product surface should make the compiler’s decision inspectable before a potentially expensive build:

```text
powerforge powershell analyze  <source> --target <contract> --output json
powerforge powershell explain  <source> --target <contract>
powerforge powershell emit-csharp <source> --target <contract> --output <directory>
powerforge powershell build    <source> --target <contract> --output <directory>
powerforge powershell test     <source> --target <contract> --against <pwsh|powershell>
```

Exact command names may follow current CLI conventions. The required workflow is:

1. analyze without executing source;
2. show typed, hosted, fallback, and rejected regions with causal diagnostics;
3. optionally emit the deterministic C# project and source maps;
4. build one explicit artifact contract;
5. run differential and artifact smoke tests;
6. publish/sign/package through the existing PowerForge release owner.

The CLI and PSPublishModule cmdlet only map these options to shared engine requests and format results. IDE or language-server integration can consume the same analysis result later; it does not get a separate eligibility engine.

## Reproducibility, provenance, and trust

Every artifact manifest should record enough input identity to reproduce and audit the build:

- compiler and support-runtime versions;
- semantic profile and capability contract;
- normalized source and resource hashes;
- generated-source hash and public ABI hash;
- target framework, RID, SDK, optimization, and relevant MSBuild properties;
- resolved managed/native dependencies and lock-file identity;
- runtime fallback and embedded-source facts;
- build, signing, and publication identities without secret material.

Generated source should be path-stable and deterministic. Package restore is locked or otherwise provenance-bound for release builds. Signing, SBOM/provenance generation, atomic publication, and public feed/release handling remain in PowerForge’s packaging/release layer after compilation succeeds.

Compilation is not a sandbox or obfuscation boundary. The compiler must not execute input during analysis, but the generated artifact has the authority of the user who runs it. Package and Hybrid artifacts carry inspectable source and a PowerShell runtime; managed and native Strict artifacts remain analyzable binaries.

## Intended source layout

The final names may follow nearby repository conventions, but responsibilities should remain recognizable:

```text
PowerForge.PowerShell/Services/Compilation/
  FrontEnd/
    PowerShellSourceParser.cs
    ParsedSourceDocument.cs
    SourceSpan.cs
  BoundTree/
    Nodes/
    Symbols/
    Types/
    Effects/
    Diagnostics/
  Binding/
    PowerShellSemanticBinder.cs
    BindingContext.cs
    Expressions/
    Statements/
    Parameters/
    Commands/
    Modules/
  Analysis/
    TypeFlow/
    DataFlow/
    CallGraph/
    Effects/
    Capabilities/
    Dependencies/
    Fallback/
  Dependencies/
    ModuleResolution/
    AssemblyResolution/
    NativeAssets/
    Locking/
  Lowering/
    Typed/
    Cmdlets/
    Hybrid/
    Pipelines/
    Interop/
  Interop/
    Managed/
    Native/
    Processes/
    Com/
  Backends/
    CSharp/
    BinaryModule/
    Executable/
  Reporting/
    Census/
    SourceMaps/

PowerForge.CompiledRuntime/               # create only when shared emitted helpers justify it
  Values/
  Conversion/
  Enumeration/
  Comparison/
  Errors/
  Streams/
```

This is an ownership map, not permission for a folder-only rewrite. Create each area when its first real semantic slice migrates.

## Milestone summary

| Milestone | Status | Result |
| --- | --- | --- |
| 0. Freeze and inventory current contracts | Complete | A trustworthy behavioral and ownership baseline |
| 1. Establish compiler boundaries | Complete | Parsing, semantic IR, lowering, backends, reporting, and artifact contracts have one-way boundaries |
| 2. Implement foundational bound IR | Complete | Simple functions compile through IR |
| 3. Add deterministic analysis passes | Complete | The semantic call graph is the sole graph authority and fixed-point results are order-stable |
| 4. Separate lowering from emission | Complete | Backends and statement-level source maps consume lowered contracts |
| 5. Migrate all current behavior | Complete | The pre-existing compiler surface and executable invocation shaping were migrated; Milestone 10 must move its later hosted lifecycle path onto the same boundary |
| 6. Define runtime-free artifact contract and managed ABI | **Partial / Current** | Versioned ABI and warning policy exist; fail-closed delivered closure and semantic output/null/cardinality contracts remain |
| 7. Preserve help and module contracts | Complete | Compiled functions retain full help/export behavior |
| Architecture-closure checkpoint | **Partial / Current** | Core semantic/back-end consolidation is complete; publication certification, consumed locks, command-family lowering, lifecycle ownership, and growth headroom remain |
| 8. Build command and pipeline semantics | **Partial / Foundation** | Deterministic registry and hosted stage contracts exist; typed family nodes, full stream ownership, runtime-free adapters, and extensible registration remain |
| 9. Resolve modules, dependencies, and interop | **Partial** | Deterministic inventory/planning exists; exact transitive resolution, consumed lock enforcement, complete COM discovery, and clean-target fixtures remain |
| 10. Complete advanced-function lifecycle | **Partial** | A PowerShell 7 Hybrid hosted path exists; canonical binding, raw-input fidelity, cleanup guarantees, and the PowerShell 5.1/version capability matrix remain |
| 11. Complete value and object flows | Blocked | Starts after the publication/lock/lifecycle closure path and Milestone 9 clean-target interop proof close |
| 12. Expand bounded runtime state | Planned | More real helpers compile without accepting arbitrary dynamic scope |
| 13. Run generic coverage waves | Planned | Coverage rises through semantic families, not product special cases |
| 14. Productize managed, Hybrid, and native delivery | Planned | Reproducible DLL/EXE outputs have runtime, RID, ABI, and provenance proof |
| 15. Optimize proven IR | Planned | Performance work follows semantic stability and hand-C# comparison |

Immediate closure order:

1. Make uncertifiable Strict delivered closure fail publication and add a build-level rejection test.
2. Add a first-class reviewed dependency lock to the build request, verify all source/dependency hashes at consumption, and reject drift.
3. Move hosted lifecycle discovery/binding into the canonical front-end result, preserve the original pipeline record, and make cleanup plus PowerShell-version behavior explicit.
4. Promote command-family metadata into typed semantic/lowered nodes, complete stream ownership, and prove at least one injected runtime-free adapter.
5. Run the external-module, managed-wrapper, native/process, and Windows COM clean-target fixtures before starting broad value/object-flow work.

## Milestone 0 — Freeze and inventory current contracts

- [x] Freeze broad language and command feature additions until the IR migration gate is met.
- [x] Record the current compiler-filtered test count and exact command.
- [x] Record generic-corpus artifact and census evidence.
- [x] Record a clean benchmark baseline for interpreted, Package, Hybrid, Strict managed, Strict NativeAOT, and equivalent hand-C# lanes where each exists.
- [x] Record current startup, throughput, allocations/working set, artifact-set size, and build time without filling missing lanes with inferred numbers.
- [x] Record which NativeAOT RIDs were executed on their target hosts and mark build-only RIDs experimental.
- [x] Record the current Strict dependency closure and any runtime helper code duplicated across generated backends.
- [x] Record current `RequiredModules`, assembly, native-library, resource, and external-requirement behavior separately for Package, Hybrid, Strict, BinaryModule, and CLR-library artifacts.
- [x] Inventory compiler production/test file sizes, partial-type totals, and semantic owners approaching 700, 800, or 1,000 lines.
- [x] Refresh the wider pinned census or label older figures as historical engine snapshots.
- [x] Inventory every owner that performs type inference, conversion, effects, command recognition, graph propagation, capability selection, or fallback classification.
- [x] Identify duplicated decisions across analyzer, transpiler, emitters, policies, artifact shaping, and census reporting.
- [x] Record current public CLI, cmdlet, plan, manifest, source-map, export, and artifact contracts.
- [x] Define the current PowerShell 5.1/7 and target-framework acceptance matrix.

Exit gate: the refactor has a behavioral baseline and an explicit ownership map. No current behavior depends on undocumented assumptions.

## Milestone 1 — Establish compiler boundaries

- [x] Define the dependency direction between parsing, binding, IR, analysis, lowering, backends, reporting, and artifact orchestration.
- [x] Define how module/assembly resolution, explicit restore, dependency locking, and interop capabilities feed the compiler without importing/executing source modules.
- [x] Define a neutral `SourceSpan` that does not expose SMA AST objects.
- [x] Define immutable symbol identities for files, functions, parameters, locals, pipeline variables, and generated commands.
- [x] Add one minimal parser-to-binder-to-backend path.
- [x] Remove AST consumption from backends and from Strict executable parameter/invocation shaping; AST remains a parser/front-end input.
- [x] Prevent binders from producing C# strings.
- [x] Keep the IR internal until a real external consumer requires a stable public API.

Exit gate: an empty or literal-returning program flows through the complete new pipeline.

## Milestone 2 — Implement foundational bound IR

- [x] Program, source document, function, parameter, block, statement, and expression nodes.
- [x] Symbols and lexical scopes.
- [x] Literal, variable, assignment, conversion, invocation, and return nodes.
- [x] PowerShell type and CLR representation models.
- [x] Type-fact provenance explaining explicit, inferred, command-contract, widened, and unknown results.
- [x] Value state and output cardinality.
- [x] Effects and required capabilities.
- [x] Execution disposition and stable fallback reasons.
- [x] Source spans on every node that can produce a diagnostic or generated line.

Exit gate: the simplest existing Strict functions compile entirely through bound IR with equivalent source-map evidence.

## Milestone 3 — Add deterministic analysis passes

- [x] Definite assignment and read-before-write analysis.
- [x] Local and parameter type propagation.
- [x] Return and success-output type inference.
- [x] Pipeline cardinality and scalarization analysis.
- [x] Make the semantic call graph the sole graph authority; remove transpiler reconstruction from `CommandAst`.
- [x] Recursive fixed-point analysis.
- [x] Effect propagation through local calls.
- [x] Capability propagation through local calls.
- [x] Fallback propagation with causal diagnostics.
- [x] Stable results independent of file, declaration, registration, and traversal order.

Exit gate: reversing input-file and function-declaration order produces equivalent bound plans, diagnostics, and artifacts.

## Milestone 4 — Separate lowering from emission

- [x] Define lowered function, parameter, local, control-flow, stream, pipeline, command-region, and return forms.
- [x] Move target selection into lowering.
- [x] Make the C# backend render lowered nodes only.
- [x] Remove local and return type inference from the C# emitter.
- [x] Remove eligibility and fallback decisions from emitters.
- [x] Remove command recognition from emitters.
- [x] Make source maps consume bound/lowered spans and record generated ranges plus source line/column ranges at statement-level diagnostic precision.
- [x] Make census consume the shared semantic result.

Exit gate: the C# backend builds without a reference to PowerShell AST types.

## Milestone 5 — Migrate all current behavior

Migrate one semantic area at a time. Each completed item includes deletion of the equivalent legacy path.

- [x] Parameters, aliases, metadata, parameter sets, and validation.
- [x] Literal defaults and omitted-versus-bound state.
- [x] Variables, assignments, returns, and output shaping.
- [x] Operators, truthiness, and conversions.
- [x] Conditions, loops, switch, try/catch, throw, break, and continue.
- [x] Arrays, collections, dictionaries, and bounded object construction.
- [x] Member access and method invocation.
- [x] Local calls, call graphs, and supported recursion.
- [x] Runtime-state intrinsics and `ShouldProcess` state.
- [x] Existing streams, command regions, and typed captures.
- [x] Move executable parameter binding, common-parameter, default, and invocation contracts out of AST-aware emission into bound/lowered artifact models.

Exit gate:

- all existing compiler tests exercise the IR path without a structural or AST-aware semantic veto;
- current generic corpus behavior is preserved;
- direct AST-to-C# semantic and artifact-shaping paths are deleted;
- no compatibility switch silently routes new features through the legacy emitter.

Broad coverage work remains frozen until this gate passes.

## Milestone 6 — Define runtime-free artifact contract and managed ABI

- [x] Name and version the semantic profile used by current runtime-free Strict artifacts.
- [x] Define the compiler-runtime ABI and record it in generated projects and manifests.
- [x] Create a shared AOT-safe runtime owner only when real emitted semantic helpers exist; current manifests explicitly record that no runtime substrate is present.
- [x] Enable trim and AOT analyzers with a fail-closed warning policy that does not depend on a manually enumerated subset of current `IL*` warning codes.
- [x] Mechanically inspect supported managed Strict artifact formats and dependency manifests; generated-source token scans remain defense-in-depth rather than the proof authority.
- [ ] Fail Strict publication when delivered closure verification returns limitations or `Verified = false`, including opaque NativeAOT output and unverified native runtime dependencies.
- [x] Define deterministic PowerShell-command-to-CLR symbol mapping.
- [x] Define the callable CLR signature and PowerShell binding contract, including compiler-added parameters, parameter sets, positions, switches, remaining arguments, defaults, streams, and exceptions.
- [ ] Carry bound value state, output cardinality/scalarization, collection element shape, and null/no-output semantics into the public ABI instead of inferring them from CLR type-name spelling; close this with Milestone 11.
- [x] Emit a normalized public ABI manifest and hash that change whenever the generated callable or binding contract changes.
- [x] Add direct C# consumer tests that reference the produced DLL as a normal assembly.
- [x] Add an independently rebuilt generated-project test using only recorded inputs.

Exit gate:

- two or more Strict backends consume the same versioned runtime-free primitives when such helpers exist, or the manifest explicitly records that the artifact has no runtime substrate dependency;
- a generated CLR library can be referenced from a clean C# consumer with a documented stable signature;
- NativeAOT analysis reports no compiler-owned trim/AOT warnings under a fail-closed warning policy;
- an uncertifiable Strict artifact cannot be returned as a successful certified build;
- inspection of every supported delivered artifact format, not only generated source or managed metadata, proves the absence of a PowerShell runtime and source fallback.

## Milestone 7 — Preserve help and module contracts

- [x] Bind comment-based help as function metadata.
- [x] Reuse the existing documentation engine to generate external MAML for compiled cmdlets.
- [x] Preserve synopsis, description, parameter help, examples, notes, links, inputs, and outputs.
- [x] Preserve aliases, exports, and mixed Hybrid command identity.
- [x] Verify `Get-Help` for typed and retained commands in the same module.
- [x] Refresh the product-neutral baseline and wider pinned census.

Exit gate: compiling a function does not remove or degrade its help or exported command contract.

This is the first new module-surface feature after migration because it proves that source metadata can flow through the IR, lowering, generated module, and artifact validation paths. It also removes the current help-preservation barrier from real-module coverage measurements.

Implemented evidence audited on 2026-08-27:

- `dotnet test .\PowerForge.Tests\PowerForge.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PowerShellCompilation"` passed 740/740 tests at exact head `8fc558c0739c6e9ec81fe5bfcbc4a44849ac0936`, including generated artifacts, differential behavior, help/MAML, statement-level source maps, callable ABI, dependency-graph snapshots, lifecycle, trimming, NativeAOT build contracts, and delivered-artifact inspection;
- `PowerForge.PowerShell.csproj` built with zero warnings and errors for `net472`, `net8.0`, and `net10.0`;
- the portable generic Hybrid corpus remains at 5/6 emitted functions (83.33%) with zero eligible functions dropped after graph or binary-cmdlet shaping; its intentional runtime-scope function remains fallback;
- the refreshed exact-pinned six-product lane reports 123/1,235 emitted functions (9.96%), 21 analyzer-eligible functions routed to fallback, 1,263 authored files, 1,353 units, and zero parse errors;
- per-product emitted/total coverage is PowerInfoBlox 3/57, PSSharedGoods 12/281, PSWriteHTML 31/238, O365Essentials 62/282, ADEssentials 12/247, and PSWriteWord 3/130;
- semantic lowering is the eligibility authority; structural diagnostics are mapped from canonical semantic features and cannot independently veto a successfully lowered unit;
- semantic IR collections take immutable snapshots, document identities are relocation-stable and filesystem-case-aware, loop variables retain PowerShell function scope, and generated literals cover control characters plus non-finite floating-point values;
- typed targets reject native-process effects before emission, while strict dependency verification rejects managed `System.Diagnostics.Process.Start` references before runtime-free certification;
- the former 139/1,235 result is retained in the companion guide as a historical snapshot of the deleted direct AST emitter, not represented as current all-IR coverage;
- the lowered C# method backend has no reference to `System.Management.Automation.Language`; the former direct AST-to-C# emitter files, transpiler graph reconstruction, and AST-aware Strict executable shaping are absent;
- deterministic graph snapshots drive current analysis/build planning and manifest schema 4; they classify discovered module, managed, native, process, and literal `New-Object -ComObject` boundaries without importing or executing source, but the build does not yet consume an externally reviewed lock or reject analysis-to-build drift;
- PowerShell 7 Hybrid advanced-function fixtures execute `begin`, per-record `process`, `end`, and `clean` through a hosted `SteppablePipeline`, and Strict rejects that hosted-only lifecycle explicitly; original pipeline-record identity/property fidelity, begin-failure cleanup, and PowerShell 5.1/`clean` capability behavior remain open;
- the repository-wide 800-line command still reports pre-existing non-compiler owners, while every touched non-generated compiler production/test file is below 800 lines after semantic, artifact, dependency-resource, lowering-context, and test-contract decomposition.

Green tests prove the integrated branch candidate and the tested artifact profiles. They do not convert an unverified closure result into certification and do not substitute for lock-consumption drift tests, lifecycle differential cases, the clean-target interop fixture matrix, NativeAOT target-host execution, default-branch integration, package publication, or release proof.

## Architecture-closure checkpoint — Partial / Current

The original semantic/back-end consolidation closed, but exact-head artifact and lifecycle review reopened the product-level checkpoint:

- [x] Integrate `origin/main`, resolve the 18-commit audit-snapshot gap, and rerun compiler tests, multi-TFM builds, corpus/census, generated-consumer, and artifact proof on the resulting head.
- [x] Make the bound/analyzed/lowered semantic result the sole eligibility authority; replace retained structural semantic vetoes with diagnostics produced or mapped from canonical semantic owners.
- [x] Remove `CommandAst` call-graph reconstruction from the transpiler and consume the semantic graph directly.
- [x] Replace AST-aware Strict executable parameter and invocation shaping with explicit bound/lowered executable contracts.
- [x] Upgrade source maps from method start lines to generated ranges and source line/column spans suitable for diagnostic remapping.
- [x] Make the public ABI manifest reflect the exact callable CLR signature and PowerShell binding contract, including compiler-added parameters.
- [ ] Complete ABI value-state, semantic nullability, and output-cardinality contracts with Milestone 11.
- [x] Inspect supported managed delivered-artifact formats; source token scanning remains a diagnostic defense, not the proof authority.
- [ ] Fail Strict publication for formats or dependencies the closure verifier cannot certify.
- [x] Make trim/AOT warning enforcement fail closed, and keep actual target-host NativeAOT execution in Milestone 14.
- [x] Decide explicitly that current emitted code does not justify `PowerForge.CompiledRuntime`; manifests record the versioned artifact contract and no runtime substrate dependency.
- [x] Split `PowerShellSemanticAnalyzer` and `PowerShellSemanticBinder` by named semantic responsibility before either grows further.
- [x] Split `PowerShellCompilationBoundPipelineTests.cs` by behavioral contract and extract artifact publication/manifest/closure responsibilities from `PowerShellCompilationArtifactBuilder.cs`.
- [x] Apply the existing line-count tooling as a scoped growth gate: no touched non-generated compiler production/test file exceeds 800 lines.
- [ ] Split the analyzer, binder, lowered backend, lowerer, and principal bound-pipeline test owners again by semantic family before M11–M13 growth; the current 761–792-line owners pass the mechanical gate but have little headroom.
- [ ] Make build consume and validate one reviewed dependency lock instead of recomputing current filesystem state.
- [ ] Move hosted lifecycle binding onto the canonical front-end/IR boundary and close raw-input, cleanup, and target-version behavior.

Exit gate: the integrated candidate has one eligibility, consumed graph lock, executable contract, semantic ABI, source-map, lifecycle, and fail-closed closure authority; focused proof remains green; and active semantic owners have enough responsibility-based headroom for the next milestone rather than merely remaining below the 1,000-line ceiling.

## Milestone 8 — Build command and pipeline semantics

- [x] Canonical deterministic resolution for commands, module-qualified names, and aliases registered in the current snapshot.
- [ ] Define deterministic external provider registration/injection without editing the built-in singleton, including duplicate and ambiguous ownership rules across providers.
- [x] Deterministic command-semantic registry.
- [x] Duplicate and ambiguous registration validation.
- [x] Versioned command-family/provider contract whose resolvers are compile-time-only, deterministic, capability-declared, and forbidden from importing or executing source modules.
- [x] Public diagnostics and census features mapped from the registry/binder result rather than matching command names in a parallel structural analyzer.
- [x] Initial hosted stream contracts for `Write-Verbose`, `Write-Debug`, and `Write-Warning`.
- [ ] Complete stream ownership for success output, information, error, and the remaining documented stream behaviors.
- [x] Initial hosted provider contracts for projection, filtering, mapping/enumeration, and sorting command families.
- [ ] Bind typed family-specific projection/filter/map/sort nodes and carry their value, cardinality, stream, error, and capability contracts through lowering.
- [x] General bounded-command-region binder.
- [x] Runtime-free command-adapter contract with semantic-profile, dependency, and AOT capabilities.
- [ ] Implement and prove at least one injected runtime-free adapter, then use that route for future managed-wrapper/AD-style adapters rather than adding product checks.
- [x] Provider metadata for command output, cardinality, stream, and error contracts.
- [ ] Typed pipeline-stage composition that does not carry executable PowerShell source strings as the semantic payload.
- [x] Explicit pipeline symbols for `$_` and `$PSItem`.

Exit gate: adding a supported `Select-Object` shape does not require coordinated semantic edits to analyzer, transpiler, emitter, Hybrid composer, and census.

## Milestone 9 — Resolve modules, dependencies, and interop

- [x] Replace the flat dependency inventory as the planning authority with semantic, dependency, and deployment graphs that share stable node identities.
- [x] Discover the currently modeled static edges from manifests, `using`, `#requires`, literal `Import-Module`, dot-sources, CLR references, native loads, and explicit build inputs.
- [ ] Parse and bind exact module specifications, including required/minimum/maximum version and GUID, without relying on comma-split or regex-only identity discovery.
- [x] Traverse contained `NestedModules`, `RequiredAssemblies`, type/format data, runtime assets, and module initialization hooks.
- [ ] Resolve acquired transitive `RequiredModules` and their dependency closure without importing or executing module initialization; unresolved external modules remain explicit environment requirements.
- [x] Emit a deterministic graph snapshot with the currently known identity, hash, source, edition, TFM, RID, architecture, and provenance fields.
- [ ] Add a first-class expected dependency lock to the build request, consume it during build, verify source/dependency hashes, and fail on drift or a different resolution.
- [x] Keep explicit restore/acquisition separate from read-only resolution and analysis.
- [x] Classify script, binary, mixed, CDXML/CIM, implicit-remoting/dynamic-proxy, managed-library, native, and external-process dependencies.
- [x] Read binary module and managed assembly metadata without importing or executing module initialization in the compiler process.
- [x] Model module load order, version conflicts, cycles, assembly unification/load context, native assets, and external target requirements.
- [x] Assign one artifact disposition to every dependency: compiled, referenced, hosted, bundled, private-restored, externally required, or rejected.
- [x] Discover literal `New-Object -ComObject` activation as a hosted/rejected capability.
- [ ] Cover equivalent COM activation forms such as `Type.GetTypeFromProgID`, CLSID activation, apartment requirements, and typed adapter ownership before claiming complete COM disposition.
- [ ] Add a representative external binary-module/Active Directory-style Hybrid contract with typed work before and after a hosted command region.
- [ ] Add a managed-wrapper fixture proving direct CLR, hosted cmdlet, and explicit generated-adapter paths remain distinct.
- [ ] Add native-library and external-process fixtures with RID, error, cancellation, and cleanup proof.
- [ ] Add a Windows COM fixture proving Package/Hybrid hosting and precise Strict rejection before typed COM support exists.
- [x] Record redistribution, publisher/signature, servicing, and license constraints separately from technical dependency resolution.

Exit gate:

- the build consumes the same reviewed dependency lock used for analysis, manifest evidence, and clean-target validation, and rejects post-analysis drift;
- a Hybrid artifact can preserve a required external module and compile safe surrounding regions without pretending the module became native;
- a managed-wrapper artifact contains or requires its complete transitive assembly/native closure;
- missing, ambiguous, incompatible, cyclic, or non-redistributable dependencies fail or remain external exactly as planned;
- COM and native capability requirements are visible in diagnostics and the artifact manifest.

## Milestone 10 — Complete advanced-function lifecycle

- [x] PowerShell 7 Hybrid hosted execution for `begin`, per-record `process`, `end`, and ordinary-path `clean`.
- [ ] Bind lifecycle metadata and source into the canonical front-end/IR result instead of reparsing and appending an AST-derived method after typed compilation.
- [ ] Guarantee `clean`/disposal on begin failure, process/end failure, stop, and early termination.
- [x] Basic `ValueFromPipeline` binding through a generated cmdlet.
- [x] Basic `ValueFromPipelineByPropertyName` binding through a generated cmdlet.
- [ ] Preserve the original pipeline record for `$_`/`$PSItem`, object identity, adapted members, and unbound properties instead of reconstructing input from bound parameter values.
- [x] `ValueFromRemainingArguments` behavior.
- [x] common parameters.
- [x] `ShouldProcess` and `ConfirmImpact`.
- [x] per-record state and output.
- [x] terminating and nonterminating errors.
- [x] stream and progress lifecycle.
- [ ] Define and test the target-version capability matrix: PowerShell 5.1/net472 lifecycle where supported, PowerShell 7 lifecycle, and `clean` as a PowerShell 7.3+ capability that is lowered or rejected explicitly on older hosts.
- [ ] Add differential fixtures for original pipeline-record identity/properties, begin failure, stop/termination, and disposal after every lifecycle phase.

Exit gate: representative conventional advanced functions execute as generated cmdlets with PowerShell-equivalent invocation and lifecycle behavior.

## Milestone 11 — Complete value and object flows

- [ ] `$null`, missing, `AutomationNull.Value`, and no-output distinctions.
- [ ] scalarization and enumeration.
- [ ] array and collection concatenation.
- [ ] `IDictionary` and ordered dictionaries.
- [ ] `IList` and `ArrayList` flows.
- [ ] `PSCustomObject` construction and known property shapes.
- [ ] bounded `PSObject.Properties` access.
- [ ] bounded `Add-Member` note properties.
- [ ] member reads and writes.
- [ ] indexing and mutation.
- [ ] adapted-object fallback boundaries.

Exit gate: common object-shaping helpers compile without pretending arbitrary ETS behavior is statically known.

## Milestone 12 — Expand bounded runtime state

- [ ] `$PSScriptRoot` and `$PSCommandPath` in supported artifact contexts.
- [ ] read-only script/module constants.
- [ ] bounded script-scope tables and caches.
- [ ] `$Error` where its lifecycle can be represented.
- [ ] supported preference variables.
- [ ] read-only environment snapshots.
- [ ] closures with statically proven captures.
- [ ] explicit mutation and lifetime boundaries.

Arbitrary global state, variable-provider escapes, uninspectable closures, and dynamic scope remain runtime-backed.

Exit gate: supported runtime state is represented in the IR and propagated through call graphs without special emitter checks.

## Milestone 13 — Run generic coverage waves

Coverage work resumes through semantic families in this order, adjusted by each fresh census:

1. [ ] comment-based help preservation;
2. [ ] parameter types and defaults;
3. [ ] common stream operations;
4. [ ] subexpressions and expandable strings;
5. [ ] function graph inference and statically known splatting;
6. [ ] object, dictionary, and collection flows;
7. [ ] pipeline lifecycle;
8. [ ] `Select-Object`, `Where-Object`, `ForEach-Object`, and `Sort-Object`;
9. [ ] bounded runtime state and scope;
10. [ ] remaining operators, conversions, and CLR interop.

Each wave must:

- improve a generic semantic contract;
- add PowerShell differential evidence;
- prove Strict rejection and Hybrid fallback boundaries;
- rerun the product-neutral corpus and wider pinned census;
- show benefits across unrelated inputs without product-specific branches.

Function percentage is not the only metric. Track:

- fully typed functions;
- Hybrid functions with typed regions;
- emitted versus analyzer-eligible functions;
- typed statement or region coverage;
- fallback feature families;
- dependency-closure success;
- runtime differential pass rate;
- artifact size and performance for meaningful workloads.

## Milestone 14 — Productize managed, Hybrid, and native delivery

- [ ] Add the explicit target-contract model to engine requests, CLI/cmdlet input, generated projects, and manifests.
- [ ] Keep existing framework-dependent, self-contained, trimmed, and NativeAOT Strict outputs on one generated C# backend.
- [ ] Add a ReadyToRun benchmark lane without promoting it to a public mode until evidence justifies it.
- [ ] Complete Hybrid EXE using the same bound plan, typed assembly, and fallback contract as Hybrid modules.
- [ ] Measure and expose typed/fallback boundary crossings so the tool can warn when Hybrid compilation is unlikely to help.
- [ ] Pin or record SDK, compiler-runtime, package, and native dependency identity for reproducible release builds.
- [ ] Add a content-addressed incremental build cache keyed by normalized source identity, compiler and semantic-profile versions, target contract, dependency lock, TFM/RID, and relevant toolchain inputs; reject incomplete or cross-target cache hits.
- [ ] Emit source, PDB/symbol, public ABI, dependency, SBOM/provenance, and artifact-integrity evidence as applicable.
- [ ] Run Strict managed and NativeAOT artifacts on every named supported RID rather than treating cross-publish success as execution proof.
- [ ] Verify Windows and Unix exit codes, stdout/stderr encoding, signals/cancellation, file permissions, resources, and native dependencies.
- [ ] Preserve signing and atomic publication in PowerForge’s shared packaging owner.
- [ ] Document supported versus experimental TFMs/RIDs and the installed runtime requirements for every artifact profile.
- [ ] Keep native shared-library export deferred until a concrete embedding consumer defines its ABI.

Exit gate:

- a user can analyze, explain, emit source, build, and test one explicit target contract without learning compiler internals;
- generated CLR libraries are consumed from clean C# projects, Strict EXEs run without PowerShell, and named NativeAOT RIDs run on their target hosts;
- Hybrid EXEs report the bundled runtime, embedded source, typed coverage, fallback closure, and crossing cost truthfully;
- manifests and release evidence distinguish source, managed artifact, native artifact, signing, and publication state.

## Milestone 15 — Optimize proven IR

- [ ] constant folding.
- [ ] dead-branch elimination.
- [ ] allocation reduction.
- [ ] pipeline-stage fusion.
- [ ] command-region coalescing.
- [ ] specialized collection loops.
- [ ] cached conversion plans.
- [ ] improved generated source and PDB mapping.

Exit gate: optimizations preserve differential and artifact contracts and show meaningful workload improvements. Small host-dominated workloads are not used to justify compiler-wide complexity.

## Validation and performance promotion matrix

Compiler coverage, semantic fidelity, artifact delivery, and performance are separate proof lanes. A green result in one does not substitute for another.

| Lane | Compared paths | Required measurements | Promotion gate |
| --- | --- | --- | --- |
| Semantic differential | selected PowerShell host versus Strict/Hybrid artifact | output values and types, property/order/cardinality, all streams, errors, exit code, filesystem effects | Zero unexplained differences for every accepted case; unsupported cases reject or fall back exactly as declared |
| Pure typed compute | `pwsh -File`, Package, Hybrid region, Strict direct CLR, equivalent generated/hand C# | elapsed time, throughput, allocations, peak working set | Initial native-like target: Strict direct CLR no worse than 1.5x equivalent hand C# and at least 3x faster than PowerShell on sufficiently large eligible kernels |
| One-shot startup | `pwsh -File`, Package, Strict framework-dependent, self-contained, trimmed, ReadyToRun experiment, NativeAOT | cold p50/p95, warm time, working set, primary and total artifact size | NativeAOT must beat PowerShell and Package startup; claim a speed advantage over managed only when it is measured on that RID |
| Hosted dispatch | original function, fine generated cmdlets, coarse cmdlet/typed region, Hybrid boundary | calls per second, crossing cost, allocations | Promote a Hybrid region only when representative work amortizes the boundary; tiny cmdlets may remain script |
| Pipeline/object flow | interpreted and compiled high-volume transforms | elapsed time, allocations, retained memory, cardinality, ordering | Semantic parity first; optimization must reduce a measured cost without materializing duplicate pipelines |
| Build and artifact | clean rebuilds for each TFM/RID/profile | analysis, generation, restore/build/publish time, deterministic hashes, warnings, files/bytes | Same recorded inputs produce equivalent plans, source, ABI, and artifact set; AOT/trim warnings are zero |
| Module/dependency closure | script, binary, mixed, and external required-module fixtures | exact graph, lock identity, load order, artifact disposition, clean-target import/execution | No ambient-module substitution; every transitive dependency is present, explicitly external, or rejected |
| Managed/native/COM interop | DLL wrapper, native/process adapter, and Windows COM fixture | TFM/RID/bitness, marshalling, lifetime, errors, cancellation, apartment/thread, deployment | The target-host artifact executes the declared contract; unsupported targets fail before publication |
| Platform | Windows, Linux, macOS and named x64/Arm64 RIDs | build plus execution, signals, encoding, permissions, native dependencies | A RID is supported only after execution proof on that target; cross-publish alone remains experimental |

The 1.5x/3x compute figures are initial engineering promotion targets, not universal marketing promises. They apply to pure typed kernels large enough to dominate process and command-dispatch startup. If a semantic helper prevents the hand-C# target, retain the behavior, record the cost, and optimize the shared primitive before widening the language surface. Do not weaken semantics to hit a benchmark.

Each benchmark result records:

- exact source revision and dirty-state check;
- compiler/runtime/SDK versions, TFM, RID, OS, architecture, and CPU;
- input size, warmup policy, sample count, run ID, and validation result;
- median and tail latency where startup or services matter;
- allocations, peak/retained memory, and artifact-set bytes where applicable;
- generated source/ABI identity and whether execution was direct CLR, PowerShell hosted, or native.

Use BenchmarkDotNet for in-process typed kernels and a process harness for cold startup and artifact execution. Benchmark setup, validation, and artifact generation stay outside timed operations. Clean publishable runs use full jobs; dry jobs prove only discovery/setup.

## Differential and adversarial proof

The semantic corpus should combine:

- focused positive cases for each accepted contract;
- negative cases proving precise Strict rejection and Hybrid fallback;
- differential cases across PowerShell 5.1 and supported PowerShell 7 profiles where behavior is intended to match;
- property-based generation within the supported grammar for operators, conversions, collections, binding, and control flow;
- metamorphic cases that vary declaration order, input-file order, whitespace, line endings, path form, culture, and casing without changing the intended result;
- malformed, oversized, cyclic, deeply nested, and adversarial input for parser, graph, dependency, and diagnostic robustness;
- real artifact execution from a clean consumer or target host.

The compiler should fuzz parsing and binding without executing source. Crashes, hangs, nondeterministic plans, source-root escapes, unbounded diagnostic growth, and accidental acceptance are defects even when the input would not be a valid Strict program.

## Feature extension checklist

Every new language feature should have:

- [ ] a written supported semantic contract;
- [ ] an explicit rejection or fallback boundary;
- [ ] one canonical binder or semantic owner;
- [ ] bound type, value-state, cardinality, effect, and capability information;
- [ ] lowering that does not inspect AST;
- [ ] backend rendering only when existing lowered nodes are insufficient;
- [ ] stable diagnostics and census feature identifiers;
- [ ] PowerShell 5.1/7 differential coverage where applicable;
- [ ] Strict rejection and Hybrid fallback coverage;
- [ ] generated artifact execution;
- [ ] source-map evidence;
- [ ] runtime-free dependency proof when admitted to Strict;
- [ ] trim/NativeAOT analysis when used by a native backend;
- [ ] public ABI impact classification when visible from a generated DLL;
- [ ] module, assembly, native, resource, and deployment-graph impact classification;
- [ ] a boundary contract for any value/state crossing into hosted module, native, process, or COM execution;
- [ ] a fresh corpus/census result.

Every new command family should normally require:

- [ ] one canonical command registration;
- [ ] one command-family binder;
- [ ] existing bound pipeline or command nodes where possible;
- [ ] an explicit unsupported-shape result rather than handler fall-through;
- [ ] command, pipeline, stream, cardinality, and error differential tests;
- [ ] required-module identity/version, host/runtime capability, and artifact disposition;
- [ ] explicit runtime-free adapter or hosted/fallback classification;
- [ ] no new command-name conditionals in an emitter.

## Maintainability gates

- [x] Prefer 100–400 lines per semantic owner.
- [x] Review files approaching 600–700 lines before adding another responsibility.
- [x] Treat 800 lines as the preferred split point for new or actively growing compiler files.
- [x] Keep 1,000 lines as the hard ceiling for touched non-generated compiler production and test files.
- [x] Keep the existing 800-line repository gate where it already applies; do not loosen it merely because 1,000 is the absolute ceiling.
- [x] Use the existing line-count tooling for any scoped 1,000-line production/test hard check not already covered by the stricter gate; do not add another policy engine.
- [x] Split by semantic responsibility, never arbitrary line ranges.
- [x] Permit a central exhaustive node-dispatch switch only when it delegates immediately.
- [x] Do not use partial classes to hide unrelated responsibilities.
- [x] A partial type may span files only when each file has one named responsibility; splitting one 3,000-line semantic owner into three arbitrary partials does not pass the gate.
- [x] Generated code, machine-maintained tables, schemas, and native templates may exceed 1,000 lines only when their source of truth and regeneration path are explicit.
- [x] Keep substantial generated PowerShell and C# templates in native template/resource files.
- [x] Add XML documentation to public and non-obvious reusable contracts.
- [x] Keep tests grouped by behavioral contract rather than implementation class count.
- [ ] Before Milestones 11–13, decompose the active analyzer, binder, lowered backend, lowerer, and large bound-pipeline test owners that are already in the 761–792-line range; passing the 800-line gate is a floor, not growth headroom.

## Architecture completion gate

The redesign is complete only when all of the following are true:

- [x] no backend consumes PowerShell AST;
- [x] no emitter performs semantic type or effect inference;
- [x] no census code independently decides compiler eligibility;
- [x] no command behavior depends on registration order;
- [x] analysis does not execute/import source modules or activate native/COM dependencies to discover semantics;
- [ ] all artifact behavior, including hosted lifecycle discovery and binding, runs through the canonical front-end/IR boundary;
- [x] migrated direct AST-to-C# paths are deleted;
- [x] the focused 740-test compiler suite passes on the integrated branch candidate;
- [x] established applicable PowerShell 5.1 and PowerShell 7 differential lanes in that suite pass;
- [ ] hosted lifecycle has an explicit PowerShell 5.1/7 version-capability matrix and differential proof;
- [x] net472, net8.0, and net10.0 builds remain warning-free on the integrated branch candidate;
- [ ] each artifact records one explicit semantic/execution/deployment target contract from Milestone 14 rather than inferring it from mode/kind/TFM fields;
- [ ] Strict publication fails when delivered dependency closure cannot mechanically exclude PowerShell runtime and source fallback;
- [x] every emitted runtime-free helper has one versioned owner and is trim/NativeAOT clean, or the artifact explicitly records that no support substrate is present;
- [ ] generated CLR libraries carry a normalized public ABI map with bound null/value/cardinality semantics and pass clean-consumer tests;
- [ ] semantic, dependency, and deployment graphs share stable identities and one reviewed lock that the build consumes and verifies;
- [ ] every required module, assembly, native asset, resource, process, and equivalent COM activation capability has one explicit artifact disposition;
- [ ] Hybrid/fallback diagnostics retain the causal function-command-module-dependency chain and boundary contract;
- [ ] representative external binary-module, managed-wrapper, native/process, and Windows COM artifacts pass clean-target validation;
- [ ] named NativeAOT RIDs have target-host execution proof;
- [ ] generated source, ABI, dependencies, and build inputs have provenance bound to the consumed lock and explicit target contract;
- [ ] generated artifacts preserve invocation, export, help, source-map, fallback, original pipeline-input, and lifecycle-cleanup contracts;
- [x] touched non-generated compiler production/test files stay below 800 lines;
- [ ] active compiler owners have responsibility-based headroom for planned value/object/command expansion, not only sub-800 line counts;
- [ ] adding an operator, syntax form, or command family has one obvious canonical semantic/lowering/backend owner and an injectable provider route where appropriate.

Only after this gate should PowerForge treat broad percentage growth as the primary objective.

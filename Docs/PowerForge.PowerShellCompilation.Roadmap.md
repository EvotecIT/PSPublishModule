# PowerShell Compilation Architecture Roadmap

Last updated: 2026-08-30

This roadmap is the execution plan for growing PowerForge PowerShell compilation without turning the analyzer, transpiler, command handling, or C# emitter into increasingly coupled catch-all components.

The branch contains a typed semantic compiler core with immutable bound IR, deterministic analysis and lowering, a lowered-only C# method backend, generated-project publication, help/module-contract flow, consumed dependency locks, typed command-family contracts, runtime-free provider injection, and canonical hosted lifecycle binding. The final 2026-08-29 closure also makes compiler-selected payloads authoritative through delivery, authenticates reusable checkpoints against the compiler and full normalized release plan, preserves post-sign evidence, and emits stable decision and reproduction evidence. The roadmap below records that architecture checkpoint as complete while keeping broader language, delivery, ecosystem, and operational work honest.

The companion [PowerShell Compilation guide](PowerForge.PowerShellCompilation.md) documents current behavior, artifact modes, supported syntax, measured performance, census evidence, and distribution limits. This file records completed architecture gates as well as the remaining implementation plan.

This roadmap does not schedule a package, gallery, NuGet, or GitHub release. Those remain separate decisions after source work is complete.

## Product north star

PowerForge should offer four honest outcomes from one compiler front end:

1. **Package** arbitrary compatible source with a PowerShell runtime when semantic compilation is not required.
2. **Hybrid** compile proven regions and retain explicit PowerShell-backed behavior for the rest.
3. **Strict managed** produce a runtime-free CLR DLL or EXE whose accepted behavior lowers to ordinary typed .NET code.
4. **Strict native** publish the same runtime-free program with NativeAOT for a specific operating system and architecture.

“Python territory” means reaching the same practical product territory available to a mature scripting ecosystem: keep a source-first authoring experience, package broad dynamic programs, accelerate eligible hot regions, expose typed library APIs, and produce self-contained native executables when the complete program fits a stricter contract. It does not mean claiming that arbitrary dynamic PowerShell can become native code without a PowerShell runtime.

Compiler breadth alone is not enough. Practical maturity also requires a versioned semantic compatibility profile, a reproducible project/lock/restore experience, platform-qualified package variants, a public provider ecosystem, clean-environment execution, and profiling that tells users where compilation helps. Milestones 18–22 make those product expectations explicit rather than treating one emitted-function percentage as the destination.

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

The current `feature/powershell-compilation-roadmap` candidate contains `origin/main` and was re-proven on 2026-08-30 after the semantic-pipeline migration, exact-closure remediation, and Milestone 14–22 implementation waves. The results below prove a branch candidate, not a default-branch merge, published package, or released product.

The compiler already provides:

- [x] Package and Strict EXE paths plus Strict/Hybrid binary-module and CLR-library paths
- [x] Strict executable build paths for framework-dependent, self-contained, trimmed, and NativeAOT publication, with fail-closed trim/AOT warning policy
- [x] Fail Strict publication when the delivered artifact format or runtime dependency cannot be certified, including exact PE/ELF/Mach-O architecture, import, native ABI, hash, and delivered-closure evidence
- [x] durable generated C# project emission for inspection and independent rebuilds
- [x] post-emission typed/fallback coverage, source-fingerprint baselines, and statement/range-level generated source mapping
- [x] capability-aware parameter contracts and supported validation metadata
- [x] omitted-versus-explicitly-bound literal defaults
- [x] typed local function graphs, conversions, operators, and control flow
- [x] bounded runtime-state intrinsics
- [x] typed code around bounded PowerShell command regions
- [x] deterministic semantic/dependency/deployment graph snapshots covering known manifest and static-source edges, managed/native assets, processes, literal COM activation, runtime content, policy, and artifact disposition
- [x] consume one reviewed dependency lock during build, reject source/dependency drift, and resolve exact local/acquired transitive module identity without importing or executing source; unresolved external modules remain explicit target requirements
- [x] a product-neutral acceptance corpus and replaceable real-module census inputs
- [x] PowerShell 5.1 and supported PowerShell 7 differential coverage, including hosted lifecycle raw-input, cleanup, and explicit `clean` version behavior
- [x] net472, net8.0, and net10.0 compiler build lanes
- [x] an explicit integrity-bound target contract in engine, CLI/cmdlet, generated source, manifests, provenance, and SBOM sidecars
- [x] a verified content-addressed build cache that rejects incomplete, modified, malformed, cross-target, and reparse-root/ancestor entries
- [x] a managed Hybrid executable path that registers compiled cmdlets while retaining unsupported source and dependency units for hosted execution
- [x] three immutable IR rewrites, five measured backend-lowering/source-evidence optimizations, and correctly classified manifest evidence
- [x] runtime Hybrid boundary profiling with measured crossing cost, estimated overhead ratio, and a deterministic coarsening advisory
- [x] target-host certification for Strict `net10.0` framework-dependent and NativeAOT executables on `win-x64` and `linux-x64`, including encoding, resources, errors, cancellation, permissions, executable inspection, and exact dependency closure
- [x] generic public `Build-Module` configuration that converts an arbitrary compatible staged script module to Hybrid or Strict binary output without module-name rules or consumer-side compilation logic
- [x] one exact compiler-selected payload inventory reused by signing, archive creation, repository publication, and temporary installation, including nested resources and canonical compiler evidence
- [x] reusable compilation checkpoints bound to compiler/core bytes, the complete staged-source tree, the exact pre-compilation staging identity, dependency-lock content, target and compilation settings, and the full normalized non-secret transformation/release plan, with detached CMS authority from the configured signing identity and exact inventory validation
- [x] finalized compiler payload hashes revalidated after lifecycle hooks and before artifact, publication, and installation boundaries; machine-local checkpoint files never enter released payloads
- [x] one portable canonical finalizer applied after the last mutation in staging, packed ZIP, unpacked folder, repository package, and installed-module roots; signed archives additionally authenticate that final evidence with detached CMS
- [x] one immutable final unit-disposition ledger consumed by manifest counters, coverage, explain output, census, reproduction hashes, and boundary profiling; emitted CLR, exported cmdlet, retained source, hosted regions, semantic fallback, shaping fallback, omission, and rejection remain distinct and may overlap honestly
- [x] stable per-unit decision traces and redacted integrity-bound reproduction evidence covering source identities, compiler/toolchain, target, provider, dependency, generated-source, ABI, source-map, trace, and diagnostic hashes

The companion guide owns the exact current benchmark numbers and runtime proof. This roadmap must not copy those figures as timeless claims; every performance or platform promotion uses a fresh clean-worktree run pinned to a source revision, toolchain, target framework, runtime identifier, and benchmark run ID.

The lowered C# method backend does not reference SMA AST types, and the former direct AST-to-C# emitter was deleted. Eligibility, call graphs, executable binding, source mapping, command providers, dependency locks, and hosted lifecycle metadata now consume canonical semantic or lowered contracts.

The dependency-ordered semantic pipeline migration checkpoint and bounded Milestones 6 and 9–15 are **Complete** after the latest review defects were reproduced and fixed at their canonical owners. Milestone 14 closes for its deliberately narrow supported set and for the generic `Build-Module` delivery contract: Strict `net10.0` framework-dependent and NativeAOT executables on `win-x64` and `linux-x64` are supported, while other named RIDs and deployment profiles remain experimental rather than weakening that gate. Milestone 15 closes with three immutable IR rewrites, five backend-lowering/source-evidence optimization families, clean-candidate performance evidence, and measured Hybrid boundary cost. Milestone 17 is **Complete** with semantic fingerprints, portable failure mapping, optional semantic-only IR snapshots, deterministic diagnostic audit evidence, and explicit retention/redaction policy.

The canonical compiler pipeline, semantic-profile/oracle architecture, and bounded provider-extensibility architecture are **Complete** within their documented profiles. The separately built provider SDK supplies canonical NuGet/signer-aware package identity, reviewed lock/provenance binding, metadata-only compiler discovery, and a versioned executable adapter ABI that runs through Strict managed, Strict NativeAOT, Hybrid, and binary-module artifacts without loading provider assemblies into the compiler. Semantic profiles affect target identity, compilation, providers, caches, artifacts, diagnostics, and compatible requirements. The oracle integrity-binds observations to exact executable/runtime identities, directly records ordered child-process launch/exit events on every promoted Windows host, and fails promotion on unpinned hosts or unexplained differences. Milestones 19–22 retain the broader ecosystem, useful-coverage, public-release, and platform gates.

## Artifact ladder

The modes are different products with different guarantees. They must not be collapsed into a single “compiled” label.

| Artifact | Current state | Requires PowerShell/SMA at runtime | Native/managed result | Primary value |
| --- | --- | --- | --- | --- |
| Package EXE | Available | Yes, complete script | Managed host; optionally self-contained | Broad compatibility and delivery |
| Hybrid binary module | Available | Yes, only fallback and bounded hosted regions | Typed cmdlets plus retained script | Incremental acceleration inside PowerShell |
| Strict binary module | Available | Yes, as the cmdlet host; no script fallback | Managed DLL | Importable compiled PowerShell command surface |
| Strict CLR library | Candidate with clean-consumer proof, callable ABI v4, and fail-closed managed-closure certification | No for a certified artifact | Managed DLL | Direct use from C# and other CLR hosts |
| Strict managed EXE | Supported for framework-dependent `net10.0` `win-x64` and `linux-x64`; portable managed builds retain their existing support state and other named profiles remain experimental | No for a certified artifact | JIT-compiled managed executable | Small runtime-free CLI/application |
| Strict NativeAOT EXE | Supported for `net10.0` `win-x64` and `linux-x64`; other profiles remain experimental and fail closed when their closure cannot be certified | No for a certified artifact | RID-specific native executable | No installed PowerShell or .NET requirement, low startup/footprint potential |
| Hybrid EXE | Managed foundation available; named-RID/native-host promotion remains experimental | Yes, explicit retained entry/dependency source and bounded hosted fallback | Packaged managed host plus registered typed cmdlets | Broad script compatibility with coarse compiled acceleration |
| Native shared library | Deferred | No | Platform ABI such as `.dll`, `.so`, or `.dylib` | Add only for a real non-.NET embedding consumer |

A C# DLL means a managed CLR library with a documented .NET API. It is not a native shared library. Native shared-library export introduces a platform ABI, marshalling, lifetime, and error-contract product of its own and must not be implied by NativeAOT executable support.

## Explicit target contract

`Strict`, `Hybrid`, and an artifact kind are not enough to describe semantic compatibility. Every successful artifact now carries one normalized, integrity-bound target contract with these dimensions:

- PowerShell semantic profile, including the behavior family being targeted rather than only a target framework;
- execution model: `RuntimeFree`, `PowerShellHosted`, or `Mixed`;
- artifact model: executable, CLR library, or PowerShell binary module;
- deployment model: framework-dependent, self-contained, trimmed, ReadyToRun experiment, or NativeAOT;
- target framework and, when platform-specific, runtime identifier and architecture;
- culture, encoding, filesystem, and operating-system assumptions that are compile-time facts versus runtime inputs;
- allowed capabilities, including command regions, host streams, module hosting, managed/native dependencies, COM, filesystem/provider access, reflection, and dynamic invocation.

The modern runtime-free profile should default to the documented PowerShell 7-compatible contract already used by Strict execution. A `net472` binary module executes inside Windows PowerShell 5.1 and is validated against that host. Where 5.1 and 7 differ, PowerForge either selects one named profile, emits a target-specific lowering, or rejects the construct. It must not claim one artifact is simultaneously identical to incompatible behaviors.

`analyze`, `explain`, and `build` accept the same `--target-contract` file and normalize it before dependency planning. Stored schema-v1 and schema-v2 contracts are authenticated against their declared support level before the current support policy is applied and schema-v2 identity is recomputed, so policy promotion or demotion does not invalidate an authentic stored request. NativeAOT dependency planning binds the exact runtime-pack version declared by the selected SDK; an unrelated newer ambient pack cannot change the reviewed graph.

## PowerShell implementation as semantic evidence

PowerForge should study the official [PowerShell source](https://github.com/PowerShell/PowerShell), especially [`LanguagePrimitives.cs`](https://github.com/PowerShell/PowerShell/blob/master/src/System.Management.Automation/engine/LanguagePrimitives.cs), [`Compiler.cs`](https://github.com/PowerShell/PowerShell/blob/master/src/System.Management.Automation/engine/parser/Compiler.cs), the runtime [`Binders.cs`](https://github.com/PowerShell/PowerShell/blob/master/src/System.Management.Automation/engine/runtime/Binding/Binders.cs), and the upstream [language tests](https://github.com/PowerShell/PowerShell/tree/master/test/powershell/Language). These are valuable evidence for conversion, truthiness, operators, parameter binding, pipeline enumeration, errors, and version-specific edge cases.

They are not a second implementation owner and must not become a hidden runtime dependency. The published Windows PowerShell 3.0 language specification is useful historical context but explicitly does not describe current PowerShell behavior. For each semantic feature, PowerForge therefore uses this evidence order:

1. define the intended PowerForge behavior for one named semantic profile;
2. record the relevant official documentation, pinned upstream source commit, and upstream test provenance;
3. implement the rule once in the PowerForge binder/IR/runtime-free support owner;
4. run black-box differential tests on the exact PowerShell 5.1 or PowerShell 7 host profile;
5. record any intentional, version-specific divergence instead of silently following whichever upstream source happens to be newest.

PowerForge may adapt small MIT-licensed upstream test cases with attribution and license tracking. It should not port PowerShell's expression compiler wholesale, call non-public engine internals from Strict artifacts, or make generated code depend on an installed PowerShell implementation. Upstream source is a semantic oracle and provenance input; the selected-host differential result is the compatibility proof.

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

`[OutputType()]` is useful contract evidence but not proof by itself because ordinary PowerShell does not enforce it. Authored `[OutputType([void])]` is advisory metadata and does not suppress success output actually produced by a function body. It is not a local-call, lifecycle-output, or recursive value contract; body inference remains authoritative, and direct self-recursion still requires one verified non-void contract. The canonical metadata policy now separates an authored type name from its optional CLR semantic contract. Generated binary cmdlets may preserve one statically resolved, non-signature-compatible output type through the string-form `OutputTypeAttribute`, proven on net472 and net8; Strict runtime-free targets still require a target-compatible semantic type, and unresolved, dynamic, or multiple declarations fail closed. Validation attributes narrow accepted input but do not silently change the underlying type. Profile-guided observations and census data may prioritize work; they never specialize a Strict artifact around values seen during training.

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

## WMI, CIM, CDXML, and management-provider boundaries

Management commands are a capability family with remote state, transport, credentials, sessions, mutable effects, and provider-shaped objects. They must not be modeled as ordinary CLR method calls or a list of recognized command names.

- Windows PowerShell WMI v1 cmdlets are a named 5.1/Windows hosted profile. Hybrid can retain their exact behavior; PowerShell 7 and cross-platform targets do not silently receive a synthetic `Get-WmiObject` compatibility layer.
- CIM cmdlets and CDXML modules use a separate management-provider contract. Hybrid can host the locked module/session behavior; Strict requires an explicit CIM/MI adapter with supported operation and target profiles.
- The semantic owner binds generic management operations such as query/enumerate/get, create/modify/delete, invoke method, association traversal, and indication subscription. Command-family registrations map supported invocations onto those operations; emitters never rediscover the command semantics.
- CDXML is static metadata only when its class, namespace, methods, parameters, outputs, adapter, and session requirements can be parsed deterministically. Parsing CDXML does not contact a server, load a custom adapter, or prove runtime compatibility.
- A target contract records local/remote target, namespace/class/query, WS-Man/DCOM or other transport, authentication capability, timeout/throttle, platform, bitness, required modules/native assets, and whether output remains a hosted `CimInstance`/`PSObject` shape or crosses through a proven typed DTO.
- Credentials, live `CimSession` identities, server-discovered schemas, and mutable subscriptions are runtime state. Locks and diagnostics record required capability and redacted identity, never secrets or a promise that a previously observed session still exists.
- Session creation, pooling, reconnect, cancellation, partial enumeration, event subscription, target disconnect, and disposal have one provider-owned lifecycle. Hybrid and Strict surfaces consume that same lifecycle contract instead of implementing separate cleanup logic.

WMI may internally use COM/DCOM, but WMI/CIM operation semantics and general COM automation remain distinct provider contracts. A successful COM activation test does not prove WMI/CIM behavior, and a successful WMI query does not qualify arbitrary COM automation.

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

NativeAOT is currently proven for a narrow Strict executable subset. Promotion of any additional RID requires repeatable proof on that exact operating system and architecture. The named supported set is currently `win-x64` and `linux-x64`; macOS, Arm64, and other profiles remain experimental even when `dotnet publish` succeeds.

## Compiler user experience

The product surface should make the compiler’s decision inspectable before a potentially expensive build:

```text
powerforge powershell analyze <source> --target-contract <contract.json> --output json
powerforge powershell explain <source> --target-contract <contract.json>
powerforge powershell build   <source> --target-contract <contract.json> --dependency-lock <graph.json> --out <directory>
powerforge powershell build   <source> --target-contract <contract.json> --dependency-lock <graph.json> --out <directory> --emit-source
```

Those are the current public CLI commands. The required workflow is:

1. analyze without executing source;
2. show typed, hosted, fallback, and rejected regions with causal diagnostics;
3. optionally emit the deterministic C# project and source maps;
4. build one explicit artifact contract;
5. run differential tests and the checked-in target-host artifact harness;
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
| 6. Define runtime-free artifact contract and managed ABI | Complete | Strict publication is fail-closed and ABI v4 carries value state, output/null/cardinality, streams, and binding semantics |
| 7. Preserve help and module contracts | Complete | Compiled functions retain full help/export behavior |
| Semantic pipeline migration checkpoint | Complete | Publication certification, consumed locks, command-family lowering, lifecycle ownership, and canonical semantic ownership are closed; current file-growth work remains in the maintainability gate |
| Core semantic pipeline gate | Complete | Parser, binder, bound IR, lowering, backend, delivery, and explainability have canonical owners |
| Extensibility/profile architecture gate | Complete within promoted profiles and provider ABI 5 | Effective semantic profiles, exact-host oracles, authoritative package/signer policy, full bounded provider conformance, Strict-executable host ABI, NativeAOT adapter execution, one representative external filesystem operation, and bounded process-isolated provider initialization are implemented |
| 8. Build command and pipeline semantics | Complete | Deterministic injected providers, typed family stages, complete CLR stream sinks, and runtime-free adapter execution share one semantic route |
| 9. Resolve modules, dependencies, and interop | Complete / planning contract | Builds consume exact graph locks; transitive module, managed, native/process, and COM dispositions are deterministic, while executable clean-target ecosystem qualification continues in Milestone 19 |
| 10. Complete advanced-function lifecycle | Complete | Canonical lifecycle IR preserves raw records, guarantees cleanup, and enforces PowerShell 5.1/7/7.3+ behavior explicitly |
| 11. Complete value and object flows | Complete / bounded contract | Known object and collection shapes compile while arbitrary ETS identity remains an explicit fallback boundary |
| 12. Expand bounded runtime state | Complete / bounded contract | Read-only invocation state propagates through IR and call graphs; mutable dynamic scope remains runtime-backed |
| 13. Run generic coverage waves | Complete / bounded wave | The product-neutral corpus and caller-supplied replaceable census proved the generic prioritization mechanism; Milestone 20 now owns the fixed public packet and future expansion |
| 14. Productize managed, Hybrid, and native delivery | Complete / bounded supported profiles | `Build-Module` can convert arbitrary staged script modules into measured Hybrid or Strict binary modules; exact target contracts, evidence, verified caching, Hybrid EXE, native closure inspection, and target-host execution support Strict `net10.0` framework-dependent and NativeAOT EXEs on `win-x64` and `linux-x64`; all other named profiles remain experimental |
| 15. Optimize proven IR | Complete | Three immutable IR rewrites plus five backend-lowering/source-evidence optimization families, authored source/PDB mapping, measured boundary cost, and clean-candidate benchmark evidence preserve the differential and artifact contracts |
| 16. Productize the provider SDK and trust model | Complete within provider ABI 5 | Versioned providers are authoritatively identified, locked, and executable through one bounded route; typed results, streams, errors, cooperative and process-isolated cancellation, deterministic cleanup, dependency closure, NativeAOT, and one representative external filesystem operation are observed |
| 17. Add compiler explainability and reproducible diagnostics | Complete | Human and versioned JSON explain output, semantic fingerprints, integrity-bound reproduction evidence, source/boundary failure mapping, optional semantic-only IR, auditable cache/graph/ABI/fallback/provider decisions, and local-only retention/redaction policy are implemented |
| 18. Establish versioned semantic oracles | Complete within the promoted Windows profiles | Named profiles govern target identity and compilation; observations carry integrity-bound executable/runtime identity, promotion rejects missing pins or unexplained differences, all 31 interpreted cases execute across all three host profiles and the 24-case promoted runtime-free subset has matching Strict results, explicit `AutomationNull` plus framed nullable/string/culture observation are implemented, and a deadline-bound Job Object boundary reconciles completion packets with cumulative process accounting before recording ordered direct-child launches/exits separately from final `LASTEXITCODE` state |
| 19. Qualify real dependency and interop ecosystems | Partial / one authorized external gate | Signed binary wrappers, the complete typed CIM/MI and LDAP families, bounded process-isolated LDAP initialization, locked native delivery, remote execution, session reuse, bounded results, authentication failure, mutation, and safe failure/cleanup cases are proven; only the controlled target-reboot/reconnect exercise remains open |
| 20. Establish generic measurement and expand useful coverage | Complete / bounded benefit packet | Public and external baselines are enforced, four emitted commands run with parity across three scenario families, and the realistic Strict application proves dependency, resource, streams, bounded errors, and counterbalanced fresh-process startup on Windows/Linux |
| 21. Productize project, lock, restore, and package UX | Partial / local workflow complete | Effective-profile restore/build/test/pack/install uses complete target and artifact-set identity; only explicitly authorized public lifecycle proof remains open |
| 22. Broaden platform and profile-guided performance maturity | Partial / target qualification | The support matrix and measured recommendation/budget policies are implemented; physical macOS/Arm64 and additional profile promotion remain open |

Closure sequence completed on this branch:

1. [x] Make uncertifiable Strict delivered closure fail publication and add a build-level rejection test.
2. [x] Add a first-class reviewed dependency lock, verify source/dependency hashes at consumption, and reject drift.
3. [x] Move hosted lifecycle discovery/binding into the canonical front end, preserve the original pipeline record, and make cleanup plus PowerShell-version behavior explicit.
4. [x] Promote command-family metadata into typed semantic/lowered nodes, complete CLR stream ownership, and prove injected runtime-free execution.
5. [x] Run external-module, managed-wrapper/provider, native/process, and Windows COM clean-target **disposition** fixtures without executing authored source or activating external capabilities; executable ecosystem qualification remains Milestone 19.

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
- [x] Fail Strict publication when delivered closure verification returns limitations or `Verified = false`, including opaque NativeAOT output and unverified native runtime dependencies.
- [x] Define deterministic PowerShell-command-to-CLR symbol mapping.
- [x] Define the callable CLR signature and PowerShell binding contract, including compiler-added parameters, parameter sets, positions, switches, remaining arguments, defaults, streams, and exceptions.
- [x] Carry bound value state, output cardinality/scalarization, collection element shape, and null/no-output semantics into ABI v4 instead of inferring them from CLR type-name spelling; Milestone 11 extends supported flows without reopening the ABI authority.
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

Implemented evidence from the 2026-08-28 closure candidate is refreshed after the final validation run below. The review-remediation wave specifically proves that:

- known `PSCustomObject` note-property reads convert from the adapter result to the bound CLR type instead of emitting uncompilable assignments;
- duplicate bounded `Add-Member` writes preserve the original value and emit a nonterminating error, while supported `IList` operands enumerate during array concatenation;
- absent environment variables remain semantically nullable/unknown through ABI emission;
- hosted lifecycle plumbing reads the original pipeline record privately and does not add a public or bindable synthetic parameter;
- Strict closure reconciles every delivered dependency against the consumed reviewed graph, whose exact identities include managed culture/public-key-token/content hash and module GUID/content hash;
- target reference-pack certification uses the selected framework catalog, including the complete net472 reference assembly set rather than the generated-code implicit-reference subset.
- delivered managed closure admits only reviewed, existing, deliverable dispositions with exact content hashes; an external/missing node cannot authorize staged bytes merely because its name and assembly identity match;
- Hybrid dependency resource hashes cover the rewritten embedded bytes that the host extracts and verifies;
- the generated workspace and emitted project pin the same exact SDK version recorded in provenance and the cache key;
- cache restore rejects reparse-point files and ancestors before reading payload bytes;
- net472 target-runtime identity includes both the top-level reference assemblies and the complete facade set;
- target promotion state is compiler-owned, so an explicit RID contract cannot self-assert `Supported`.
- schema-v1 and schema-v2 target identities authenticate their stored support state before migration, then reclassify against current compiler policy and receive a new schema-v2 identity without preserving caller provenance as semantic target identity;
- target-only analysis applies the contract before RID/TFM dependency planning, so analyze, explain, and the reviewed build consume one graph; NativeAOT locks use the exact runtime-pack version declared by the selected SDK rather than the highest ambient same-major pack;
- cache keys cover canonical restore results, actual resolved package bytes, and the selected SDK/reference-pack bytes, while copied payloads are rehashed before atomic restore;
- the first explain schema includes file-level blockers and missing dependencies and redacts authored absolute paths as well as omitting path-bearing source identities;
- the published complete `PowerShellCompiledMethod` constructor signature remains available while exact hosted-region counts use a new overload;
- existing-artifact target-host validation recomputes artifact hashes and sizes and binds each supplied file to its managed or NativeAOT target, deployment, format, architecture, support, and verified closure before execution;
- optimization evidence schema 3 reports three immutable IR rewrites, five backend-selection optimizations, and source instrumentation separately; benchmark metadata records the ReadyToRun SDK, and boundary results are explicitly an overhead ratio.

Integrated evidence from the final 2026-08-29 closure candidate:

- at exact branch head `06ffe2a412528a2fe4b2498fd0bdd4ecb067cdbf`, the compiler category passed 938 executed tests with zero failures on both Windows and Linux/WSL; each run retained the same one explicit certificate-dependent signing skip. The same head built `net472`, `net8.0`, and `net10.0` with zero warnings and errors, and the final independent review plus targeted compatibility confirmation reported no remaining findings. This is exact-head branch evidence, not source-code coverage, a default-branch merge, public package, or released-product proof. Certificate-backed signing remains a separate live acceptance gate when a suitable Windows identity is configured;
- public PowerShell DSL acceptance built unrelated arbitrary module identities through Hybrid and Strict modes, verified Strict rejection, exact ZIP and managed local-repository package contents, temporary installation, extracted import/invocation, checkpoint reuse, and source/resource/payload tamper rejection;
- an earlier certificate-backed public DSL acceptance signed the exact finalized payload, recomputed canonical manifest and per-file hashes after signing, verified the recorded certificate/signature counts, extracted the ZIP, revalidated Authenticode status, and invoked the delivered command; the current candidate retains this as an explicit live gate rather than claiming it from the skipped run;
- compiler checkpoint reuse now requires a cryptographically valid detached CMS signature from the currently configured signing identity and binds compiler/core assembly bytes, the complete staged-source tree, exact pre-compilation staging identity, dependency-lock content, target and compilation settings, and the complete normalized non-secret transformation, build, signing, artifact, publication, and installation plan; reuse revalidates the canonical manifest, staged root module, exact inventory, artifact identity, and payload hashes;
- finalized payload hashes are captured after checkpoint finalization and checked after lifecycle hooks and before every delivery boundary; staging, packed ZIP, unpacked folder, managed repository package, and installed-module roots all receive the same portable canonical finalization after their last mutation, checkpoint authority stays machine-local, and signed archives add refreshed post-sign evidence plus detached CMS authority;
- canonical compiler diagnostics, decision trace, reproduction evidence, and manifest survive staging and delivery; manifest schema 12 embeds one immutable unit-disposition ledger whose metrics are consumed by coverage, explain, census, reproduction, and boundary evidence without counting manifest hooks or hosted lifecycle wrappers as typed authored emission, and binds the exact target-specific NuGet closure lock consumed by a project build;
- final reviewer regressions prove case-correct per-source dependency causes, separate delivery-wide requirements, fail-closed inaccessible explicit/CompleteModule payload, diagnostic-path tamper rejection, post-copy unpacked inventory refresh, and AutoRevision signing evidence restricted to byte-identical verified payload;
- `PSPublishModule.csproj` and its compiler project references built with zero warnings and errors for `net472`, `net8.0`, and `net10.0` on Windows and Ubuntu after the final compatibility changes;
- the final target-host lane rebuilt and executed Strict `net10.0` framework-dependent and NativeAOT artifacts on Windows 10 x64 with PowerShell 7.6.4 and Ubuntu 24.04.3 x64 with PowerShell 7.6.5; both profiles preserved exact Unicode/resource output, returned 1 for invalid arguments, survived executable/native-closure inspection, and exercised cancellation, while Windows PowerShell 5.1 revalidated the same Windows artifact hashes and a one-byte tamper was rejected before execution;
- the portable generic Hybrid corpus emits 8/8 functions (100%) with zero retained-source fallback or eligible-function loss; one emitted function truthfully records its bounded hosted command region as runtime-routed without reducing typed coverage;
- the final seven-suite Windows benchmark packet records clean exact head `009901b0bbf56285a1ab291e2a1bff760e05a4c9`, zero validation failures, explicit ReadyToRun SDK 10.0.303 metadata, generated artifact hashes/sizes, and refreshed computation, startup, local-call, dispatch-amortization, and boundary-overhead results;
- earlier closure review waves identified five blocking and two documentation findings that were remediated at their owners; the final current-head independent review result is recorded with the final validation packet rather than inferred from those older candidates;
- a replaceable external-source census snapshot reports 122/1,235 emitted functions (9.88%), 21 analyzer-eligible functions routed to fallback, 1,263 authored files, 1,353 units, and zero parse errors; those caller-supplied inputs are scale evidence, not committed compiler configuration;
- all compiler behavior and prioritization remain keyed to generic syntax, semantic IR, dependency, host, target, resource, and artifact contracts; repository and module identities never select behavior;
- semantic lowering is the eligibility authority; structural diagnostics are mapped from canonical semantic features and cannot independently veto a successfully lowered unit;
- semantic IR collections take immutable snapshots, document identities are relocation-stable and filesystem-case-aware, loop variables retain PowerShell function scope, and generated literals cover control characters plus non-finite floating-point values;
- typed targets reject native-process effects before emission, while strict dependency verification rejects managed `System.Diagnostics.Process.Start` references before runtime-free certification;
- the former 139/1,235 result is retained in the companion guide as a historical snapshot of the deleted direct AST emitter, not represented as current all-IR coverage;
- the lowered C# method backend has no reference to `System.Management.Automation.Language`; the former direct AST-to-C# emitter files, transpiler graph reconstruction, and AST-aware Strict executable shaping are absent;
- deterministic graph schema 5 snapshots drive analysis/build planning and manifest schema 12; build requires a reviewed lock by default, marks an explicit development opt-out as unreviewed, rejects source, exact-identity, content-hash, delivered-closure, and analysis-to-build drift, records selected manifest identities separately from requested constraints, resolves local/acquired transitive modules plus managed/P/Invoke closure without importing source, and records managed, native, process, and literal `New-Object -ComObject` boundaries;
- PowerShell 7 Hybrid advanced-function fixtures preserve original pipeline-record identity and properties while executing `begin`, per-record `process`, `end`, and `clean` through a hosted `SteppablePipeline`; cleanup runs across begin/process/end failure paths, PowerShell 5.1 behavior and pre-7.3 `clean` rejection are explicit, and Strict rejects hosted-only lifecycle;
- deterministic external command-provider registration, typed projection/filter/map/sort stages, distinct information and `Write-Host`/`PSHOST` sinks, complete PowerShell stream ownership, and injected runtime-free adapters flow through the canonical semantic/lowered contracts; the strict library proof executes an injected provider without an SMA dependency;
- the clean-target interop matrix records TFM/RID/bitness, ownership, error, cancellation, cleanup, threading, and COM apartment contracts without activating COM or executing authored source; actual adapter execution remains profile-specific, while supported-RID promotion passed for the bounded Strict executable profiles named in Milestone 14;
- the repository-wide 800-line command still reports pre-existing owners; the formerly 1,200–1,900-line active compiler planning and execution owners were split by responsibility. The current closure also reduced `ModuleBootstrapperGenerator.cs` to 902 lines and `PowerShellBoundCSharpBackend.cs` to 708 lines by extracting named responsibilities, restoring hard-ceiling compliance and practical growth headroom.

Current 2026-08-30 expansion evidence keeps each proof lane separate:

- the focused compiler category passes 1,012 tests with zero failures and one explicit certificate-dependent signing skip on Windows; omitted compatibility profiles resolve to Windows PowerShell 5.1 for `net472` and PowerShell 7.6 for modern targets, while explicit and migrated project profiles remain authoritative. Instrumented coverage for the compiler owner is 52.32% line and 43.60% branch, while the separately packaged provider SDK is 88.60% line and 58.10% branch;
- the fixed public packet passes baseline-gated Hybrid build/import/invocation for 10/10 unrelated public modules and target execution for 4/4 Strict programs on Windows and Linux; these are complete-program packet rates, not estimates of PowerShell-language coverage;
- the Hybrid packet analyzes 735 authored units, emits none, and retains or runtime-routes all 735 without omission or rejection; the Strict packet analyzes, binds, and emits all 18 authored units without fallback, including 11 methods in a realistic four-file application. This deliberately exposes the current gap between broad package compatibility and native typed emission;
- isolated restore now authenticates each archive against NuGet's canonical signed/unsigned content identity, verifies raw archive and extracted payload bytes separately, and requires the generated compilation project to consume the selected target `packages.lock.json` in locked mode. The durable manifest binds that closure-lock SHA-256, and regression tests reject mutable-sidecar package substitution, extracted-file tampering, and missing or additional actual restore packages;
- two clean quick benchmark packets at implementation head `99c1ad0fc9a25aae5b71eb6f308cebf339fa2fbe` completed every lane without validation failure. The repeated build/import qualification rows remained within the prior quick reference envelope; these are smoke and variance evidence, not a replacement for the full promotion packet or a universal performance promise;
- `net472`, `net8.0`, and `net10.0` compiler-owner builds and the `net10.0` CLI build complete with zero warnings or errors. Physical macOS/Arm64, signed external-module/transitive-wrapper, remote management, and public package lifecycle evidence remain open exactly where Milestones 19, 21, and 22 say they do.

Green tests prove the tested branch candidate and artifact profiles. They do not certify an opaque native closure, substitute for the separately recorded NativeAOT target-host execution, establish current default-branch integration, publish a package, or prove a release.

### 2026-08-30 independent maturity reclassification and closure

The validation review at branch head `afe1946a344352ee8cb9fa534be5aeda34b95f68` reproduced the then-current external assessment online and offline: 6/6 inputs, 154 source files, zero parser errors, 3/185 emitted units, 3/173 emitted functions, and zero complete-workload executions. Fifty-three focused semantic-oracle, fuzzing, provider, management, interop, project-workflow, lock, maturity, and census tests passed. It also exposed nondeterministic SDK selection. The integrated branch now pins SDK `10.0.303` with roll-forward disabled; normal build/test/CLI entrypoints consume that supported selection rather than an ad hoc `dotnet exec` workaround.

The review established four corrections; this branch has closed their architecture portions as follows:

- `SemanticProfileId` participates in the normalized target/hash, binder/lowering, provider selection, cache/artifact identity, diagnostics, and compatible `#requires` decisions. Exact-host per-feature oracle evidence now covers every promoted semantic family.
- provider packages are reconciled with canonical `.nuspec` identity and reviewed signer policy, and separately built adapters execute through Strict managed, Strict NativeAOT, Hybrid, and binary-module artifacts without compiler loading or source-specific branches. The executable matrix observes typed results, every stream, errors, cooperative cancellation through a generated host and local call graph, adapter-owned exclusive-file cleanup after normal return, failure, and cancellation, and one representative external filesystem operation from a native PE.
- at review time, the semantic-oracle harness did not preserve every claimed observation, pin every exact host artifact, or automate immutable-profile monitoring. The current branch now carries reviewed exact pins for all three promoted hosts, native cases for every promoted family, a bounded schema-3 structured envelope, a read-only scheduled upstream monitor, explicit selected-property `AutomationNull` identity, a versioned framed Strict-observation protocol for nullable/string/culture-sensitive values, runtime-free differential evidence for every promoted family, and direct ordered OS child-process launch/exit evidence. Final `LASTEXITCODE` remains a separate mutable state observation and is never substituted for process history.
- the fixed public Hybrid packet still proves safe compatibility rather than typed benefit because it emits 0/735 units, but its baseline is enforced. The current opt-in external benefit packet separately proves 4/4 emitted commands across three scenario families, so safe packaging and measured typed benefit remain distinct evidence lanes.

The branch's former 36-commit gap was resolved by integrating `origin/main`, and the post-integration focused gates were rerun. These findings did not reopen the canonical parser → binder → bound IR → lowering → backend design. Exact-host oracle coverage, the bounded provider conformance/family gate, and the three-family Hybrid benefit packet are now closed; useful external ecosystems, additional physical targets, and authorized release proof remain explicit product gates.

## Semantic pipeline migration checkpoint — Complete

The original semantic/back-end consolidation closed, but exact-head artifact and lifecycle review reopened the product-level checkpoint:

- [x] Integrate `origin/main`, resolve the 18-commit audit-snapshot gap, and rerun compiler tests, multi-TFM builds, corpus/census, generated-consumer, and artifact proof on the resulting head.
- [x] Make the bound/analyzed/lowered semantic result the sole eligibility authority; replace retained structural semantic vetoes with diagnostics produced or mapped from canonical semantic owners.
- [x] Remove `CommandAst` call-graph reconstruction from the transpiler and consume the semantic graph directly.
- [x] Replace AST-aware Strict executable parameter and invocation shaping with explicit bound/lowered executable contracts.
- [x] Upgrade source maps from method start lines to generated ranges and source line/column spans suitable for diagnostic remapping.
- [x] Make the public ABI manifest reflect the exact callable CLR signature and PowerShell binding contract, including compiler-added parameters.
- [x] Complete ABI value-state, semantic nullability, and output-cardinality contracts; Milestone 11 adds breadth through the same contracts.
- [x] Inspect supported managed delivered-artifact formats; source token scanning remains a diagnostic defense, not the proof authority.
- [x] Fail Strict publication for formats or dependencies the closure verifier cannot certify.
- [x] Make trim/AOT warning enforcement fail closed, and keep actual target-host NativeAOT execution in Milestone 14.
- [x] Decide explicitly that current emitted code does not justify `PowerForge.CompiledRuntime`; manifests record the versioned artifact contract and no runtime substrate dependency.
- [x] Split `PowerShellSemanticAnalyzer` and `PowerShellSemanticBinder` by named semantic responsibility before either grows further.
- [x] Split `PowerShellCompilationBoundPipelineTests.cs` by behavioral contract and extract artifact publication/manifest/closure responsibilities from `PowerShellCompilationArtifactBuilder.cs`.
- [x] Apply the existing line-count tooling as a scoped growth gate: no touched non-generated compiler production/test file exceeds 800 lines.
- [x] Split the analyzer, binder, lowered backend, lowerer, and principal bound-pipeline test owners by semantic responsibility before M11–M13 growth; active primary owners now remain below 800 lines with extracted declarations, host requirements, invocation/parameter emission, and host-focused tests.
- [x] Make build consume and validate one reviewed dependency lock instead of trusting recomputed current filesystem state.
- [x] Move hosted lifecycle binding onto the canonical front-end/IR boundary and close raw-input, cleanup, and target-version behavior.

Exit gate: the integrated candidate has one eligibility, consumed graph lock, executable contract, semantic ABI, source-map, lifecycle, and fail-closed closure authority; focused proof remains green; and active semantic owners have enough responsibility-based headroom for the next milestone rather than merely remaining below the 1,000-line ceiling.

## Milestone 8 — Build command and pipeline semantics

- [x] Canonical deterministic resolution for commands, module-qualified names, and aliases registered in the current snapshot.
- [x] Define deterministic external provider registration/injection without editing the built-in singleton, including duplicate and ambiguous ownership rules across providers.
- [x] Deterministic command-semantic registry.
- [x] Duplicate and ambiguous registration validation.
- [x] Versioned command-family/provider contract whose resolvers are compile-time-only, deterministic, capability-declared, and forbidden from importing or executing source modules.
- [x] Public diagnostics and census features mapped from the registry/binder result rather than matching command names in a parallel structural analyzer.
- [x] Initial hosted stream contracts for `Write-Verbose`, `Write-Debug`, and `Write-Warning`.
- [x] Complete CLR sink ownership for success output, verbose, debug, warning, information, `Write-Host` with `PSHOST` record identity, and nonterminating error; bounded provider shapes fall back when their argument semantics are not proven.
- [x] Initial hosted provider contracts for projection, filtering, mapping/enumeration, and sorting command families.
- [x] Bind typed family-specific projection/filter/map/sort nodes and carry their value, cardinality, stream, error, and capability contracts through lowering.
- [x] General bounded-command-region binder.
- [x] Runtime-free command-adapter contract with semantic-profile, dependency, and AOT capabilities.
- [x] Implement and execute an injected runtime-free adapter in a Strict CLR library, then reserve that route for future managed-wrapper/AD-style adapters rather than adding product checks.
- [x] Provider metadata for command output, cardinality, stream, and error contracts.
- [x] Per-command parameter contracts, including canonical names, aliases, and positional eligibility; stream providers no longer share a fictitious `-Message` shape.
- [x] Bounded object-mutation provider ownership for exact `Add-Member -NotePropertyName/-NotePropertyValue` shapes.
- [x] Typed pipeline-stage composition does not carry executable PowerShell source as stage semantic payload; source text exists only on explicitly hosted fallback regions.
- [x] Explicit pipeline symbols for `$_` and `$PSItem`.

Exit gate: adding a supported `Select-Object` shape does not require coordinated semantic edits to analyzer, transpiler, emitter, Hybrid composer, and census.

## Milestone 9 — Resolve modules, dependencies, and interop

- [x] Replace the flat dependency inventory as the planning authority with semantic, dependency, and deployment graphs that share stable node identities.
- [x] Discover the currently modeled static edges from manifests, `using`, `#requires`, literal `Import-Module`, dot-sources, CLR references, native loads, and explicit build inputs.
- [x] Parse and bind exact module specifications, including required/minimum/maximum version and GUID, through parser-owned script requirements without comma-split or regex-only identity discovery.
- [x] Traverse contained `NestedModules`, `RequiredAssemblies`, type/format data, runtime assets, and module initialization hooks.
- [x] Resolve contained/acquired transitive `RequiredModules` and their dependency closure without importing or executing module initialization; unresolved external modules remain explicit environment requirements.
- [x] Emit a deterministic graph snapshot with the currently known identity, hash, source, edition, TFM, RID, architecture, and provenance fields.
- [x] Record the selected local manifest version separately from required/minimum/maximum constraints and detect conflicting selected manifest identities.
- [x] Normalize lock hashing to LF so identical graphs have one hash across Windows and Unix.
- [x] Add a first-class expected dependency lock to the build request, consume it during build, verify source/dependency hashes before and after generation, and fail on drift or a different resolution.
- [x] Keep explicit restore/acquisition separate from read-only resolution and analysis.
- [x] Classify script, binary, mixed, CDXML/CIM, implicit-remoting/dynamic-proxy, managed-library, native, and external-process dependencies.
- [x] Read binary module and managed assembly metadata without importing or executing module initialization in the compiler process.
- [x] Traverse adjacent transitive managed references and managed P/Invoke imports without loading assemblies; record missing managed/native requirements explicitly, and certify only exact catalogued target-host architecture, ABI, publisher, transitive-native, deployment, and execution contracts while all other managed P/Invoke closure fails closed.
- [x] Certify Strict managed references against the requested target framework reference pack rather than the build host's trusted-platform-assembly set.
- [x] Model module load order, version conflicts, cycles, assembly unification/load context, native assets, and external target requirements.
- [x] Assign one artifact disposition to every dependency: compiled, referenced, hosted, bundled, private-restored, externally required, or rejected.
- [x] Discover literal `New-Object -ComObject` activation as a hosted/rejected capability.
- [x] Cover `Type.GetTypeFromProgID`, CLSID activation, apartment requirements, and typed adapter ownership without activating COM during analysis.
- [x] Add a representative external binary-module/Active Directory-style Hybrid artifact with typed work before and after a hosted command region.
- [x] Prove direct managed references, hosted external cmdlets, and explicit generated-adapter paths remain distinct across graph and executable artifact fixtures.
- [x] Add native-library and external-process disposition fixtures that lock RID, error, cancellation, and cleanup requirements; actual adapter execution remains a profile-specific target-host gate rather than being inferred from graph discovery.
- [x] Add a Windows COM Package/Hybrid/Strict matrix proving hosted ownership and precise Strict rejection before typed COM support exists.
- [x] Record redistribution, publisher/signature, servicing, and license constraints separately from technical dependency resolution.

Exit gate:

- the build consumes the same reviewed dependency lock used for analysis, manifest evidence, and clean-target validation, and rejects post-analysis drift;
- a Hybrid artifact can preserve a required external module and compile safe surrounding regions without pretending the module became native;
- a managed-wrapper graph contains or requires its complete transitive assembly/native closure, while Strict publication rejects every native closure the target-host verifier cannot certify exactly;
- missing, ambiguous, incompatible, cyclic, or non-redistributable dependencies fail or remain external exactly as planned;
- COM and native capability requirements are visible in diagnostics and the artifact manifest.

## Milestone 10 — Complete advanced-function lifecycle

- [x] PowerShell 7 Hybrid hosted execution for `begin`, per-record `process`, `end`, and ordinary-path `clean`.
- [x] Bind lifecycle metadata and source into the canonical front-end/IR result instead of reparsing and appending an AST-derived method after typed compilation.
- [x] Guarantee idempotent authored `clean` on begin failure, process/end failure, stop, and early termination while the owning runspace remains usable; terminal runspace closure/breakage instead guarantees idempotent resource disposal without falsely claiming the authored block ran.
- [x] Basic `ValueFromPipeline` binding through a generated cmdlet.
- [x] Basic `ValueFromPipelineByPropertyName` binding through a generated cmdlet.
- [x] Preserve the original pipeline record for `$_`/`$PSItem`, object identity, adapted members, and unbound properties instead of reconstructing input from bound parameter values.
- [x] `ValueFromRemainingArguments` behavior.
- [x] common parameters.
- [x] `ShouldProcess` and `ConfirmImpact`.
- [x] per-record state and output.
- [x] terminating and nonterminating errors.
- [x] stream and progress lifecycle.
- [x] Define and test the target-version capability matrix: PowerShell 5.1/net472 lifecycle where supported, PowerShell 7 lifecycle, and `clean` as an explicitly rejected PowerShell 7.3+ capability on older hosts.
- [x] Add differential fixtures for original pipeline-record identity/properties, begin failure, stop/termination, and disposal after every lifecycle phase.
- [x] Keep `StopProcessing()` prompt while a hosted `process` block is running, wait without an arbitrary timeout, and execute the authored `clean` block exactly once after the owning runspace becomes available; if the runspace becomes closed or broken first, release the hosted pipeline once through the explicit terminal-host path.

Exit gate: representative conventional advanced functions execute as generated cmdlets with PowerShell-equivalent invocation and lifecycle behavior.

## Milestone 11 — Complete value and object flows

- [x] `$null`, missing, `AutomationNull.Value`, and no-output distinctions through ABI v4 value-state and output contracts.
- [x] scalarization and enumeration for the supported typed/hosted boundaries.
- [x] one-dimensional array and collection concatenation, including null-left and null-right array behavior.
- [x] `IDictionary` and ordered dictionaries.
- [x] `IList` and `ArrayList` flows, including negative indexing, mutation, and `Count`.
- [x] `PSCustomObject` construction and statically known note-property shapes.
- [x] bounded `PSObject.Properties['literal'].Value` access through the same known-property fact.
- [x] exact `Add-Member -NotePropertyName <literal> -NotePropertyValue <expression>` note-property mutation through one registered semantic family.
- [x] direct known-property reads and writes through the PowerShell adapter.
- [x] typed array/list/dictionary indexing and mutation.
- [x] adapted-object fallback boundaries: arbitrary ETS members, identity-observing methods, unknown shapes, and unsupported `Add-Member` flags remain rejected or Hybrid-hosted.

Exit gate: common object-shaping helpers compile without pretending arbitrary ETS behavior is statically known.

Evidence: Strict generated-module tests execute array concatenation, `ArrayList`, and object-shape flows; Hybrid differential proof keeps PSCustomObject identity observation on fallback; the generic corpus adds unrelated object and collection helpers without product-specific names or branches.

## Milestone 12 — Expand bounded runtime state

- [x] `$PSScriptRoot` and `$PSCommandPath` in supported packaged artifact contexts.
- [x] read-only target/runtime constants, including supported platform, edition, version, path, and invocation-state values.
- [x] classify mutable script/module tables and caches as explicit runtime-backed boundaries instead of snapshotting shared mutable identity.
- [x] `$Error` as one read-only per-invocation collection snapshot with mutation and method invocation rejected.
- [x] supported preference variables: verbose, debug, warning, information, error-action, progress, and confirm.
- [x] read-only environment-variable access with environment-provider mutation rejected.
- [x] read-only process, user-home, and current culture/UI-culture values with assignment still rejected.
- [x] closures and hosted command regions with statically proven captures.
- [x] explicit mutation and lifetime boundaries for environment, variable-provider, script, global, private, and `$Error` state.

Arbitrary global state, variable-provider escapes, uninspectable closures, and dynamic scope remain runtime-backed.

Exit gate: supported runtime state is represented in the IR and propagated through call graphs without special emitter checks.

Evidence: runtime-state expressions lower through one intrinsic policy, generated cmdlets capture one invocation-state dictionary, typed local calls receive the same snapshot, and net8/PowerShell 7 plus net472/PowerShell 5.1 differential tests cover process identity, user home, current culture/UI culture, preferences, common-parameter overrides (including the hosts' distinct `-Debug` behavior), and `$Error` where those hosts are available. The expanded pinned-host matrix reviews the process/user/culture case under every semantic profile.

## Milestone 13 — Run generic coverage waves

Coverage work resumes through semantic families in this order, adjusted by each fresh census:

1. [x] comment-based help preservation;
2. [x] bounded parameter types and defaults;
3. [x] common stream operations with command-specific parameter contracts;
4. [x] bounded subexpressions and expandable strings;
5. [x] function graph inference and statically known splatting;
6. [x] object, dictionary, and collection flows;
7. [x] hosted pipeline lifecycle;
8. [x] bounded `Select-Object`, `Where-Object`, `ForEach-Object`, and `Sort-Object` semantic families;
9. [x] bounded read-only runtime state and explicit dynamic-scope rejection;
10. [x] the currently accepted operator, conversion, CLR, dependency, and interop families through the shared IR.

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

Completion here means the current ten bounded waves went through the generic semantic pipeline and their unsupported variants remain explicit diagnostics. It does not mean arbitrary PowerShell syntax, ETS, dynamic scope, commands, or interop are compiled. The current product-neutral corpus is 8/8 emitted functions with zero retained-source fallback and one bounded hosted command-region route; one replaceable caller-supplied external-source snapshot is 122/1,235 emitted functions with zero parse errors. Future coverage gains continue through the same census-driven process without reopening this ownership gate.

## Milestone 14 — Productize managed, Hybrid, and native delivery

Milestone 14 closes the bounded artifact-delivery contract independently of Milestone 17: target contracts, exact source/dependency/native evidence, generated source maps, provenance, SBOMs, and target-host execution are sufficient to promote a deliberately narrow target set. Milestone 17 subsequently completed user-facing causal traces, redacted reproduction evidence, optional semantic-only IR, diagnostic audits, and authored-source failure mapping without changing the supported delivery profiles proved here.

- [x] Add the explicit integrity-bound target-contract model to engine requests, CLI/cmdlet input, analyze/explain/build planning, generated projects, and manifests.
- [x] Keep existing framework-dependent, self-contained, trimmed, and NativeAOT Strict outputs on one generated C# backend.
- [x] Add a ReadyToRun benchmark lane without promoting it to a public mode until evidence justifies it; the lane pins stable same-major SDK 10.0.303 and remains benchmark-only.
- [x] Complete the managed Hybrid EXE foundation using the same bound plan, generated cmdlets, embedded retained-source/dependency plan, and fallback contract as Hybrid modules; NativeAOT-hosted Hybrid promotion remains open.
- [x] Measure runtime typed/fallback boundary crossings and their cost so the tool can warn when Hybrid compilation is unlikely to help; the profiler records crossing counts, nanoseconds per crossing, estimated overhead ratio, and a deterministic advisory alongside static manifest evidence.
- [x] Pin or record SDK, its exact runtime-pack version, compiler-runtime, package, managed/native dependency, target-contract, and reviewed-lock identity for reproducible release builds.
- [x] Add a content-addressed incremental build cache keyed by normalized generated inputs and restore graph, actual resolved package bytes, compiler version/source identity, selected SDK/reference-pack bytes, build host, target contract, dependency lock, and TFM/RID; verify a copied payload before atomic restore and reject incomplete, malformed, modified, reparse-root/ancestor, escaping, or cross-target entries.
- [x] Emit source, PDB/symbol, public ABI, dependency graph/lock, target contract, CycloneDX SBOM, build provenance, and artifact-integrity evidence as applicable.
- [x] Run Strict managed and NativeAOT artifacts on every named supported RID rather than treating cross-publish success as execution proof; the supported set is `win-x64` and `linux-x64` for Strict `net10.0` framework-dependent and NativeAOT delivery.
- [x] Verify Windows and Unix exit codes, stdout/stderr encoding, signals/cancellation, file permissions, resources, executable architecture/imports, and native dependencies on those target hosts.
- [x] Preserve signing and atomic publication in PowerForge’s shared packaging owner.
- [x] Integrate script-to-binary module conversion into `Build-Module` after staged manifest/format preparation, with generic Hybrid/Strict configuration, source-tree isolation, payload preservation, normal downstream signing/testing/packaging, and structured coverage results.
- [x] Treat the compiler’s finalized, containment-checked payload inventory as the exact input to signing, archives, managed repository packages, and temporary installation instead of rediscovering files through generic module filters.
- [x] Preserve the pre-extension public CLR constructor and `BuildWithFinalizer` signatures as forwarding overloads so delivery finalization does not break already compiled consumers.
- [x] Build one immutable final unit-disposition ledger after emitter and artifact shaping, then derive manifest metrics, coverage, explain output, census, reproduction hashes, and boundary profiling from that same ledger; hosted lifecycle cmdlets remain explicit binary surfaces without being counted as typed CLR emission, and dependency causes retain case-correct per-source attribution.
- [x] Bind checkpoint reuse to compiler/core assembly bytes, the complete staged-source tree, exact pre-compilation staging identity, dependency-lock content, target and compilation settings, and the complete normalized non-secret transformation/module/release plan; require detached CMS authority from the configured signing identity and verify the canonical manifest, staged manifest root, exact inventory, artifact identity, and payload hashes before reuse.
- [x] Exclude machine-local checkpoint authority from delivered payloads, fail closed when lifecycle hooks drift finalized bytes, and refresh portable canonical hashes plus detached CMS authority inside signed packed artifacts after their last signing mutation.
- [x] Preserve the canonical compiler manifest and diagnostics in every delivery root, rewrite producer-local paths to portable relative identities, and recompute artifact and per-file hashes after the last mutation; carry Authenticode status only for byte-identical files from the verified signed source root so AutoRevision and unpacked post-copy mutations cannot inherit stale positive evidence.
- [x] Prove the public PowerShell DSL with arbitrary module identities across Hybrid success, Strict success and rejection, exact ZIP delivery, managed local-repository packaging, temporary installation, extracted import/invocation, checkpoint reuse, and tamper rejection; keep certificate-backed signing acceptance opt-in when a valid local code-signing identity is available.
- [x] Document the current supported versus experimental TFM/RID boundary and installed runtime requirements for every implemented artifact profile below.
- [x] Keep native shared-library export deferred until a concrete embedding consumer defines its ABI; no unsupported export surface was added to close this milestone.

Current target-profile contract:

| Artifact profile | External runtime requirement | Runtime evaluation | Current promotion state |
| --- | --- | --- | --- |
| Strict/Hybrid CLR library, portable `net472`/`net8.0`/`net10.0` | compatible .NET runtime | no hosted fallback in the delivered library | Supported within the bounded CLR ABI and clean-consumer tests |
| Strict/Hybrid binary module, portable `net472`/`net8.0`/`net10.0` | compatible PowerShell host | generated cmdlet/host lifecycle as declared | Supported within the tested PowerShell 5.1/7 host matrix |
| Package or managed Hybrid EXE, framework-dependent | compatible .NET runtime; PowerShell SDK/source closure is packaged | yes | Implemented; named RID/platform promotion remains experimental |
| Package or managed Hybrid EXE, self-contained/trimmed | no separately installed .NET or PowerShell runtime | yes | Build and benchmark contracts are implemented; each explicit RID remains experimental |
| Strict managed EXE, framework-dependent | compatible .NET runtime | no | `net10.0` `win-x64` and `linux-x64` are Supported; portable builds remain `PortableManaged`; other named profiles remain experimental |
| Strict managed EXE, self-contained/trimmed | none | no | Build and benchmark contracts are implemented; named profiles remain experimental |
| Strict NativeAOT EXE | none | no | `net10.0` `win-x64` and `linux-x64` are Supported after exact native-closure inspection and target-host execution; other named profiles remain experimental |
| ReadyToRun | depends on deployment choice | profile-dependent | Benchmark-only; public target requests are rejected |

`RuntimeRequirement` records what the target host must supply. `AllowsPowerShellRuntimeEvaluation` separately records whether the artifact can enter a hosted source/fallback boundary; bundling PowerShell SDK assemblies does not falsely turn that capability into an external PowerShell installation requirement. A RID is never marked supported from cross-publish alone.

Exit gate:

- [x] A user can analyze and explain one explicit target contract, then build it with optional `--emit-source` through the public entry points, while the checked-in target-host harness tests the artifact execution contract without requiring compiler internals.
- [x] Generated CLR libraries are consumed from clean C# projects, Strict EXEs run without PowerShell, and every named supported NativeAOT RID runs on its target host.
- [x] Hybrid EXEs report the bundled runtime, embedded source, typed coverage, fallback closure, and measured crossing cost truthfully.
- [x] Manifests and release evidence distinguish source, managed artifact, native artifact, signing, and publication state.
- [x] Public `Build-Module` configuration can opt an arbitrary compatible module into Hybrid or Strict compilation without consumer-specific code, and every downstream delivery phase consumes the exact finalized compiler payload.

## Milestone 15 — Optimize proven IR

- [x] immutable constant folding for conservative pure literal operations while retaining authored source spans.
- [x] immutable dead-branch elimination for statically literal `if`/`while` conditions while retaining selected authored spans.
- [x] allocation-reducing backend selection for empty arrays and list concatenation without changing collection/cardinality semantics.
- [x] bind adjacent compatible pipeline stages into one hosted invocation and report the avoided crossings as backend-selection evidence.
- [x] bind adjacent hosted operations with the same boundary contract into one command region and report the coalesced statement count.
- [x] select indexed backend emission for proven typed-array iteration.
- [x] emit repeated proven generic conversions through one cached per-method conversion helper.
- [x] instrument generated source and PDBs through authored-document publication and `#line` sequence mapping.

Exit gate: **Complete.** Three immutable rewrite passes and the separately classified backend/instrumentation optimizations preserve differential and artifact contracts, expose truthful evidence, and the clean exact-head suite records meaningful typed-kernel, local-call, startup, artifact-footprint, and boundary-amortization results. Small host-dominated workloads are not used to justify compiler-wide complexity.

## Milestone 16 — Productize the provider SDK and trust model

- [x] Build and deterministically pack a small provider SDK whose public surface describes versioned command-family, semantic-profile, adapter-operation, dependency, AOT, stream, error, and capability metadata consumed by the compiler.
- [x] Define deterministic provider discovery from explicit build inputs and locked packages; do not scan arbitrary loaded assemblies, user profiles, or ambient module paths.
- [x] Add provider/adapter ABI negotiation and reject unsupported schema, semantic-profile, or operation versions before source analysis.
- [x] Bind provider package/archive/payload hashes and declared assemblies, publisher, license, dependencies, target restrictions, and capabilities into the reviewed provider/dependency locks and artifact provenance.
- [x] Keep provider discovery metadata-only and out of the compiler process; importing modules, loading arbitrary provider assemblies, or executing authored source during analysis is prohibited.
- [x] Add metadata conformance covering deterministic registration, ambiguous aliases, qualification, schema/profile negotiation, declared streams, dependency identities, target restrictions, and fail-closed malformed or conflicting packages.
- [x] Reconcile provider identity, publisher, license, exact dependency IDs/versions, and package contents against canonical NuGet `.nuspec` metadata rather than trusting only `provider.json`; require an explicit reviewed signer fingerprint when signer policy is configured rather than treating signature integrity alone as publisher authorization. Dependency content identities remain reviewed inputs here and are independently reconciled with NuGet's resolved lock and acquired bytes by the isolated project restore.
- [x] Define one versioned executable provider ABI through which an independently built package supplies typed binding/lowering metadata and a runtime-free adapter implementation without compiler-source edits, command-name branches, assembly loading in the compiler, or a second eligibility authority. ABI 4 added the separately named `ExternalOperation` family and Strict-executable stream/cooperative-cancellation host contract. ABI 5 adds a manifest-bounded process-isolation deadline and closed scalar string worker contract; ABI 4 and earlier packages fail exact negotiation instead of silently acquiring that meaning.
- [x] Expand isolated executable-provider conformance beyond the original deterministic information-stream adapter to prove values, types, cardinality, every stream, errors, cancellation, cleanup, dependency closure, and AOT/runtime-free claims against each package's actual implementation. The exact locked executable route proves scalar and collection success values across its closed `string`/`Int32`/`Int64`/`Double`/`Boolean` ABI, all seven PowerShell stream sinks, terminating adapter errors, cooperative cancellation propagated through a local-function call graph, adapter-owned exclusive-file cleanup after normal return, failure, and cancellation, and a two-assembly managed dependency closure in Strict and Hybrid where applicable. Generated binary cmdlets route `StopProcessing()` to the same cooperative provider token and deterministically dispose their cancellation source after invocation. A separately packaged file-read operation also builds and executes as a `net10.0` `win-x64` Strict NativeAOT PE with AOT/trim warnings treated as errors; package adapters that do not declare AOT compatibility fail before publication.
- [x] Execute one separately built, locked managed-wrapper adapter through Strict and Hybrid artifacts and verify its delivered assembly closure and observable information-stream result.
- [x] Reuse the executable route for one representative filesystem provider operation without adding the command or package identity to compiler policy. HTTP, directory-service, WMI/CIM/CDXML, native/process, and future COM execution remain ecosystem qualifications in Milestone 19 where those operations can honestly be runtime-free.
- [x] Document the route for reusable filesystem, HTTP, JSON/CSV, managed-wrapper, directory-service, management-provider (WMI/CIM/CDXML), native, process, and future COM providers without turning command names into compiler intrinsics.

Exit gate: **Complete within provider ABI 5.** A separately built provider package is reconciled with canonical NuGet metadata, governed by package/publisher/license/signer policy, locked by exact archive and assembly identity, metadata-validated without loading it into the compiler, and invoked from generated Strict managed, Strict NativeAOT, Hybrid, and binary-module artifacts through the canonical registry/lowering route. The route proves closed scalar/collection string and primitive-value types, all seven stream sinks, terminating errors, cooperative host cancellation, adapter-owned exclusive-file cleanup after normal return, failure, and cancellation, an exact transitive managed assembly at runtime, and one representative external filesystem operation compiled into and executed from a native PE. ABI 5 additionally permits a reviewed runtime-free provider to declare a closed scalar string operation as process-isolated with a one-to-3,600-second deadline; generated Strict executables create one-use inherited anonymous-pipe capabilities, emit only the provider-ID switch reachable from the entrypoint call graph, exchange length-prefixed bounded frames, keep the direct worker alive until the response is consumed, then kill the complete worker tree; ordinary authored or provider-owned process launch remains forbidden. Library, binary-module, and Hybrid hosts fail closed for this contract. This closes the extensibility architecture and bounded conformance gate; it does not claim arbitrary filesystem commands, HTTP, directory, management, native/process, or COM coverage. Those useful provider ecosystems remain Milestone 19 work. Public feed publication remains a separate release decision.

Executable entry points are accepted only from exact reviewed provider-package locks; direct command-provider inputs cannot bypass package trust with an entry point or external-operation declaration. The SDK packer, compiler package reader, and semantic registry share the same executable-contract shape validator. Strict observation frames ordered success, information/host, warning, verbose, debug, and nonterminating error records, and bound/lowered IR evidence reports the runtime-free provider capability without misclassifying it as hosted PowerShell streams.

## Milestone 17 — Add compiler explainability and reproducible diagnostics

- [x] Emit a stable decision trace linking source spans to bound features, inferred types/value states, capabilities, provider resolution, dependency decisions, fallback/rejection causes, lowering choices, and artifact disposition with deterministic ordering and relocation-safe source identity.
- [x] Add `explain` output for humans plus versioned JSON for tools, with deterministic ordering, relocation-stable identities, and absolute-path redaction.
- [x] Produce integrity-bound redacted reproduction evidence containing normalized source identities and hashes, compiler/semantic-profile and toolchain identities, target contract, provider contracts, dependency lock, generated source hash, ABI hash, source-map hash, trace hash, and diagnostics hash including portable diagnostic file identity, without copying authored source, secrets, absolute paths, or machine-owned state.
- [x] Map build, trim/AOT, dependency, ABI, and runtime failures back through statement-level source maps and boundary contracts instead of exposing generated-project internals as the primary diagnosis.
- [x] Add optional bound/lowered IR snapshots suitable for diffing while keeping parser AST objects, authored source text, and executable hosted source out of runtime-free semantic payloads.
- [x] Record cache hit/miss reasons, graph/ABI drift, fallback crossings, and provider selection so performance and reproducibility claims can be audited.
- [x] Add golden compatibility tests proving equivalent inputs produce equivalent semantic fingerprints across path relocation, input/declaration order, and the supported host build lanes while retaining parameter order as a semantic contract.
- [x] Define local-only retention and redaction policy for diagnostics, crash bundles, generated source, and optional IR evidence before any telemetry or automatic upload is considered.

Exit gate: **Complete.** A user can explain why a unit compiled, fell back, or failed; reproduce the same decision from integrity-bound evidence; compare optional semantic-only IR; audit cache, dependency, ABI, fallback, and provider decisions; and map build/runtime failures back to authored source without reverse-engineering generated C# or exposing source, secrets, or machine paths.

## Milestone 18 — Establish versioned semantic oracles

This correctness gate strengthens both built-in and external-provider semantic paths without copying PowerShell's compiler architecture.

- [x] Define named semantic-profile manifests for Windows PowerShell 5.1 and supported PowerShell 7 behavior families, plus versioned semantic-provenance records with documentation/upstream references and canonical compiler owners.
- [x] Build a black-box oracle harness whose fixtures are independent of emitted C# and compiler implementation details and whose expected-result envelope can be compared with interpreted, Strict, Hybrid, and hand-written C# lanes.
- [x] Add initial generated, property-based, metamorphic, and adversarial cases for arithmetic/conversion behavior and selected stream/filesystem semantics within the supported grammar.
- [x] Pin the exact executable host artifact for every promoted profile—patch/build, edition, OS, architecture, culture, feature switches, executable hash, and upstream commit/release identity—instead of a broad version range or executable name. The embedded schema-1 pin set records reviewed Windows x64 evidence for Windows PowerShell 5.1.26100.9168, PowerShell 7.4.19, and PowerShell 7.6.5; the standalone releases also bind the official archive URI/hash and peeled upstream tag commit. The schema-2 runner can require those canonical identities, and the promotion gate uses the immutable catalog by default.
- [x] Preserve and compare structured output value/type/null/cardinality/enumeration state, original property order, all stream records, structured error identity/category/termination, enclosing-process exit code, culture/encoding, declared filesystem effects, and final `LASTEXITCODE` state without reducing them to one formatted comparison string. Schema 3 distinguishes explicit null success from no output, records retained collection cardinality and element types, binds encoding facts, and validates bounded shape/profile invariants before promotion.
- [x] Preserve an explicit `AutomationNull` identity before PowerShell's public hosting boundary collapses it to null/no-output. The isolated wrapper snapshots explicitly selected property values through a CLR identity check before PowerShell parameter binding; ordinary null remains a separate state, and a bare sentinel that has already collapsed to no output is never inferred from formatting.
- [x] Add a versioned framed Strict-observation protocol for nullable, string/multiline, and culture-sensitive values. `PowerForge.StrictObservation/1` is observer-activated, base64 frames type/state/value payloads, binds the requested culture inside the artifact, distinguishes null from no output, and fails closed on malformed, unframed, contradictory, oversized, or wrong-type output.
- [x] Directly instrument sequenced child-process launches/exits. On the promoted Windows profiles, the compiler-owned runner attaches the isolated external host to a Job Object completion port, waits for the host's ready signal within the overall request deadline, reconciles unique launch packets with authoritative cumulative Job Object process accounting, timestamps launches against the authored-source gate, and only then releases authored source. Explicit queue barriers replace timing delays. The boundary records post-gate direct-child launches and exits observed before closure with stable invocation/sequence ordinals and the versioned `Windows.JobObject.ProcessTree/1` source identity; missing, surplus, recycled, late, inferred, contradictory, or unbounded evidence fails closed. It terminates a surviving asynchronous tree when observation closes, while omitting observer-forced exits from authored history. Schema 3 keeps final mutable `LASTEXITCODE` state separate and never infers an event from it.
- [x] Add a minimized executable oracle case and exact observed-host matrix for every promoted semantic feature. Thirty-one native `.ps1` resources cover all currently promoted feature families, provenance resolves to real applicable case IDs, and the complete 31-case/93-observation interpreted matrix executes on the pinned Windows PowerShell 5.1, PowerShell 7.4.19, and PowerShell 7.6.5 hosts. The certified Strict-executable observer validates ABI type/cardinality, executes artifacts without PowerShell, and promotes a 24-case runtime-free subset covering compatible `#requires`, profile-fixed `$PSVersionTable.PSVersion.Major`, bounded compiler-owned dictionary flow, compile-time-safe literal conversion, stable-string interpolation, local function graphs, typed/defaulted parameters, bounded parameter-validation metadata, exact/alias/abbreviated parameter binding, index/member assignment targets, exact constructed-generic `List<T>` construction/member invocation with PowerShell-compatible null `Count`, ordered typed catch filters, bounded scalar regex-switch matching, bounded literal or variable one-dimensional stable-scalar typed-array `ForEach-Object` enumeration with compiler-owned `$_`/`$PSItem`, pinned null-record evidence, and cross-target null-versus-empty artifact evidence, bounded typed-executable begin/process/end lifecycle invocation, bounded local `Get-Help` Name/Synopsis metadata, typed integral compound arithmetic, comparison operators including PowerShell-sign-aware nullable integral and decimal ordering, logical operators including bounded short-circuit non-null refinement, bounded literal `New-Object` CLR construction through the canonical constructor IR, and post-test loop control flow against the exact PowerShell 7.6.5 pin. The generic-list slice accepts only recursively target-compatible type arguments and exact target-reference members; other generic type definitions, generic methods, dynamic receivers, and non-exact overloads remain hosted or rejected. A null typed array contributes one by-value pipeline record, converted to the exact stable parameter type, while an empty array contributes zero records. Types whose null conversion produces a PowerShell parameter-binding error fail closed rather than receiving an invented CLR default. The pipeline-enumeration slice retains the assignment-only process-block boundary and rejects process output/control flow, scalar input, and object arrays. The lifecycle slice accepts one explicitly typed stable-scalar `ValueFromPipeline` parameter, a compiler-allocated typed input array, and explicit begin/process/end blocks; other lifecycle shapes and binary-module lifecycle commands remain hosted. The runtime-free help slice accepts one statically named compiled local function and exposes immutable Name/Synopsis strings from the canonical comment-help binder; help discovery, formatting, options, and other properties remain hosted. All 24 promoted runtime-free cases (100% of that subset) now have runtime-free observations. This percentage measures the promoted runtime-free oracle subset, not the complete oracle catalog or the PowerShell language.
- [x] Carry the selected semantic-profile identity and behavior into target-contract hashing, analysis, provider resolution, binder/lowering decisions, cache keys, artifact selection, and diagnostics. Different behavior profiles produce distinct compilation identities; compatible `#requires` policy is selected from the exact profile.
- [x] Add an automated upstream-change lane that compares the pinned PowerShell source/test references and proposes affected reviews without silently advancing any immutable profile. The read-only scheduled/manual workflow resolves peeled stable patch tags, emits an affected-profile/case review artifact and summary, and fails closed on change; it has no write permission and cannot edit pins, open a pull request, or create an issue.
- [x] Fail feature promotion when selected hosts disagree and no explicit version split, target-specific lowering, fallback, or rejection contract exists. The canonical gate requires pinned provenance, two independent profile/surface observations, exact host-artifact identities for host-backed lanes, zero unexplained differences, and a non-empty justification for every allowed difference.
- [x] Prohibit Strict output from depending on PowerShell internal APIs, copied expression trees, private binders, or an installed engine. The upstream implementation remains evidence, not executable compiler infrastructure.

Exit gate: **Complete within the promoted Windows profiles.** Named profiles govern target identity, binding/lowering, providers, caches, artifacts, diagnostics, compatible `#requires`, and every promoted semantic family. Schema-3 observations integrity-bind exact executable/runtime identity, reject contradictory or unbounded evidence, preserve ordered values/cardinality/streams/errors/state and original property order, explicitly distinguish selected-property `AutomationNull` from ordinary null before binding collapse, consume the versioned framed Strict protocol for nullable, multiline string, and culture-sensitive values, and record direct OS-observed child-process launches/exits with ordered invocation identity. All 31 minimized interpreted cases execute across the three immutable pinned hosts for 93 observations, and all 24 cases in the promoted runtime-free subset carry artifact-hash-bound Strict-executable evidence against the exact PowerShell 7.6.5 pin; the direct process case also executes against all three exact pins, and upstream patch changes produce review-only evidence without changing a pin.

## Milestone 19 — Qualify real dependency and interop ecosystems

This milestone consumes the Milestone 16 executable-provider/trust boundary for external Strict adapters. Hosted Hybrid fixture work may continue earlier, but it must be reported as PowerShell-hosted compatibility rather than runtime-free provider execution.

- [x] Add a hermetic clean-target runner with an intentionally empty ambient module path and package cache, followed only by restore from the reviewed dependency lock and declared feeds/local sources.
- [x] Execute a signed external binary module with a separately built transitive managed dependency through its declared hosted module boundary. The clean-target acceptance verifies exact version/GUID, process architecture, assembly load context, external help, type and format data, success/information/warning/verbose/debug/error streams, terminating and nonterminating errors, cooperative cancellation, exclusive-resource cleanup, and preserved valid Authenticode signer identity on the module DLL, dependency DLL, and nested manifest. This remains PowerShell-hosted Hybrid compatibility rather than translation of binary cmdlets into Strict CLR.
- [x] Execute a managed-wrapper adapter from a separately built and locked package in Strict and Hybrid artifacts without compiler-source edits or assembly-name special cases; its current operation is deliberately narrow and does not substitute for directory or management providers.
- [x] Add an AD-style administration fixture in Hybrid. It preserves an installed directory-administration binary module and its transitive requirements as an explicit runtime route. Strict LDAP support uses a separately versioned executable directory-service provider with typed operations; arbitrary binary cmdlets are not translated from PowerShell source.
- [x] Execute the complete typed LDAP operation family through compiler-generated Strict artifacts using the release-wired `PowerForge.PowerShell.Provider.Directory.Runtime` package. Search, exact-DN read, add, modify, true/false compare, rename, delete, and post-delete absence execute against a caller-declared mutable test location; read-only RootDSE, bounded paged-search state cleanup, and reusable provider-created session behavior execute separately. Early page termination sends the [RFC 2696](https://www.rfc-editor.org/rfc/rfc2696) size-zero cleanup request and closes the connection if the server rejects it. Sessions are opaque values created by the provider under the qualified `win-x64`, LDAP port 389, and Negotiate profile; arbitrary caller-created LDAP connections cannot bypass it. Other transports, authentication modes, explicit credentials, referral chasing, and alternate ports fail closed until independently qualified. The acceptance uses one GUID-named generic entry, deletes both possible DNs in cleanup, and verifies no temporary entry remains.
- [x] Bound the Windows Negotiate provider's synchronous initial bind behind the ABI-5 process-isolated initialization contract. The seven executable LDAP contracts declare a 45-second manifest deadline; the generated Strict executable creates one-use inherited anonymous-pipe capabilities, emits only entrypoint-reachable provider IDs, exchanges length-prefixed bounded scalar string frames, keeps the direct worker alive until its response is consumed, then kills the complete tree on success, deadline, or caller cancellation. Direct command-line worker entry and unreferenced package operations fail closed, former frame-marker text round-trips as ordinary payload, and a non-cooperative blocked provider releases its exclusive handle. The ordinary reusable in-process adapter still documents `PostInitializationCooperative` because `System.DirectoryServices.Protocols` exposes a synchronous Windows bind; only compiler-generated Strict executables receive the stronger isolated guarantee. Microsoft's [`ldap_bind_s` contract](https://learn.microsoft.com/en-us/windows/win32/api/winldap/nf-winldap-ldap_bind_s) remains the platform-boundary evidence.
- [x] Add a Windows Management Instrumentation v1 Hybrid fixture for the Windows PowerShell 5.1 profile. Because the WMI v1 cmdlets are absent from PowerShell 7, it remains an explicit Windows PowerShell hosted boundary. A future Strict replacement must use a separately versioned executable management provider and must not claim binary compatibility with removed WMI cmdlets.
- [x] Define a typed CIM/MI operation contract for query, enumerate, get, create, modify, delete, method invocation, association, and indication subscription, including namespace/class/query, local/remote target, transport, session, authentication, timeout/throttle, shaping, errors, cancellation, and deterministic cleanup. A direct C# local query proves the adapter foundation; it does not prove arbitrary `Get-CimInstance` lowering.
- [x] Execute the complete typed CIM/MI operation family through compiler-generated Strict artifacts. Query, enumerate, exact-key get, create, modify, delete, method invocation, association traversal, and bounded indication subscription run through the release-wired `PowerForge.PowerShell.Provider.Management.Runtime` package; remote WS-Man/DCOM and reusable caller-owned session behavior execute on a declared Windows target.
- [x] Remove unimplemented `Certificate` authentication from the advertised CIM contract; reject undefined authentication, missing credentials for explicit authentication, caller-session conflicts, and transport/authentication combinations that the adapter cannot honor.
- [x] Preserve CDXML through declared Hybrid behavior and deterministic metadata/dependency planning without contacting a target. Runtime-free CDXML binding remains blocked on the executable CIM provider; custom adapters, dynamic metadata, unsupported output shaping, and session-dependent discovery remain hosted or rejected.
- [x] Execute the non-destructive local and remote management matrix against declared Windows targets: missing class/namespace, invalid runtime credentials, unreachable host, protocol/authentication mismatch, disposed-session disconnect, exact result limiting, bounded subscription, cancellation, session ownership, and cleanup. The official [CIM session contract](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_cimsession) and [PowerShell 5.1 versus 7 WMI differences](https://learn.microsoft.com/en-us/powershell/scripting/whats-new/differences-from-windows-powershell) remain profile evidence rather than substitutes for these target-host tests.
- [ ] Run the destructive reboot/reconnect case on an explicitly disposable Windows management target. The product and test harness must not reboot an ambient runner merely to turn this checkbox green; record disconnect, bounded failure, reconnect, and post-reconnect cleanup only when a target is deliberately placed under the acceptance run's lifecycle authority.
- [x] Execute native-library and child-process adapters on each currently claimed target, including bitness/ABI, argument and environment encoding, standard streams, exit/error mapping, cancellation, child-tree cleanup, deployment closure, and missing/incompatible asset rejection.
- [x] Execute an explicit Windows COM route across the currently supported local profile with declared identity, activation policy, marshalling, error mapping, RCW/object lifetime, cleanup, and registration absence behavior. COM remains Windows-only unless a different provider contract is deliberately created.
- [x] Keep CDXML/CIM, implicit remoting, dynamic module discovery, and uninspectable provider behavior hosted or rejected until each has an explicit provider and target contract.
- [x] Bind authoritative provider/module publisher, signer trust, canonical package/license identity, redistributability, package hash, managed assemblies, RID-specific native assets, transitive dependencies, and target restrictions into the reviewed lock, SBOM, provenance, and final artifact disposition. Selected native assets are metadata-checked, parsed as PE/ELF/Mach-O, architecture-checked, exact-hash locked, RID-filtered, collision-checked, and required to close their own native imports before delivery certification. Exact platform loader-name probing rejects arbitrary suffixes, and the resulting format/architecture/import evidence is locked and recorded without loading provider code into the compiler; self-declared metadata alone receives no trust credit.
- [x] Prove missing ambient dependencies, wrong versions, spoofed identities, case collisions, malformed packages, incompatible native assets, and untrusted providers fail before publication.

Exit gate: **Partial at one authorized external boundary.** The complete typed CIM/MI and LDAP operation families execute through compiler-generated Strict artifacts without a PowerShell runtime. The release-wired management package carries three exact managed assemblies and one parsed, architecture-checked native bridge; the release-wired directory package carries its exact adapter and LDAP protocol assembly plus the reviewed Windows LDAP ABI. Both packages flow through lock, provenance, SBOM, publication disposition, native-import closure, and delivered-closure verification. Management proves local/remote operation, reusable caller-session ownership, bounded results, failure, cancellation, disconnect, and cleanup. Directory qualification proves the explicitly advertised `win-x64` LDAP/Negotiate profile, read-only integrated access, bounded paging-state release, reusable provider-created sessions, every mutation operation, true/false compare, rename, deletion, and verified cleanup in a caller-declared test location. ABI 5 confines synchronous initialization to a deadline-bound child process and terminates its tree on timeout or cancellation; the direct in-process adapter remains honestly post-initialization cooperative. Unqualified LDAP transport/authentication modes and arbitrary caller-created connections fail closed instead of inheriting credit from the proven profile. WMI v1, CDXML, and arbitrary directory cmdlets remain explicit hosted/profile contracts rather than automatic translation. Completion now requires only the authorized reboot/reconnect exercise on a disposable Windows management target; it is not inferred from metadata or replaced by rebooting an ambient runner.

## Milestone 20 — Establish generic measurement and expand useful coverage continuously

Milestone 13 proved the mechanism. This milestone turns it into a maintained compatibility program without making Evotec modules or any public product into compiler intrinsics.

- [x] Commit a redistributable public-corpus manifest with repository URL, license, exact revision, content hash, selected entrypoints, scenario family, and expected restore policy. Keep source payloads external when licenses or size make vendoring inappropriate.
- [x] Cover at least compute/control flow, object/collection/ETS, filesystem, REST/JSON/CSV, module orchestration, directory/management administration including WMI/CIM/CDXML, native/process, and cross-platform script families. Private inputs may remain an additional regression lane, never compiler configuration.
- [x] Publish analyzed, bound, emitted CLR, exported cmdlet, hosted region, retained source, semantic fallback, shaping fallback, runtime-routed, omitted, rejected, and complete-Strict-program counts from the one final disposition ledger.
- [x] Rank the frontier by generic value: sole blocker, units and complete programs unlocked, frequency across unrelated inputs, semantic risk, provider dependency, target reach, and measured performance value. Do not prioritize by module name or by the easiest percentage increase.
- [x] Require every accepted feature wave to pass the Milestone 18 oracle, Strict rejection/Hybrid fallback tests, generated artifact execution, source/failure mapping, dependency closure, and target-profile gates before refreshing the corpus baseline.
- [x] Add differential fuzzing and minimized regression retention for parser/binder crashes, nondeterminism, incorrect acceptance, unbounded diagnostics, source-root escapes, and semantic mismatches.
- [x] Establish an initial product packet, fixed before implementation, containing at least ten unrelated public modules across five scenario families that analyze and complete clean-target Hybrid build/import/invocation, plus three fixed standalone Strict fixtures executed on two supported RIDs.
- [x] Keep a second exact-hash external assessment packet for coverage-frontier inputs that are not acceptance candidates yet. Its workload metadata may name public sources, but compiler decisions remain based only on syntax, bound semantics, providers, target contracts, and resource policy.
- [x] Report per-scenario progress and complete-program outcomes. Never describe emitted functions divided by authored functions as “percentage of the PowerShell language” or a probability that an arbitrary module works.
- [x] Make the public packet runner consume and enforce its checked-in baseline, including exact packet identity, complete input aggregates, post-emission disposition, clean invocation evidence, target-host Strict outcomes, and no-loss/no-regression rules; filtered or skipped lanes are identity-only and cannot satisfy a full baseline.
- [x] Make zero-success public packet lanes produce bounded failure and regression evidence under strict mode instead of crashing through empty-collection property enumeration.
- [x] Consolidate HTTPS/archive acquisition, path normalization, collision detection, link rejection, size/count/compression bounds, hash verification, process execution, and offline replay behind one shared harness owner used by both public and external packets.
- [x] Add a fixed Hybrid benefit packet that emits and invokes compiled commands from at least three unrelated scenario families. Continue to require safe retention for everything unsupported, but do not count a 0%-emission module as typed-compilation benefit.
- [x] Add one realistic four-file Strict application beyond the small compiler fixtures; compile all 11 methods and match direct execution on `win-x64` and `linux-x64` under the enforced baseline.
- [x] Extend the Strict application packet with dependency, error, stream, resource, startup, and performance evidence on every advertised target.

Exit gate: **Complete within the fixed bounded packets.** Ten unrelated public modules pass baseline-gated clean-target Hybrid build/import/invocation with all 735 units safely retained or runtime-routed. The exact external benefit packet invokes 4/4 emitted commands from four unrelated workloads across three scenario families with original/generated parity. Four Strict programs totaling 18 emitted units execute on `win-x64` and `linux-x64`; the four-file/11-method application additionally consumes a hash-verified delivered resource, proves its four-source-file reviewed dependency closure, exact success streams, and bounded failure contract, then records six alternating fresh-process samples after one excluded warmup per surface. The checked-in per-RID baseline enforces those identities, hashes, exits, sampling rules, and conservative timing budgets. In the final combined Windows packet its compiled median was 101 ms versus 790 ms through `pwsh`; the pinned-SDK Linux replay recorded 26 ms versus 233 ms. Those timings are packet evidence, not universal performance promises.

Frontier snapshot on 2026-09-01: the separate seven-workload packet acquired and analyzed 155 source files with zero parser-error files. Profile-compatible `#requires`, nullable CLR value-member reads, stable scalar stream-message interpolation, adjacent control-flow closure, the additional immutable utility workload, and Boolean command discovery raise post-emission coverage to 9/196 units (4.59%) and 9/183 functions (4.92%). The exact baseline enforces that result. Generated binary modules bind `Get-Command` only when one name plus `-ErrorAction Ignore` or `SilentlyContinue` is consumed as a Boolean availability test. The provider routes a constant, argument-bound script through the canonical hosted-command capture delegate so the current PowerShell host retains exact-name autoload, module qualification, wildcard matching, and the different `$Error` effects of `Ignore` and `SilentlyContinue`. It records that hosted contract and remains unavailable to Strict runtime-free targets; general `CommandInfo` output and wider parameters still fail closed. This raises typed CLR emission by one unit/function but does not reduce the runtime-fallback count because the emitted method intentionally retains one hosted boundary. Bounded local begin/process/end lifecycle calls accept an exact one-dimensional stable-scalar array literal, local, or parameter. A null typed array supplies one process record after exact by-value parameter conversion, an empty array supplies zero, and begin/end each run once per invocation; types with binding-error null semantics fail closed. The lifecycle binder also accepts homogeneous stable-scalar success output at top level or inside nested `if`/`elseif`/`else` branches in `process`, materializes the records actually selected plus the terminal `end` record in authored order, and exposes the result through a typed collection ABI. The output-producing slice is proven against Windows PowerShell 5.1, pinned PowerShell 7.4.19 and 7.6.5, and runtime-free .NET 8/.NET 10 artifacts. Output-producing `begin`, loop/switch/try-nested process output, heterogeneous or array-valued process output, process return/break/continue control flow, and wider pipeline shapes still fail closed. Generated binary-module hosts now preserve an untyped parameter as an object-valued binding contract behind a dedicated capability; Strict runtime-free entry points still reject it, and dynamic member or invoke-member behavior remains unsupported. This generic slice removes the previous 32 `parameter.type` occurrences across 23 units and five workloads, but none was a visible sole blocker. Complete-workload execution remains 0/7 because the assessment lane is census-only; the separate opt-in qualification builds exact acquired inputs and proves 4/4 selected emitted commands across three scenario families without claiming a complete workload. The strongest cross-workload gaps remain runtime scope, bound pipeline syntax, untyped member/invoke-member expressions, broader pipeline lifecycle, missing lowering, remaining parameter contracts, and additional common command-provider shapes. Coverage work must address those semantic families generically and then pass the oracle, fallback, artifact, dependency, and target-profile gates before each baseline refresh.

The conditional lifecycle expansion changes no unit or function in the fixed external packet because those lifecycle units have additional unsupported shapes. It closes a standalone semantic and artifact-proof gap; it is not counted as a corpus-coverage gain.

Direct static CLR assignment now follows the same canonical member binder, bound IR, lowering, target-reference, and backend path as instance member assignment. It accepts only simple `=` against one public, unambiguous, target-compatible writable static property or non-literal, non-readonly static field. Compound assignment, dynamic or nonlocal receivers, ambiguous targets, and property assignment whose failure would be observed by a `RuntimeException` catch remain hosted or rejected. The canonical assignment-target oracle was expanded without adding a second assignment case; the promoted runtime-free subset retains runtime-free evidence. The selected affected external workload now reaches its next unsupported typed error-record construction boundary, so the fixed packet remains 9/196 emitted units and 9/183 emitted functions. This is a generic semantic closure with no claimed corpus-coverage gain.

Generated binary modules now also preserve language `foreach` over an authored `[array]`/`System.Array` parameter. The canonical binder assigns each element the existing object-valued host contract, bound and lowered IR record the general-array shape, and the backend enumerates the CLR `System.Array` without inspecting PowerShell syntax. Null and empty arrays produce no iterations, scalar parameter binding produces one, and heterogeneous and multidimensional arrays preserve their CLR enumeration cardinality. Strict executable entry points, untyped member access, and dynamic invocation remain unsupported. The current 31-case/93-observation exact-host matrix and real net472/net10 binary modules prove the contract. The two fixed-packet units that previously stopped at the foreach variable now expose hosted command-result typing/provider blockers, so measured emission honestly remains 9/196 units and 9/183 functions.

Null-seeded reference locals now use one conservative whole-function inference rule: the first assignment must be `$null`, every later concrete assignment must resolve to the same exact reference type, and mixed, unknown, value-type, or compound-assignment flows fail closed. Exact CLR type and definite value state remain separate: cloned optional branch, loop, switch/try, and lifecycle `process` paths merge null state conservatively before later member binding. Potentially null `Length` reads whose concrete result is not `Int32` fail closed because PowerShell's null result is an `Int32` zero. Contextual null binding then preserves eligible reference types through the existing mutation IR and C# backend. Real net472/net10 binary modules prove null, constructed, optional-branch, and zero-iteration loop paths plus deterministic cleanup; generated lifecycle evidence proves the zero-record path retains null propagation, and the current 31-case/93-observation exact-host matrix records the interpreted contract. The two fixed-packet units that previously stopped at object-to-handler type changes now reach their next delegate-property boundary, so measured emission remains 9/196 units and 9/183 functions with zero regressions.

Scalar numeric comparison follows one bounded left-directed conversion rule: when the right integral operand can widen exactly through a CLR-safe conversion to the left integral operand's static type, the binder records that conversion in bound IR before lowering. Nullable integral and decimal equality use the corresponding lifted equality. Nullable integral and decimal relational comparisons now use four explicit null-ordered bound operators; the binder normalizes one exact underlying type, lowering allocates two temporaries, and the backend evaluates each authored operand once before applying PowerShell's rule that negative numbers sort below null while zero and positive numbers sort above it. Both-null inclusive comparisons are true and strict comparisons are false. Nullable floating equality and ordering, precision-changing promotion, narrowing, nullable-right widening, arrays, and dynamic coercion remain rejected. Real net472/net8 binary modules match Windows PowerShell 5.1 and PowerShell 7 for signed integral, unsigned integral, and decimal null ordering, and the dedicated runtime-free case passes the complete 31-case/93-observation exact-host matrix plus the 24-case Strict subset against the PowerShell 7.6.5 pin. The fixed external packet remains 9/196 emitted units and 9/183 emitted functions with zero regressions; this closes a generic semantic contract without inventing a corpus-coverage gain. The frontier classifier still evaluates assignment mutation and CLR-member diagnostics before generic `validation` wording, preventing either category from being mislabeled as parameter metadata while preserving `assignment.target` for member mutation.

Post-test `do`/`while` and `do`/`until` now share the canonical loop binder, bound IR, definite-assignment pass, optimizer, lowering, and backend. The body executes before the condition, `continue` transfers to the post-test condition, `break` exits the loop, and `until` negates the authored Boolean condition at lowering. A first-iteration assignment is available to the condition and loop exit only when branch merging proves it and no `break`, `continue`, `return`, or `throw` can bypass that flow; transfer-heavy bodies retain conservative state. Constant post-test loops remain represented as loops so an optimizer cannot hoist loop-control statements outside their valid target. Real net472/net8 binary modules match Windows PowerShell 5.1 and PowerShell 7 for zero- and above-threshold starts, body-first assignment, `continue`, `break`, and constant-condition execution; labeled loop control still fails closed. A dedicated post-test-loop semantic family, native case, upstream provenance, and exact host pins expand the canonical matrix to 27 cases and 81 interpreted observations and the promoted runtime-free subset to 21 cases. The fixed external packet contains five post-test syntax occurrences, but those units retain additional dynamic pipeline, scope, member, or provider blockers, so this generic closure does not claim an emission increase.

Short-circuit logical binding now clones symbol state only for the right operand and marks one direct reference variable non-null when reaching that operand proves it: `$null -ne $value` under `-and`, or `$null -eq $value` under `-or`, including the safe reversed form only for a PowerShell-scalar string or a statically sealed non-enumerable scalar. Null equality has an explicit bound operator and emits `Object.ReferenceEquals`, so user-defined C# equality cannot manufacture a non-null proof. Collection-left values, unsealed reference types, enumerable value types including nullable wrappers, opposite predicates, and wider/dynamic conditions do not refine the right operand. They remain hosted or rejected unless the complete right-hand expression has an independent bounded contract; nullable integral/decimal member comparisons now provide one such runtime-free path for opposite predicates without manufacturing non-null state. Runtime-free libraries execute null, positive, zero, reversed, `-and`, and `-or` paths across net472/net8/net10; the 31-case/93-observation exact-host matrix and 24-case Strict subset include a dedicated minimized case. The fixed external packet retains the non-null refinement and now clears that unit's construction diagnostic too, but independent blockers keep measured emission at 9/196 units and 9/183 functions with zero regressions.

Bounded `New-Object` CLR construction now resolves through one compiler-owned command-family contract and normalizes into the same exact constructor selection, bound invocation IR, lowering, and C# backend used by `[Type]::new(...)`. The accepted shape requires a literal `TypeName`, no redirection or splatting, and either no arguments, one closed scalar literal, or one parenthesized list of closed scalar literals; module qualification and the documented `Args` alias are accepted. `Property`, `ComObject`, `Strict`, dynamic type names, variable or splatted argument lists, nested/empty arrays, array-as-one-argument ambiguity, unsupported target types, non-exact overloads, and runtime-error wrapping remain hosted or rejected. Cross-target net472/net8/net10 libraries, the 31-case/93-observation exact-host matrix, and the 24-case Strict subset prove the bounded contract. The safe literal slice removes 9 of the former 25 `command.new-object` occurrences; 16 wider occurrences remain across four external workloads instead of being misclassified as runtime-free. Independent runtime-scope, lifecycle, pipeline, parameter, filesystem/provider, and member blockers keep the honest packet at 9/196 emitted units and 9/183 emitted functions.

Exact `System.Diagnostics.CodeAnalysis.SuppressMessageAttribute` declarations now have one compile-time-only metadata owner shared by structural analysis and semantic validation. The bounded shape requires the real framework attribute on a script or function parameter block, exactly two literal string constructor arguments, and only literal `Justification`, `MessageId`, `Scope`, or `Target` properties; it is deliberately omitted from the runtime ABI. Dynamic or malformed arguments, unknown or repeated properties, parameter-level placement, and unresolved lookalikes fail closed. A real Strict executable and the existing parameter-metadata oracle prove that suppressions do not change execution or output. The fixed external packet drops from 45 to 34 `parameter.metadata` occurrences and from 38 to 31 affected units while staying at 9/196 emitted units and 9/183 emitted functions with zero regressions, so this is a generic semantic closure rather than a claimed coverage increase.

Standalone `[void] expression` now binds as a statement-level discard rather than a general conversion to `System.Void`. The accepted shape is exactly one single-element statement pipeline whose operand already has a typed contract; return, assignment, expression-container, multi-stage pipeline, and unsupported-operand forms still fail closed. The binder preserves operand evaluation, lowering carries explicit discard intent, and one collision-free generic backend helper owns both `[void]` and value-returning `$null = expression` without emitting success output. Bare values, nested control flow, side-effecting member calls, net472/net8/net10 artifacts, Windows PowerShell 5.1, pinned PowerShell 7.4/7.6, and the complete 31-case/93-observation matrix prove the boundary. The fixed external packet removes one of six `expression.conversion` occurrences and one of six affected units while remaining at 9/196 emitted units and 9/183 emitted functions with zero regressions; independent blockers prevent an emission increase.

Exact static enum-member parameter defaults now use the canonical target-typed literal owner rather than evaluating a general PowerShell expression. The enum type must exactly match the parameter's enum or nullable-enum type, optional parentheses may contain only the single member expression, and one case-insensitive public literal member must resolve from the selected target framework's reference metadata. Its target-metadata value becomes the portable numeric ABI, so a member available only to the compiler's newer host framework fails closed for an older target. Mismatched types, missing members, non-enum static properties, method calls, and wider runtime defaults remain fallback. Generated binary-cmdlet omitted/explicit behavior, explicit net472 rejection of a newer-only member, net472/net8/net10 runtime-free libraries, Windows PowerShell 5.1, pinned PowerShell 7.4/7.6, and the complete 31-case/93-observation matrix prove the boundary. The fixed external packet drops from 8 to 6 `parameter.default` occurrences, from 7 to 5 affected units, and from 4 to 3 affected workloads while staying at 9/196 emitted units and 9/183 emitted functions with zero regressions; independent blockers still prevent an emission increase.

The exact read-only `$ExecutionContext.SessionState.LanguageMode` chain now uses the existing runtime-state classification, bound/lowered IR, and binary-host state dictionary. Generated cmdlets capture the live `PSLanguageMode` at invocation; no profile label or build-time snapshot substitutes for session state. Assignment, locally defined `$ExecutionContext`, other execution-context/session-state members, and runtime-free targets fail closed. Real net472/Windows PowerShell 5.1, net8/pinned PowerShell 7.4.19, and net10/pinned PowerShell 7.6.5 binary modules plus the expanded 31-case/93-observation exact-host matrix prove the boundary. The fixed external packet moves one unit from `runtime.scope` to its independent enum/string comparison blocker: runtime-scope occurrences fall from 192 to 191, affected units from 56 to 55, and visible sole blockers from 5 to 4, while emission stays at 9/196 units and 9/183 functions with zero regressions.

Hosted binary modules now preserve scalar enum-left/string-right equality for `-eq`, `-ne`, `-ceq`, and `-cne` through PowerShell's public `LanguagePrimitives.Equals` contract. The bound operator owns the required language-runtime capability, so optimizer reconstruction cannot silently turn hosted semantics into a runtime-free claim. Reverse operand order, relational operators, arrays, and object-typed right operands remain rejected or hosted. Real net472/Windows PowerShell 5.1, net8/PowerShell 7, pinned PowerShell 7.4.19 and 7.6.5 execution, and the complete 31-case/93-observation matrix cover case-insensitive, case-sensitive, numeric-text, invalid-text, and inequality behavior. The fixed external packet remains at 9/196 emitted units and 9/183 emitted functions with zero regressions; `syntax.unsupported` falls from 106 to 105 occurrences, from 40 to 39 affected units, and from 6 to 5 visible sole blockers, but the affected function still has an independent hosted command boundary.

## Milestone 21 — Productize project, lock, restore, and package UX

This is the toolchain/ecosystem part of “Python territory”: users need a reproducible environment and artifact workflow, not only a compiler API.

- [x] Define one portable project manifest over the existing target contract, source/resource policy, semantic profile, provider references, dependency lock, artifact matrix, ABI baseline, and diagnostic/IR policy. PowerShell DSL, CLI, and future Studio surfaces map to this model rather than inventing separate configuration brains.
- [x] Provide a coherent `init`/`analyze`/`explain`/`recommend`/`lock`/`restore`/`build`/`test`/`pack`/`install`/`diagnose` workflow, reusing existing commands where they already own the behavior. A repository-specific wrapper should normally only select the project and credentials/publish gate.
- [x] Restore modules, providers, managed/native assets, SDK/runtime packs, and compiler tools into a content-addressed isolated environment. Ambient `PSModulePath`, loaded assemblies, user profiles, and global caches must not change the resolved graph.
- [x] Support offline locked restore after an explicit acquisition step and produce actionable evidence for missing, incompatible, untrusted, or integrity-invalid dependencies.
- [x] Authenticate restored archive content against NuGet's canonical closure identity, verify extracted bytes against that archive, and make the generated compilation project consume the selected target `packages.lock.json` in locked mode with exact actual-closure reconciliation.
- [x] Define wheel-like qualified artifact variants by semantic profile, artifact kind, TFM, RID, architecture, and deployment model. Selection fails closed instead of silently loading a nearby binary.
- [x] Carry the manifest's semantic profile into the actual compilation target, semantic decisions, hashes, caches, provider selection, package variant, and diagnostics; reject any profile that is recognized only as a label.
- [x] Pack source, binary-module, CLR-library, managed EXE, and NativeAOT outputs with normalized manifests, SBOM, provenance, signatures, ABI/semantic identities, and exact dependency requirements appropriate to each artifact.
- [x] Derive project-local installation identity as a full 256-bit Base64URL SHA-256 over the complete authenticated artifact-set hash and qualified target-contract identity. This avoids both primary-artifact collisions and Windows path inflation from two nested hex digests.
- [ ] Publish versioned compiler/core/CLI/PowerShell/provider packages through an explicitly authorized release lane, then prove install, upgrade, rollback, and clean-consumer usage from public feeds. Source/branch proof remains distinct from public-package availability.
- [x] Define compatibility and deprecation policy for project, target-contract, dependency-lock, provider, manifest, explain/diagnose, cache, and public ABI schemas before a stable toolchain release.

Exit gate: **Partial / local workflow complete.** From a clean project, the generic CLI restores the exact reviewed environment online or offline, authenticates package archives and extracted payloads, consumes the target closure lock during generated compilation, applies the selected semantic profile, analyzes, recommends, builds, tests, packs, installs by complete target/artifact-set identity, and diagnoses drift without repository-specific resolution. Only public publish/install/upgrade/rollback proof remains open and unauthorized.

## Milestone 22 — Broaden platform and profile-guided performance maturity

- [ ] Add target-host execution lanes for the next explicitly selected Windows, Linux, and macOS x64/Arm64 profiles. Support is promoted one RID/deployment profile at a time; cross-publish remains experimental.
- [x] Pin and exercise one supported SDK-selection path for build, test, MSBuild task-host, and benchmark evidence. SDK 10.0.303 is selected with roll-forward disabled; the normal solution, compiler/project workflow, generated MSBuild, and recorded ReadyToRun benchmark paths consume that selection rather than an ambient preview or `dotnet exec` workaround.
- [x] Turn Hybrid boundary profiling into an opt-in recommendation workflow that identifies hot eligible regions, crossing cost, and coarse-boundary candidates without changing source or artifact mode automatically.
- [x] Add further immutable IR rewrites, allocation reduction, pipeline fusion, loop specialization, conversion caching, and mapping only when a named corpus/benchmark exposes a cost and semantic/source-map proof remains intact.
- [x] Evaluate ReadyToRun, trimming, NativeAOT, composite artifacts, and profile-guided optimization per scenario. Do not treat one backend as universally faster or smaller.
- [x] Keep readable generated C# as the canonical lowering. Consider a direct IL/native backend only if measured Roslyn/MSBuild/code-generation limits block an accepted product requirement and the new backend consumes the same lowered IR, source maps, ABI, and evidence contracts.
- [x] Add cold/warm startup, throughput, allocation, working-set, boundary, build-time, artifact-size, import, and clean-target execution budgets for the public packet. Record regressions separately from language-coverage changes.
- [x] Define the support matrix, preview/stable channels, security and compatibility response policy, release cadence, and downstream pilot evidence required before a 1.0-quality claim.

Exit gate: **Partial.** The support matrix advertises only the already target-proven `win-x64` and `linux-x64` Strict profiles, keeps macOS/Arm64 and additional deployment profiles experimental, and exposes measured opt-in recommendations plus explicit performance budgets. Physical macOS/Arm64 promotion and a stable-channel release remain separate qualification work.

## Next implementation order

The canonical semantic pipeline is established, but its profile, provider, oracle, and benefit gates must close before broad release or platform expansion:

1. [x] **Integrate and rebaseline:** integrate `origin/main`, pin SDK 10.0.303 without roll-forward, and rerun the focused compiler, oracle/provider/project, public/external packet, multi-TFM, line-size, and artifact gates on the resulting head.
2. [x] **Close one-brain correctness:** effective profiles, canonical NuGet/signer trust, provider ABI 5, shared corpus acquisition, responsibility splits, structured exact-host per-feature oracles, full bounded provider conformance, Strict NativeAOT adapter execution, one representative external filesystem operation, and closed process-isolated provider initialization are complete.
3. [ ] **Run value-ranked semantic waves:** compatible `#requires`, bounded read-only process/user/culture state, nullable CLR value-member reads, null-seeded exact reference-local inference, exact left-directed integral comparison widening plus nullable integral/decimal equality and sign-sensitive relational ordering, bounded hosted enum-left/string-right equality, bounded direct-null short-circuit refinement, pre-test and post-test loops with unlabeled loop control, stable scalar stream-message interpolation, terminal `try`/`catch` output, the first exact typed invoke-member expansion, nullable one-dimensional stable-scalar array-variable input for bounded `ForEach-Object` assignment pipelines, exact typed-array literal/local/parameter input for bounded local begin/process/end lifecycle calls, homogeneous top-level and conditional stable-scalar `process` output, binary-module-only untyped object parameters and `[array]`/`System.Array` foreach enumeration, Boolean-only `Get-Command` availability, advisory `[OutputType([void])]` and single resolved binary-command output-type-name metadata, exact direct static CLR assignment, and bounded literal `New-Object` CLR construction are complete; broad dynamic scope, labeled loop control, unresolved/dynamic/multiple output-type metadata, and untyped object members remain intentionally runtime-backed. Numeric comparison widens only a right integral operand whose value range is preserved by the left integral type. Nullable integral/decimal equality uses CLR lifting, and relational ordering uses dedicated single-evaluation bound operators with PowerShell's negative/null/nonnegative order; nullable floating equality and ordering, precision-changing promotion, narrowing, nullable-right widening, and dynamic coercion remain rejected. Direct null predicates refine one right-operand reference only when `-and`/`-or` evaluation proves it non-null; the safe reversed form additionally requires a PowerShell-scalar string or a sealed non-enumerable scalar after nullable unwrapping. Post-test loops preserve body-before-condition order; `continue` evaluates the post-test condition, and constant post-test forms stay as loops so control flow cannot escape during optimization. The untyped parameter contract has its own target capability, preserves scalar/null/array/object inputs through a real generated binary module, and is not process-bindable or available to Strict runtime-free entry points. Constructed `List<T>` uses normalized open-generic target-reference identities for exact construction, member invocation, and reads, including null `Count` behavior; unrelated generic definitions, generic methods, dynamic receivers, and non-exact overloads still fail closed. The command-discovery provider accepts exactly one name and an explicit `Ignore` or `SilentlyContinue` action, preserves exact/module-qualified autoload, wildcard lookup, and `$Error` behavior through the current PowerShell host, and returns only a Boolean; command metadata and Strict runtime-free discovery remain unsupported. Both bounded pipeline slices preserve the profiled distinction between a null typed array, which contributes one converted record, and an empty array, which contributes none; binding-error null conversions remain hosted or rejected. The assignment slice continues to reject scalar/object-array input, process output, and process control flow. The lifecycle slice continues to reject scalar or mismatched input, dynamic/provider collections, redirection, extra command arguments, output-producing `begin`, loop/switch/try-nested output, heterogeneous or array-valued process output, process return/break/continue control flow, `clean`/`dynamicparam`, and wider lifecycle signatures. Continue with remaining parameter contracts, wider PowerShell numeric promotion, additional common provider shapes, and additional bounded process control-flow families. Every change must use parser → binder → bound IR → lowering → backend and the exact profile/oracle path. The external packet remains 9/196 units and 9/183 functions. The metadata-name slice removes three formerly unsupported `OutputType` occurrences from the fixed packet but unlocks no unit because those functions have independent blockers; it therefore claims semantic and artifact preservation, not a coverage increase. Short-circuit refinement removes the visible nullable-relation blocker, and bounded literal `New-Object` removes 9 of 25 construction occurrences while leaving 16 wider occurrences across four external workloads safely hosted or rejected; independent blockers still prevent an emission increase. The new unit remains runtime-routed, invoke-member diagnostics fell from 9 to 7 occurrences and from 6 to 4 affected units, and the 32 former `parameter.type` occurrences across 23 units and five workloads are gone. Boolean command discovery removed one visible sole blocker; static assignment and post-test loops reach their next independent blockers, and two null-seeded handler locals reach their next delegate-property boundary, without changing packet totals. Hosted enum/string equality moves the language-mode unit to its next independent hosted-command boundary and reduces `syntax.unsupported` from 106 to 105 occurrences without changing emitted totals. The corrected classifier no longer mistakes a CLR member whose name contains `Validation` for parameter metadata and preserves member mutation as `assignment.target`. Semantic breadth, CLR emission, and runtime independence remain separate claims.
4. [x] **Prove user benefit:** the exact packet invokes 4/4 emitted commands across three scenario families, and the realistic Windows/Linux Strict application proves its reviewed dependency, resource, exact-success/bounded-failure streams, and six-sample counterbalanced startup contracts. Safe fallback remains required, and 0%-emission compatibility is not counted as typed benefit.
5. [ ] **Qualify ecosystems:** the signed external-module/wrapper plus executable CIM and directory operation families, including bounded process-isolated directory initialization, are complete through the same provider and clean-target contracts; finish the controlled management-target reboot/reconnect acceptance case.
6. [ ] **Release only with authority:** after useful-benefit and clean-consumer gates pass, use an explicitly authorized lane to publish and prove compiler/core/CLI/provider install, upgrade, rollback, and clean-consumer use. No publication is authorized by this roadmap work.
7. **Promote platforms last:** select one experimental RID/deployment profile at a time and add physical target-host semantic, install, and performance proof before advertising it.

Do not translate syntax nodes directly in an emitter, clone PowerShell's internal compiler, add product-specific provider branches, or infer support from cross-publish alone.

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
| WMI/CIM/CDXML management | WMI v1 hosted, CIM/MI hosted and provider, CDXML metadata, local and remote Windows targets | query/method/mutation/indication behavior, object shaping, streams/errors, WS-Man/DCOM, credentials redaction, session lifetime, cancellation, cleanup | Exact named profile and target-host parity; unavailable platforms/transports or unsupported dynamic metadata fail before publication or remain explicitly hosted |
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
- [x] Split `PowerShellBoundCSharpBackend.cs` by named backend responsibilities before the next semantic wave; its central owner is now 708 lines, with control flow and other renderers in focused partials.
- [x] Split the actively growing artifact builder, hardening tests, and module-plan coordinator by responsibility before the next production feature wave. Diagnostics/evidence publication, final assembly orchestration, hardening contract groups, and module-plan segment aggregation now have named owners with growth headroom.
- [x] Split `ModuleBootstrapperGenerator.cs` by semantic responsibility; its central owner is now 902 lines, with assembly-load-context generation and other responsibilities in focused partials.

## Core semantic-pipeline gate — Complete; extensibility/profile closure — Partial

The parser-to-artifact semantic pipeline is canonical. The broader redesign is complete only when all of the following are true:

- [x] no backend consumes PowerShell AST;
- [x] no emitter performs semantic type or effect inference;
- [x] no census code independently decides compiler eligibility;
- [x] no command behavior depends on registration order;
- [x] analysis does not execute/import source modules or activate native/COM dependencies to discover semantics;
- [x] all current artifact behavior, including hosted lifecycle discovery and binding, runs through the canonical front-end/IR boundary;
- [x] migrated direct AST-to-C# paths are deleted;
- [x] the focused compiler suite passes on the integrated branch candidate;
- [x] established applicable PowerShell 5.1 and PowerShell 7 differential lanes in that suite pass;
- [x] hosted lifecycle has an explicit PowerShell 5.1/7 version-capability matrix and differential proof;
- [x] net472, net8.0, and net10.0 builds remain warning-free on the integrated branch candidate;
- [x] each artifact records one normalized, integrity-bound semantic/execution/deployment target contract from Milestone 14; compatibility fields may construct that contract, but artifact generation and evidence consume the contract;
- [x] Strict publication fails when delivered dependency closure cannot mechanically exclude PowerShell runtime, source fallback, or missing non-framework managed references;
- [x] every emitted runtime-free helper has one versioned owner and is trim/NativeAOT clean, or the artifact explicitly records that no support substrate is present;
- [x] generated CLR libraries carry a normalized public ABI map with bound null/value/cardinality and success-stream semantics and pass clean-consumer tests;
- [x] semantic, dependency, and deployment graphs share stable identities and one reviewed lock that the build requires, consumes, and verifies; explicit development opt-out is recorded as unreviewed;
- [x] every currently discovered required module, assembly, native asset, resource, process, and equivalent COM activation capability has one explicit artifact disposition;
- [x] Hybrid/fallback diagnostics retain the causal function-command-module-dependency chain and boundary contract in the final unit-disposition ledger, with unit-local dependency attribution and separate delivery-wide causes;
- [x] every named supported NativeAOT RID has target-host execution proof; unproved RIDs remain experimental;
- [x] generated source, ABI, dependencies, target contract, SBOM, artifact hashes, build inputs, and toolchain evidence have provenance bound to the consumed reviewed lock and explicit target contract;
- [x] generated artifacts preserve invocation, export, help, source-map, fallback, original pipeline-input, and synchronized lifecycle-cleanup contracts in the supported profiles;
- [x] touched non-generated production/test files in the compilation path stay below the 1,000-line hard ceiling; `ModuleBootstrapperGenerator.cs` is 902 lines after extracting assembly-load-context generation, with its other responsibilities already divided across focused partials;
- [x] active compiler owners have responsibility-based headroom for planned value/object/command expansion; the lowered backend owner is 708 lines after control-flow and other semantic renderers were split into responsibility-based partials;
- [x] adding an operator, syntax form, or command family has one obvious canonical semantic/lowering/backend owner and an injectable provider route where appropriate.
- [x] the selected semantic profile participates in target hashing, binding/lowering, provider resolution, caching, artifact selection, and diagnostics rather than acting only as package metadata;
- [x] a separately built, authoritatively trusted provider package can extend bounded Strict/Hybrid executable behavior without compiler-source edits, ambient discovery, authored-source execution during analysis, or a parallel eligibility path; complete provider-family conformance remains a product-maturity gate;
- [x] every promoted feature has structured, minimized differential evidence against an exact pinned host artifact for each claimed semantic profile;
- [x] public and external corpus acquisition and baseline enforcement share one bounded, path-safe owner.
- [x] the fixed public benefit lane invokes emitted commands across three unrelated scenario families rather than proving hosted compatibility alone.

The semantic compiler does not have a competing AST/emitter eligibility brain. Profiles are effective and exact-host proven for every promoted family, the external-provider route is executable and trust-bound, corpus policy has one owner, and the fixed benefit lane invokes emitted commands across three unrelated scenario families. The remaining product risks are the unfinished external provider ecosystems, physical target promotion, and authorized public lifecycle proof. Those gaps must close through the existing owners rather than creating parallel behavior as coverage expands.

## Product maturity gate — Partial / Milestones 16 and 18–22

- [x] every promoted semantic feature has a named, effective, exact-host, differentially proven profile under Milestone 18;
- [ ] Milestone 19's signed external binary-module/managed-wrapper, executable CIM and LDAP providers, process-isolated LDAP initialization, safe remote operation/failure matrices, bounded Hybrid WMI/CIM/CDXML, native/process, and Windows COM routes pass; only authorized disposable-target reboot/reconnect evidence remains;
- [x] the fixed public-corpus and Strict-program packet enforces its baseline, and opt-in external qualification invokes 4/4 emitted commands across three scenario families without product-specific compiler paths;
- [x] the fixed three-family Hybrid benefit packet and the Strict application's dependency/error/stream/resource/performance matrix are complete under Milestone 20;
- [x] clean-project restore/build/test/pack/install and tamper diagnosis pass locally under Milestone 21 with effective profile selection and complete artifact-set install identity;
- [ ] public-package publish/install/upgrade/rollback proof remains open under Milestone 21;
- [x] every currently advertised OS/architecture/deployment profile has Milestone 22 target-host and performance evidence;
- [ ] physical macOS/Arm64 promotion and a stable-channel release remain open under Milestone 22.

Broad percentage growth is useful only as a diagnostic trend. It never substitutes for these complete-program, clean-target, semantic, ecosystem, and product gates.

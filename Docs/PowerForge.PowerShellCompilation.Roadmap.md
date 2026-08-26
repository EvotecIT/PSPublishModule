# PowerShell Compilation Architecture Roadmap

Last updated: 2026-08-26

This roadmap is the execution plan for growing PowerForge PowerShell compilation without turning the analyzer, transpiler, command handling, or C# emitter into increasingly coupled catch-all components.

The current compiler works and proves the product direction. The next step is not another large syntax wave. The next step is to establish a typed semantic architecture that makes later coverage work smaller, deterministic, and easier to verify.

The companion [PowerShell Compilation guide](PowerForge.PowerShellCompilation.md) documents current behavior, artifact modes, supported syntax, measured performance, and distribution limits. This file tracks future architecture and implementation work.

This roadmap does not schedule a package, gallery, NuGet, or GitHub release. Those remain separate decisions after source work is complete.

## Status legend

- `[x]` complete and proven by current source or executable evidence
- `[ ]` required work
- **Current** identifies the milestone that should receive implementation effort now
- A milestone is complete only when its exit gate is satisfied

## Current position

The compiler already provides:

- [x] Package, Strict, BinaryModule, Hybrid module, and CLR library artifact paths
- [x] post-emission typed/fallback coverage and source-fingerprint baselines
- [x] capability-aware parameter contracts and supported validation metadata
- [x] omitted-versus-explicitly-bound literal defaults
- [x] typed local function graphs, conversions, operators, and control flow
- [x] bounded runtime-state intrinsics
- [x] typed code around bounded PowerShell command regions
- [x] a product-neutral acceptance corpus and replaceable real-module census inputs
- [x] PowerShell 5.1 and supported PowerShell 7 differential coverage where applicable
- [x] net472, net8.0, and net10.0 compiler build lanes

The current scaling problem is architectural rather than conceptual. Eligibility analysis, graph construction, type inference, effect discovery, fallback classification, and C# generation still inspect PowerShell ASTs in several stages. A new feature can therefore require coordinated changes in the analyzer, transpiler, emitter, command policy, diagnostics, and census.

The current emitter is physically split into partial files, but it remains one semantic owner. Adding more partial files would control line counts without removing the duplicate decisions.

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
- no touched non-generated compiler file needs to grow beyond 800 lines.

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
  - Strict executable or library
  - binary or Hybrid module
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
- runtime-state and host requirements;
- bound IR, analysis passes, and PowerShell-specific lowering;
- generated cmdlet and Hybrid boundary behavior.

### `PowerForge`

Owns host-neutral behavior where it already fits the dependency direction:

- stable public compilation plans and results;
- artifact-neutral models;
- build, filesystem, integrity, and artifact orchestration;
- reporting models that do not require SMA types.

Do not move PowerShell semantics into `PowerForge` merely to make the IR look generic. The IR is an internal PowerShell compiler model, not a speculative general compiler framework.

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
  Analysis/
    TypeFlow/
    DataFlow/
    CallGraph/
    Effects/
    Capabilities/
    Fallback/
  Lowering/
    Typed/
    Cmdlets/
    Hybrid/
    Pipelines/
  Backends/
    CSharp/
    BinaryModule/
    Executable/
  Reporting/
    Census/
    SourceMaps/
```

This is an ownership map, not permission for a folder-only rewrite. Create each area when its first real semantic slice migrates.

## Milestone summary

| Milestone | Status | Result |
| --- | --- | --- |
| 0. Freeze and inventory current contracts | **Current** | A trustworthy behavioral and ownership baseline |
| 1. Establish compiler boundaries | Planned | Parser, binder, IR, passes, lowering, and backend dependencies are explicit |
| 2. Implement foundational bound IR | Planned | Simple functions compile through IR |
| 3. Add deterministic analysis passes | Planned | Types, graph, effects, capabilities, and fallback are order-independent |
| 4. Separate lowering from emission | Planned | Backends consume lowered nodes and no longer infer semantics |
| 5. Migrate all current behavior | Planned | Existing compiler behavior runs through IR and legacy paths are deleted |
| 6. Preserve help and module contracts | Planned | Compiled functions retain full help/export behavior |
| 7. Build command and pipeline semantics | Planned | Command families have canonical, deterministic owners |
| 8. Complete advanced-function lifecycle | Planned | `begin`/`process`/`end`/`clean` and pipeline binding are modeled correctly |
| 9. Complete value and object flows | Planned | Common PowerShell object shaping becomes typed |
| 10. Expand bounded runtime state | Planned | More real helpers compile without accepting arbitrary dynamic scope |
| 11. Run generic coverage waves | Planned | Coverage rises through semantic families, not product special cases |
| 12. Optimize proven IR | Planned | Performance work follows semantic stability |

## Milestone 0 — Freeze and inventory current contracts

- [ ] Freeze broad language and command feature additions until the IR migration gate is met.
- [ ] Record the current compiler-filtered test count and exact command.
- [ ] Record generic-corpus artifact and census evidence.
- [ ] Refresh the wider pinned census or label older figures as historical engine snapshots.
- [ ] Inventory every owner that performs type inference, conversion, effects, command recognition, graph propagation, capability selection, or fallback classification.
- [ ] Identify duplicated decisions across analyzer, transpiler, emitters, policies, artifact shaping, and census reporting.
- [ ] Record current public CLI, cmdlet, plan, manifest, source-map, export, and artifact contracts.
- [ ] Define the current PowerShell 5.1/7 and target-framework acceptance matrix.

Exit gate: the refactor has a behavioral baseline and an explicit ownership map. No current behavior depends on undocumented assumptions.

## Milestone 1 — Establish compiler boundaries

- [ ] Define the dependency direction between parsing, binding, IR, analysis, lowering, backends, reporting, and artifact orchestration.
- [ ] Define a neutral `SourceSpan` that does not expose SMA AST objects.
- [ ] Define immutable symbol identities for files, functions, parameters, locals, pipeline variables, and generated commands.
- [ ] Add one minimal parser-to-binder-to-backend path.
- [ ] Prevent new backends from accepting AST nodes.
- [ ] Prevent binders from producing C# strings.
- [ ] Keep the IR internal until a real external consumer requires a stable public API.

Exit gate: an empty or literal-returning program flows through the complete new pipeline.

## Milestone 2 — Implement foundational bound IR

- [ ] Program, source document, function, parameter, block, statement, and expression nodes.
- [ ] Symbols and lexical scopes.
- [ ] Literal, variable, assignment, conversion, invocation, and return nodes.
- [ ] PowerShell type and CLR representation models.
- [ ] Value state and output cardinality.
- [ ] Effects and required capabilities.
- [ ] Execution disposition and stable fallback reasons.
- [ ] Source spans on every node that can produce a diagnostic or generated line.

Exit gate: the simplest existing Strict functions compile entirely through bound IR with equivalent source-map evidence.

## Milestone 3 — Add deterministic analysis passes

- [ ] Definite assignment and read-before-write analysis.
- [ ] Local and parameter type propagation.
- [ ] Return and success-output type inference.
- [ ] Pipeline cardinality and scalarization analysis.
- [ ] Function call graph construction.
- [ ] Recursive fixed-point analysis.
- [ ] Effect propagation through local calls.
- [ ] Capability propagation through local calls.
- [ ] Fallback propagation with causal diagnostics.
- [ ] Stable results independent of file, declaration, registration, and traversal order.

Exit gate: reversing input-file and function-declaration order produces equivalent bound plans, diagnostics, and artifacts.

## Milestone 4 — Separate lowering from emission

- [ ] Define lowered function, parameter, local, control-flow, stream, pipeline, command-region, and return forms.
- [ ] Move target selection into lowering.
- [ ] Make the C# backend render lowered nodes only.
- [ ] Remove local and return type inference from the C# emitter.
- [ ] Remove eligibility and fallback decisions from emitters.
- [ ] Remove command recognition from emitters.
- [ ] Make source maps consume bound/lowered node spans.
- [ ] Make census consume the shared semantic result.

Exit gate: the C# backend builds without a reference to PowerShell AST types.

## Milestone 5 — Migrate all current behavior

Migrate one semantic area at a time. Each completed item includes deletion of the equivalent legacy path.

- [ ] Parameters, aliases, metadata, parameter sets, and validation.
- [ ] Literal defaults and omitted-versus-bound state.
- [ ] Variables, assignments, returns, and output shaping.
- [ ] Operators, truthiness, and conversions.
- [ ] Conditions, loops, switch, try/catch, throw, break, and continue.
- [ ] Arrays, collections, dictionaries, and bounded object construction.
- [ ] Member access and method invocation.
- [ ] Local calls, call graphs, and supported recursion.
- [ ] Runtime-state intrinsics and `ShouldProcess` state.
- [ ] Existing streams, command regions, and typed captures.
- [ ] Executable parameter binding and generated binary-cmdlet contracts.

Exit gate:

- all existing compiler tests exercise the IR path;
- current generic corpus behavior is preserved;
- direct AST-to-C# semantic paths are deleted;
- no compatibility switch silently routes new features through the legacy emitter.

Broad coverage work remains frozen until this gate passes.

## Milestone 6 — Preserve help and module contracts

- [ ] Bind comment-based help as function metadata.
- [ ] Reuse the existing documentation engine to generate external MAML for compiled cmdlets.
- [ ] Preserve synopsis, description, parameter help, examples, notes, links, inputs, and outputs.
- [ ] Preserve aliases, exports, and mixed Hybrid command identity.
- [ ] Verify `Get-Help` for typed and retained commands in the same module.
- [ ] Refresh the product-neutral baseline and wider pinned census.

Exit gate: compiling a function does not remove or degrade its help or exported command contract.

This is the first new end-to-end feature after migration because it proves that source metadata can flow through the IR, lowering, generated module, and artifact validation paths. It also removes the current help-preservation barrier from real-module coverage measurements.

## Milestone 7 — Build command and pipeline semantics

- [ ] Canonical command, module qualification, alias, and discovery resolution.
- [ ] Deterministic command-semantic registry.
- [ ] Duplicate and ambiguous registration validation.
- [ ] Stream command binder.
- [ ] Projection command binder.
- [ ] Filtering command binder.
- [ ] Mapping/enumeration command binder.
- [ ] Sorting command binder.
- [ ] General bounded-command-region binder.
- [ ] Command output, cardinality, stream, and error contracts.
- [ ] Pipeline-stage composition.
- [ ] Explicit pipeline symbols for `$_` and `$PSItem`.

Exit gate: adding a supported `Select-Object` shape does not require coordinated semantic edits to analyzer, transpiler, emitter, Hybrid composer, and census.

## Milestone 8 — Complete advanced-function lifecycle

- [ ] `begin` block.
- [ ] per-record `process` block.
- [ ] `end` block.
- [ ] `clean` block and disposal behavior.
- [ ] `ValueFromPipeline` binding.
- [ ] `ValueFromPipelineByPropertyName` binding.
- [ ] `ValueFromRemainingArguments` behavior.
- [ ] common parameters.
- [ ] `ShouldProcess` and `ConfirmImpact`.
- [ ] per-record state and output.
- [ ] terminating and nonterminating errors.
- [ ] stream and progress lifecycle.

Exit gate: representative conventional advanced functions execute as generated cmdlets with PowerShell-equivalent invocation and lifecycle behavior.

## Milestone 9 — Complete value and object flows

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

## Milestone 10 — Expand bounded runtime state

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

## Milestone 11 — Run generic coverage waves

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

## Milestone 12 — Optimize proven IR

- [ ] constant folding.
- [ ] dead-branch elimination.
- [ ] allocation reduction.
- [ ] pipeline-stage fusion.
- [ ] command-region coalescing.
- [ ] specialized collection loops.
- [ ] cached conversion plans.
- [ ] improved generated source and PDB mapping.

Exit gate: optimizations preserve differential and artifact contracts and show meaningful workload improvements. Small host-dominated workloads are not used to justify compiler-wide complexity.

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
- [ ] a fresh corpus/census result.

Every new command family should normally require:

- [ ] one canonical command registration;
- [ ] one command-family binder;
- [ ] existing bound pipeline or command nodes where possible;
- [ ] an explicit unsupported-shape result rather than handler fall-through;
- [ ] command, pipeline, stream, cardinality, and error differential tests;
- [ ] no new command-name conditionals in an emitter.

## Maintainability gates

- [ ] Prefer 100–400 lines per semantic owner.
- [ ] Review files approaching 600–700 lines before adding another responsibility.
- [ ] Keep 800 lines as the touched non-generated compiler-file maximum.
- [ ] Use the existing line-count tooling instead of adding another policy engine.
- [ ] Split by semantic responsibility, never arbitrary line ranges.
- [ ] Permit a central exhaustive node-dispatch switch only when it delegates immediately.
- [ ] Do not use partial classes to hide unrelated responsibilities.
- [ ] Keep substantial generated PowerShell and C# templates in native template/resource files.
- [ ] Add XML documentation to public and non-obvious reusable contracts.
- [ ] Keep tests grouped by behavioral contract rather than implementation class count.

## Architecture completion gate

The redesign is complete only when all of the following are true:

- [ ] no backend consumes PowerShell AST;
- [ ] no emitter performs semantic type or effect inference;
- [ ] no census code independently decides compiler eligibility;
- [ ] no command behavior depends on registration order;
- [ ] all current behavior runs through the bound IR;
- [ ] migrated direct AST-to-C# paths are deleted;
- [ ] the full compiler suite passes;
- [ ] applicable PowerShell 5.1 and PowerShell 7 differential lanes pass;
- [ ] net472, net8.0, and net10.0 builds remain warning-free;
- [ ] generated artifacts preserve invocation, export, help, source-map, and fallback contracts;
- [ ] touched compiler production files remain below 800 lines;
- [ ] adding an operator, syntax form, or command family has one obvious canonical owner.

Only after this gate should PowerForge treat broad percentage growth as the primary objective.

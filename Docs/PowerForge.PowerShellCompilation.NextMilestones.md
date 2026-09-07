# PowerShell compiler: next major milestones

Updated: 2026-09-07.

Execution status: M24 is active; M25 and M26 are the next dependency-ordered implementation goals. This tranche includes remediation of existing and newly exposed compiler defects, with complete-workflow qualification across different source styles.

This is the next execution tranche of the [compiler architecture roadmap](PowerForge.PowerShellCompilation.Roadmap.md). Milestones 24–30 turn its remaining product gaps into large deliverables with observable exit gates. The [readiness assessment](PowerForge.PowerShellCompilation.Assessment.md) owns dated audit results; the [compilation guide](PowerForge.PowerShellCompilation.md) owns current commands and supported behavior.

## Starting point

The compiler already has the expensive architectural foundations: canonical binding and immutable IR, a C# backend, semantic profiles and oracles, dependency locks, generated projects, CLR ABI metadata, provider contracts, Hybrid state/region support, and bounded Strict managed/NativeAOT delivery. Replacing the backend is not the next step.

The main shortfall is useful coverage. Six authored Strict programs qualify a narrow accepted subset. The broader external baseline has 196 units across seven workloads, with only six emitted units and no complete-workload execution credit. Emitted helpers, retained source, successful packaging, and analysis-only opportunities measure different outcomes. A successful Hybrid package can still execute almost entirely through PowerShell.

The 2026-09-07 audit also reproduced accepted-code errors caused by confusing an expression cast with a persistent variable constraint. Correcting that distinction can reduce emission for programs whose dynamic promotion was never implemented correctly. Coverage must not be recovered by relaxing the fix.

## Delivery order

| Milestone | User-visible outcome | Prerequisites | Relationship to existing work |
| --- | --- | --- | --- |
| 24. Values, collections, and error continuation | Ordinary data-processing functions preserve PowerShell behavior through compilation | Correctness fixes and their regression gate | Expands bounded M11/M18 and supplies M23 transfer contracts |
| 25. Complete pipelines and advanced functions | Complete commands produce every output and preserve failure, stop, and cleanup behavior | M24 contracts for the values and errors crossing each stage | Expands bounded M8/M10; does not replace their owners |
| 26. Stateful modules and multiple Hybrid regions | Real modules retain one correct state owner while substantially more of their bodies compile | M24; relevant M25 stream/lifecycle contracts | Closes the remaining M23 scope |
| 27. Practical CLR library consumption | A PowerShell-authored library is useful from an ordinary C# application | M24; M26 only for stateful libraries | Product qualification of M6/M9/M16 ABI and dependency foundations |
| 28. Source-first development loop | Diagnose, run, inspect, and debug a compiled project from its authored source | Existing project workflow; incremental integration with M24–27 | Builds on M17/M21, source maps, PDBs, and verified cache |
| 29. Reproducible distribution and qualified platforms | A new user installs a supported toolchain and runs its artifacts on a clean target | A declared supported workload/profile set; release authorization | Closes M19/M21/M22 external gates without duplicating them |
| 30. Measured acceleration of useful workloads | Qualified programs have predictable performance and deployment tradeoffs | Correctness and execution qualification of each measured workload | Extends M15/M22 optimization after semantic growth |

Start implementation with M24. Discovery and developer-tool design can proceed alongside it. Platform and public-feed qualification are independent lanes once the selected artifact contracts stabilize; they do not prevent source-level correctness work. M30 is a later optimization phase, not an admission requirement for M24–29.

Each milestone spans coherent implementation slices. A slice must carry binding, bound/lowered contracts, diagnostics, affected artifacts, and executable proof together; do not land a new emitter interpretation and promise semantic closure later.

## Milestone 24 — Preserve values, collections, and error continuation

**Outcome:** compile useful data transformations without changing what a caller observes when a value promotes, an enumerable fails, or a later statement continues.

- [ ] Represent value type and variable constraint separately. Add bounded unconstrained numeric promotion through canonical contracts, including integral widths, Single/Double, narrowing, compound updates, increment/decrement, and overflow. Keep unsupported cases explicitly retained/rejected until implemented; an RHS cast is not a declaration.
- [ ] Close null, `AutomationNull`, scalar, singleton, nested-array, and empty-output cardinality across assignment, `@()`, comma expressions, return, indexing, and foreach. Preserve exact enumeration depth and order.
- [ ] Model collection enumeration and disposal, including partial success before `MoveNext`, `Current`, or cleanup fails. Preserve PowerShell error identity, `ErrorAction` behavior, state after failure, and continuation instead of simply translating all failures into a CLR throw.
- [ ] Extend bounded dictionaries, lists, object properties, member/index updates, aliasing, and typed-array conversions using one value/shape owner. Wider ETS remains hosted until its observable contract is closed.
- [ ] Select three complete data-processing workflows from the pinned discovery frontier. Record every co-blocker before implementation so isolated helper support is not mistaken for a workflow unlock.

**Exit gate:** three previously blocked complete workflows from at least two unrelated families execute original/generated differential matrices. Include empty, singleton, nested, null, overflow, malformed-input, and injected enumeration-failure cases. Prove values, CLR types, output count/order, errors, continued statements, and disposal on each claimed host. At least one workflow must pass as a Strict runtime-free artifact; Hybrid results must identify the operations that remain hosted. A higher emitted-unit count alone does not close the milestone.

**First slice:** recover one bounded array/foreach transformation with its enumeration-failure continuation contract. Use the previously rejected collection experiment only as evidence of the missing contract, not as code to reinstate before that contract exists.

The discovery packet now pins SamErde/PowerShell, MicrosoftIntune, CleanupMonster, PSSharedGoods, and the algorithm library. The PSSharedGoods snapshot at `2a807a4f11ba458b7bc405ce3674d93838639af1` adds its full 286-unit module closure: the initial scan emits 14 units in Hybrid and zero in Strict. Across the packet each mode assesses 278 of 291 submitted inputs and retains all 13 source-closure failures. This is analysis-only discovery; the existing public/external acceptance baselines and their execution claims remain separate. Use the established public corpus, including its other library styles, for regression qualification as contracts expand.

The first closed-value implementation accepts inferred empty `@()` arrays and ordinary null elements in Object arrays. Generated empty arrays have distinct identities, matching authored allocations. Typed null elements that require PowerShell conversion remain retained/rejected, including comma literals. Inferred scalar-string foreach now preserves zero iterations for null and one iteration for an empty or nonempty string, without restoring the incorrect persistent constraint on RHS casts. Focused original/generated module checks cover PowerShell 5.1 and 7. These cases do not close the collection milestone: enumerable failures, nested collection grouping, numeric promotion, and the three complete workflow gates remain open.

The loop-state sweep also removes stale value facts across loop writes, iterator assignment, backedges, and exits, including `continue` paths into a `for` iterator. A receiver that starts non-null but can become null no longer receives an unsafe Strict CLR call; an assignment inside the body can establish a fresh non-null fact before use. Possible module-state origin propagates across loop-carried aliases and local-function returns so later iterations cannot bypass the live-state boundary. Hybrid nullable-loop checks preserve the original error identity and later statements through retained PowerShell execution. This is correctness protection, not compiled-workflow credit.

Authored array expressions now preserve one enumeration level inside `@()`: `@((1,2))` collects two numbers, while `@((1,2),(3,4))` collects two nested arrays. A comma wrapper such as `@(,$Value)` preserves the wrapped object's identity without enumerating it. Original/generated Strict binary-module checks cover both PowerShell hosts, including nested and empty arrays, ordinary null, typed scalar records, and a wrapped enumerable that throws if enumerated. Arbitrary unwrapped enumerables and records requiring unsupported element conversions still retain PowerShell behavior. Complete workflow qualification remains open.

## Milestone 25 — Compile complete pipelines and advanced functions

**Outcome:** compile an entire command workflow, including multiple success outputs, instead of restricting useful work to a scalar terminal return.

- [ ] Preserve sequential success output across local calls, branches, loops, try/catch/finally, and terminal returns. A return value must not discard earlier records or prematurely end the caller.
- [ ] Compose typed and hosted pipeline stages with canonical input/output/stream/error contracts. Handle enumeration boundaries, partial output, downstream early stop, cancellation, and disposal without materializing every pipeline by default.
- [ ] Extend advanced-function binding and lifecycle for selected real shapes: pipeline input by value/property, parameter sets, default-versus-bound state, validation, common parameters, begin/process/end, and host-qualified clean behavior.
- [ ] Preserve local-call binding, dynamic-scope observations, streams, nonterminating errors, and caller continuation through the full reachable call graph. Unknown observers still force a truthful boundary.
- [ ] Add two complete pipeline-heavy commands to executable acceptance, including a command with more than one output-producing local call and one with a retained provider stage.

**Exit gate:** both commands run in their original module context and generated artifacts with record-for-record output/stream comparison. Exercise zero/one/many inputs, partial failure, downstream stop, cancellation, and cleanup on PowerShell 5.1 and selected PowerShell 7 profiles where the authored feature exists. Strict admission uses runtime-free adapters only; unsupported host features remain explicit.

## Milestone 26 — Compile stateful modules and multiple Hybrid regions

**Outcome:** finish M23 with meaningful module-scale compilation while preserving one state owner and one semantic pipeline.

- [ ] Generalize scalar prefix/suffix selection to multiple statement-aligned regions using canonical liveness, definite assignment, effects, error routes, and transfer contracts. Typed work before and after a hosted operation must preserve authored order.
- [ ] Admit safe local-call closures and the collection/value contracts qualified in M24. Retain an entire region when any live input, output consumer, alias, exception route, or source identity remains unproved.
- [ ] Keep the parent PowerShell module authoritative for Hybrid state. Prove nested/reentrant calls, two runspaces, retained-code mutation, initialization failure, removal/reimport, and authored cleanup.
- [ ] Implement Strict module initialization and lifetime independently: deterministic initializers, instance/state ABI, explicit concurrency policy, initialization failure rollback, disposal/reset, and separate library instances. No hidden runspace or generated copy of Hybrid state is allowed.
- [ ] Execute two unrelated real module workflows that cross compiled and retained code, and one small complete stateful Strict library/application.

**Exit gate:** the two Hybrid workflows promote more than a single terminal scalar helper and preserve state, streams, exceptions, cancellation, and cleanup in their full module context. The Strict workflow runs from a clean C# consumer or EXE with independent-instance tests and certified runtime-free closure. Explain output and the final ledger agree with the executed boundaries. Performance is recorded separately.

## Milestone 27 — Make compiled CLR libraries practical to consume

**Outcome:** publishable PowerShell-authored DLLs expose a useful, predictable .NET API rather than merely proving that a generated assembly can load.

- [ ] Choose two library use cases with different data shapes, such as a collection transformation and a validation/calculation library. Derive required API contracts from those consumers.
- [ ] Extend the existing ABI as needed for typed records/collections, nullability, defaults, overload/name collisions, errors, streams, cancellation, ownership, and disposal. Version any ABI change; retain deterministic source and API mapping.
- [ ] Package generated libraries, XML documentation, symbols, dependency locks, and required runtime-free support assets through PowerForge. Rebuild the emitted project independently.
- [ ] Qualify one independently built provider package through its existing SDK/ABI with exact version, signer policy, transitive dependencies, and failure/cancellation behavior. Do not equate arbitrary cmdlets with registered adapters.
- [ ] Add normal C# consumers that restore the package through a temporary local feed and run without referencing compiler internals or PowerShell. Public-feed consumption belongs to M29.

**Exit gate:** both consumer applications compile and execute success, invalid-input, null/empty, cancellation where supported, and repeated-lifetime cases. An API compatibility check catches an intentionally incompatible sample change. Runtime/dependency inspection confirms the promised CLR-library contract. Native shared-library exports remain deferred until a specific non-.NET embedding consumer requires that separate ABI.

## Milestone 28 — Deliver a source-first development loop

**Outcome:** a user can understand and develop a compiled project without treating generated C# and temporary build directories as the primary interface.

- [ ] Add a coherent run workflow to the existing project service and thin CLI/cmdlet surfaces. Select the declared target, handle arguments/stdin/stdout/exit codes, and reuse validated build outputs and locks.
- [ ] Extend incremental rebuild/watch behavior using existing source/dependency fingerprints. A changed callee, resource, provider, semantic profile, or dependency must invalidate the right artifact; a no-change run must reuse verified output.
- [ ] Present blockers grouped by complete function/workflow and explain the causal dependency chain, supported alternatives, and exact source locations. Keep semantic, shaping, dependency, and target failures distinct without making users read the full internal ledger.
- [ ] Prove source-level debugging through existing generated C#, PDBs, and source maps: breakpoints, stepping, locals, call stacks, and exceptions across a typed call and a Hybrid boundary. Explicitly identify any boundary that cannot yet be stepped through.
- [ ] Add a short contributor-independent quickstart for one EXE and one library/module project, with runnable examples and a supported-host diagnostic when prerequisites are missing.

**Exit gate:** from a clean workspace, complete init → lock → restore → run/test → edit → rebuild → diagnose/debug → pack using documented entrypoints. Observe the debugger and any editor UI directly. Verify argument quoting, redirected streams, cancellation, offline restore, cache invalidation, and source-line accuracy. Do not claim debugger integration from the presence of PDB files alone.

## Milestone 29 — Qualify distribution, providers, and platforms

**Outcome:** turn a source-proven preview into a declared, reproducibly installable supported product.

- [ ] Complete M21's compiler/core/CLI/PowerShell/provider package release set and clean public-feed install, upgrade, rollback, and consumer execution after explicit publication authorization. Local feeds and source builds remain separate evidence.
- [ ] Close M22's native-Linux test capability partition. Keep Windows-only signing, management, net472, and oracle fixtures in correctly labeled lanes; test actual Linux behavior in its own checkout/output tree.
- [ ] Qualify the next selected physical RID/deployment profiles one at a time, prioritizing an actual consumer need. macOS/Arm64, Windows Arm64, and extra deployment forms remain experimental until executed on their target.
- [ ] Close M19's disposable management-target reboot/reconnect gate only on a target explicitly placed under the test lifecycle. Preserve the separate qualified authentication/protocol/provider profiles.
- [ ] Exercise long/non-ASCII/space-containing paths, clean machine install, resource discovery, offline operation, package tampering, runtime servicing, uninstall, and side-by-side versions. Carry the real Windows path limitation into qualification instead of hiding it with an unusually short test path.
- [ ] Publish the support matrix and compatibility/security servicing policy with the actual released package versions and their immutable evidence.

**Exit gate:** the declared release set is publicly obtainable, survives clean install/upgrade/rollback, and runs the qualified workload packet on every advertised profile. CI, signed artifacts, public availability, installed versions, and target-host execution are independently recorded. Missing optional platforms narrow the supported matrix; they must not be reported as tested.

## Milestone 30 — Accelerate proven workflows

**Outcome:** make performance predictable after the selected programs are correct and useful.

- [ ] Select measured hot workloads from M24–27 and retain cold-start, short/cheap, and error-path controls. Compare original PowerShell, Hybrid, Strict managed, and NativeAOT only where each implements the same contract.
- [ ] Use the existing profiler and region graph to reduce crossing overhead, specialize loops, fuse compatible pipeline stages, and eliminate proven allocations through canonical IR/lowering passes.
- [ ] Record fresh-process startup, warm throughput, allocations, peak working set, build time, artifact size, and import cost with pinned compiler/toolchain/host identities and validated outputs.
- [ ] Make recommendations explain why a profile helps or loses for the measured workload. Keep tiny commands on an appropriate path when compilation overhead dominates.
- [ ] Consider another backend only after a measured C#/Roslyn/MSBuild limitation blocks a required result and the alternative can consume the same lowered IR and source/ABI contracts.

**Exit gate:** at least three unrelated qualified workflow families have repeatable comparisons, declared budgets, and honest wins/ties/regressions. A promotion must meet its predeclared workload budget without semantic or deployment regressions. No universal speedup or language-wide coverage claim follows from this gate.

## How to choose the next implementation slice

1. Capture a complete blocked workflow from an immutable source packet, its license/selection policy, and every blocker along its reachable execution path.
2. Minimize the semantic failure into a generic regression without importing or executing external administration code during analysis.
3. Fix accepted-code defects first. Preserve the failing original/generated example and sweep sibling assignment, call, collection, or lifetime paths.
4. Choose the smallest reusable contract that removes the workflow's decisive blockers. Keep other missing contracts visible instead of refactoring the source to manufacture a success.
5. Prove the generated artifact and original behavior on the claimed hosts, update the unchanged census comparison, and add the complete workflow to acceptance.

The next tranche succeeds through complete workflow unlocks and reproducible consumption. Completed architecture checkboxes remain useful foundations, but they do not substitute for those outcomes.

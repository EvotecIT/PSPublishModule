# PowerShell compiler: next major milestones

Updated: 2026-09-22. Qualified continuation: `3cfa5edadbf0a9352924c2d45d9e017776d65ed1` on `fix/compiler-audit-milestones`; production corrections are in `18e443a14`.

M24–M27 have completed their bounded local implementation gates. The September 22 stopping failure was traced to a test handshake race and corrected across eight fixtures without changing runtime semantics. Package cancellation, metadata, and coverage corrections pass the 1,097-test compiler gate, six-program Strict corpus, and final 55-case focused suite. **M28 is next.** Completion of an implementation milestone does not establish general PowerShell compatibility, a released package, or qualification on an untested host.

The [readiness assessment](PowerForge.PowerShellCompilation.Assessment.md) owns findings and dated validation. The [architecture roadmap](PowerForge.PowerShellCompilation.Roadmap.md) owns design and M0–M23. The [compilation guide](PowerForge.PowerShellCompilation.md) owns commands. This document owns the remaining execution order; earlier per-commit journals are available in Git history.

## Starting point

Keep the canonical parser → binder → bound IR → analysis → lowering → C# backend. Keep semantic and artifact rules in `PowerForge.PowerShell` / `PowerForge`, with thin CLI and cmdlet adapters. New syntax must not acquire a separate evaluator in the emitter, census, or wrapper.

Generated compiler artifacts accept `net10.0` and `net472`; executables accept only `net10.0`. Hosted artifact qualification targets PowerShell 7.6 and Windows PowerShell 5.1. This is distinct from the shared `PowerForge.PowerShell` host library, whose project still builds `net472;net8.0;net10.0`. Existing 7.4 observations and the host-library build target do not reopen generated-artifact net8 support. Do not remove a shared-library target without checking its consumers.

The September 20 checkpoint recorded 1,063 compiler-gate tests and six Strict Windows programs passing. Its pinned PSSharedGoods census recorded 188/282 complete emitted functions and 36 promoted regions. These are dated results for one input packet, not a language-coverage percentage or speedup. An emitted Hybrid method may still delegate most of its work to PowerShell. The older heterogeneous 6/196-unit packet has different inputs and must not be compared as if it were the same census.

## Delivery order

| Priority | Milestone | Current state | Required outcome |
| --- | --- | --- | --- |
| 1 | M28 development loop | Current | Documented run/watch/diagnose/debug experience over the existing owners |
| 2 | M29 distribution and platforms | Partial / separate qualification | Supported hosts, release set, public-feed lifecycle, and clean-target execution are evidenced independently |
| 3 | M30 performance | Planned | Repeatable workload benefit after semantic and deployment correctness |
| Ongoing | M24–M26 semantic expansion | Bounded implementation complete | Expand only for selected blocked workflows, retaining unsupported behavior safely |

Known accepted-code defects take priority over breadth or performance. Keep remediation proportional to a reachable trigger and consequence: cover the observed failure and consequential sibling paths, then stop expanding the matrix once the supported contract is demonstrated. Record every finding as reproduced, source-demonstrated, or unverified; record its owner, affected modes, test, and closure evidence. Do not mark a milestone complete because its aggregate test count increased.

## Milestone 24 — Preserve values, collections, and error continuation

**Status: Complete within the qualified value and ownership families.**

**Outcome:** data transformations preserve observable values, CLR types, identity, cardinality, order, error behavior, continued statements, and cleanup.

The completed contract separates expression casts from variable constraints; preserves bounded numeric promotion; models no-value/null/scalar/collection output; and retains PowerShell enumeration and failure behavior where the compiler cannot own it safely. Schema-6 transfer evidence covers exact atomic maps, stable-scalar vectors, read-only exact list-like references, return/fallthrough envelopes, closed scalar/vector alternatives, and one exact fresh-ArrayList local factory. Only fresh one-dimensional stable-scalar vectors receive compiled mutation ownership, ending at live-out transfer.

Hybrid native functions additionally use the active host for computed members, map-key iteration, patterns, conditional value capture, and retained-reference mutation. These capabilities do not become runtime-free merely because the surrounding method is generated C#.

**Evidence:** the conditional-value/error matrices, unchanged TimeSpan and random-name workflows, full-module registry workflow, collection transfer/return tests, and Strict stateful consumer. See the dated assessment for the checkpoint and execution limits.

**Boundaries to preserve:** borrowed compiled reference mutation, arbitrary object/ETS transfer, arbitrary enumerators, multidimensional/open arrays, unproved local ownership, and general local-call collection factories remain retained or rejected. A later qualified Hybrid slice must not silently broaden Strict admission.

**Gate for any expansion:** select three complete previously blocked workflows from at least two families, including one Strict artifact when claiming runtime-free growth. Compare original/generated behavior for empty, singleton, nested, null, overflow, malformed input, and injected acquisition/advance/current/disposal failures on each claimed host. Track all co-blockers and state which operations still execute in PowerShell.

## Milestone 25 — Compile complete pipelines and advanced functions

**Status: Complete within the qualified command families; stopping fixtures requalified on both hosts.**

**Outcome:** complete commands preserve every record, parameter binding, failure/continuation, downstream stop, cancellation, and cleanup.

The existing native invocation owns PowerShell command resolution, parameter sets, defaults, validation, common parameters, dynamic scope, pipeline stages, and begin/process/end/clean behavior where the host supports it. Shared output/capture owners preserve local-call records, enumeration depth, error destinations, and cleanup ordering. The statement-error bridge preserves admitted CLR invocation errors through the loaded PowerShell host; it is not a runtime-free error implementation.

**Evidence:** complete pipeline and implicit-output matrices, random-name calls, statement-error and output-unwind tests, and both supported hosted artifact lanes. Provider replacements qualify compiler composition without granting live administration or network-operation coverage.

**Boundaries to preserve:** terminal success sinks, unsupported basic-function/common-parameter collisions, unproved error-state observers, broader mutable receivers, and unregistered runtime-free adapters retain their own admission decisions. Record an unsupported shape explicitly rather than routing only its success stream.

**Gate for any expansion:** two complete commands in their original module context, including multiple output-producing local calls and a retained provider stage. Compare zero/one/many inputs, ordered success and non-success streams, partial failure, stop, cancellation, cleanup, and subsequent invocation. PowerShell 5.1 cannot be credited for syntax that its parser does not support.

## Milestone 26 — Compile stateful modules and multiple Hybrid regions

**Status: Complete for the bounded M23 state/region contract.**

**Outcome:** several safe regions share one module state owner without creating another PowerShell runtime.

The selector admits a maximal approved candidate from each non-overlapping canonical run and preserves independently safe suffixes when a larger candidate fails policy. It does not promise every arbitrary source span. Canonical graph inputs/outputs must match transfer metadata. The guarded prefix remains the fresh-local owner; additional regions cannot invent ownership. Hybrid state stays in the parent module. Strict stateful libraries use a separate runtime-free instance/lifetime contract.

**Evidence:** the same-function `Get-ObjectEnumValues` two-region workflow; synthetic four-scalar-plus-factory region composition; full pinned registry and saved-state modules; and a stateful reservation library consumed by ordinary C#. Tests cover two runspaces, nesting/reentrancy, retained mutation, initialization failure, stop/cancellation, reuse, removal/reimport, independent instances, reset, concurrency, and disposal within their selected shapes.

Registry providers are isolated replacements. Saved-state tests use in-memory providers and do not qualify real filesystem persistence, the external dependency manifest, or the public administration entrypoints. The census remained 188/282 functions and 36 promoted regions after general selection because that packet had no third eligible disjoint run.

**Boundaries to preserve:** borrowed mutable transfer, ambient initializer effects, caller-dependent inherited state, dynamic object identity, and general lifecycle/error envelopes remain separate work. Strict state qualification currently uses explicitly typed immutable scalar field declarations with controlled state updates.

**Gate for any expansion:** retain the two unrelated complete Hybrid module workflows and the clean Strict consumer. Add a minimized real workflow for the new ownership/lifetime shape; verify aliasing, failure rollback, streams, stopping, disposal, next invocation, and agreement between explain output and executed regions. Record crossing cost separately from correctness.

## Milestone 27 — Make compiled CLR libraries practical to consume

**Status: Complete for bounded local-feed CLR consumption, including September 22 corrective qualification.**

**Outcome:** PowerShell-authored libraries expose a predictable .NET API and produce packages that ordinary consumers can restore and use safely.

Implemented and previously qualified:

- [x] Static TimeSpan and stateful reservation consumers exercise different data and lifetime contracts.
- [x] Deterministic local-feed packages contain generated library, XML documentation, portable PDBs, source, locks, and provenance; an independent rebuild checks library/PDB/provider bytes.
- [x] Ordinary consumers restore generated packages for `net10.0` and `net472` without source-project references or a PowerShell runtime requirement.
- [x] Registered providers have separate output, failure, cancellation, cleanup, and closure tests.
- [x] Generated API comparison rejects removed/changed methods and module lifetime changes. Static manifests use schema 4; stateful manifests use schema 5.

Corrective closure:

- [x] **R1 / P2:** cancellation-aware snapshot/copy/hash/archive work and a final pre-commit check preserve previous output. Rebuild cleanup precedes publication; later cancellation cannot be reported as though a committed package were rolled back.
- [x] **R2 / P3:** shared NuGet ID validation and canonical `x.y.z` policy reject malformed library, provider, and dependency metadata. Real builders preserve previous output; NuGet and ordinary consumers validate valid packages.
- [x] **R3 / P2 validation:** include all nine ABI cases and six existing provider-consumer/lifecycle/dependency cases in the bounded gate. Require passing results from the necessary M27 families.
- [x] Qualify replacement failure, Windows destination contention, cleanup failure, and cancellation during/after writing. Cleanup errors retain the primary failure and diagnostic details.
- [x] Define the current consumption contract: select identity/version/TFM, restore, and rebuild the consumer. Generated ABI comparison covers API/module lifetime; binary drop-in replacement and replacing loaded assemblies are not promised. M29 qualifies any future broader upgrade contract before promotion.
- [x] Final focused suite passes 55/55 with no failures/skips, including the eight stopping fixtures on both hosts. The net472 Release build has zero warnings/errors; targeted read-only confirmation of the production fixes has no actionable findings.
- [x] Canonical compiler gate passes 1,097/1,097 with zero failures/skips; Strict corpus passes 6/6 with 24 emitted units. Revision, host, artifact, and follow-up fixture evidence is recorded in the assessment.

**Exit gate:** both ordinary consumers exercise success, invalid input, null/empty, defaults, cancellation where supported, and repeated lifetime. Intentional generated API incompatibilities and malformed package identities are rejected. Cancellation/failure cannot damage the prior destination. Runtime/dependency inspection proves the promised runtime-free closure. Signed-input rejection is not signed-package qualification; signing and public-feed upgrade/rollback remain M29.

Native shared-library exports remain deferred until a concrete non-.NET embedding consumer requires their separate ABI.

## Milestone 28 — Deliver a source-first development loop

**Status: Current.**

**Outcome:** users develop from PowerShell source without treating generated C# or temporary build paths as the main interface.

The [project CLI](../PowerForge.Cli/Program.Command.PowerShell.Project.cs) already exposes `init`, `analyze`, `explain`, `recommend`, `lock`, `restore`, `build`, `test`, `pack`, `install`, and `diagnose` through `PowerShellCompilationProjectWorkflowService`. Preserve that owner and its existing workflow. Current project `pack` produces a qualified ZIP; the Strict CLR library NuGet builder is a separate shared API. Neither `run` nor `watch` is currently a project command.

- [ ] Add run to the existing project owner and thin surfaces, preserving declared target, arguments, stdin/stdout/stderr, exit codes, cancellation, and locks.
- [ ] Add project watch and incremental rebuild using the [existing artifact cache](../PowerForge.PowerShell/Services/Compilation/PowerShellCompilationArtifactBuildCache.cs) and fingerprints. Cover callee, resource, provider, profile, and dependency changes, rapid edits, failed builds, and verified no-change reuse. Never execute the last successful artifact as though it were the result of a failed new build.
- [ ] Integrate the existing library NuGet builder with the documented project packaging path and reuse existing ABI-baseline checks. Keep qualified ZIP delivery and NuGet consumption explicit; do not create another packer or compatibility checker.
- [ ] Extend existing explain/diagnose output to group blockers by complete function/workflow and show causal dependencies, source positions, and retained/rejected behavior. Keep semantic, shaping, dependency, and target failures distinct.
- [ ] Observe debugging directly: source breakpoints, stepping, locals, stacks, and exceptions across a typed call and a Hybrid boundary. State boundaries that cannot be stepped through; PDB presence alone is insufficient.
- [ ] Provide contributor-independent EXE and library/module quickstarts and actionable supported-host diagnostics.

**Exit gate:** clean init → lock → restore → run/test → edit → rebuild → diagnose/debug → pack through documented entrypoints. Verify argument quoting, redirected streams, cancellation, offline restore, stale-output prevention, invalidation, and source-line accuracy.

## Milestone 29 — Qualify distribution, providers, and platforms

**Status: Partial; public release and target qualification remain separate from local implementation.**

- [ ] Reconcile the continuation with the then-current default branch, inspect conflicts at shared owners, and run exact-candidate CI/review before integration. Do not infer readiness from the branch's earlier main merge or local test result.
- [ ] Measure the canonical gate on the actual CI runner and retain all required contract families within an explicit execution budget. The September 22 corrective local test phase took 45m48s, longer than the workflow's 40-minute compiler-step budget; local elapsed time is not a hosted-run measurement.
- [ ] Complete the compiler/core/CLI/PowerShell/provider release set and clean public-feed install, upgrade, rollback, and execution after publication authorization. Record source, package, installed version, and runtime evidence separately.
- [ ] Exercise package-version upgrades through ordinary restore/rebuild. If an actual consumer requires binary drop-in upgrades, first define assembly identity, TFM, dependency/provider binding, and running-process restart policy, then test both previously compiled and rebuilt consumers. Do not infer this guarantee from the generated ABI hash.
- [ ] Qualify PowerShell servicing updates against the native-function and statement-error bridges, which use private host APIs. Check capabilities before side effects; preserve authored fallback where supplied, otherwise fail with an actionable diagnostic. Test missing/changed contracts and a new patch candidate before changing pins.
- [ ] Partition native Linux validation from Windows-only signing, management, net472, and oracle fixtures. Run actual Linux behavior in its own output tree; cross-publishing alone is not target execution.
- [ ] Promote additional physical RID/deployment profiles only for an actual consumer need. macOS/Arm64, Windows Arm64, and additional deployment forms remain experimental until observed on target.
- [ ] Perform the disposable management-target reboot/reconnect gate only on a target explicitly placed under that lifecycle. Keep parser/metadata inspection separate from source execution and live provider effects.
- [ ] Exercise long/non-ASCII/space-containing paths, clean machines, offline resources, tampering, interrupted operations, runtime servicing, uninstall, and side-by-side versions. Verify packed generated projects do not acquire ambient build configuration or unintended network inputs.
- [ ] Carry explicit package authorship/license/source metadata and dependency notices through packaging; do not treat a default metadata value as evidence about the input source. Record signing authority, immutable hashes, and servicing policy for the release set.

**Exit gate:** every advertised profile has an obtainable release set, clean install/upgrade/rollback, and executed workload evidence. Missing optional profiles narrow the matrix. Private-host compatibility failures or unresolved package correctness findings block affected promotion.

## Milestone 30 — Accelerate proven workflows

**Status: Planned.**

- [ ] Select hot workflows from M24–M27 and retain startup, cheap-command, and error-path controls. Compare only modes implementing the same observable contract.
- [ ] Use canonical IR/lowering and the boundary profiler to reduce crossings and allocations. Keep support eligibility independent from a hoped-for speedup.
- [ ] Record fresh-process startup, warm throughput, allocations, peak working set, build/import time, and artifact size with pinned source, toolchain, host, target, run ID, and validated output. Include large-function/many-region compile-time and memory budgets so safe-suffix exploration cannot grow without measurement.
- [ ] Report wins, ties, and regressions against declared budgets. Prefer another execution mode when compilation overhead dominates.
- [ ] Consider another backend only after a measured C#/Roslyn/MSBuild limitation blocks a required outcome and the alternative can consume the same lowered contracts.

**Exit gate:** three unrelated qualified workflow families have repeatable measurements and declared budgets. No language-wide speedup claim follows from a selected workload.

## How to choose and close the next implementation slice

1. Fix accepted-code defects first; reproduce the original/generated failure and inspect sibling paths.
2. Choose a complete blocked workflow from immutable source with every co-blocker visible. Do not import or execute external administration code merely to analyze it.
3. Implement the smallest contract in the existing owner. Group by semantic shape, ownership, effects, and lifetime rather than function name or one CLR type at a time.
4. Validate real artifacts on claimed hosts, including negative/failure paths. Report values, types, identity, streams, errors, continuation, cleanup, and actual retained boundaries.
5. For hard-to-observe changes, freeze a candidate for one independent read-only review, then resolve validated findings and run focused confirmation.
6. Record exact evidence and remaining limitations in the assessment. Update only current roadmap state, preserve user-owned work, and clean task-created heavy outputs before handoff.

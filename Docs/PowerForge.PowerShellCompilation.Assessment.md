# PowerShell compilation readiness assessment

Updated: 2026-09-22. M28 grouped diagnostics implementation: `a30e72b79a579ed3d5796f6833c2161b8d4569b4`. M28 NuGet packaging implementation: `7ba90d82352152195472e5526e57fc4f18adbdbc`. M28 run/watch implementation: `3823979bd305cb530fd5586e75eff46dc49f5db8` on `feature/powershell-compiler`. Earlier audit baseline: `419620a88000a714cfb4450821a02fb8e5364583`; corrections: `18e443a1401611e7e144c39cc52b27f3e9c25ab0`; stopping-fixture consolidation: `3cfa5edadbf0a9352924c2d45d9e017776d65ed1`.

The compiler has a substantial semantic architecture and useful bounded Hybrid/Strict workflows. The September 22 audit findings are closed with implementation and artifact evidence. M28 now includes executable run/watch, Strict library NuGet packaging, and grouped project diagnostics through existing owners and thin CLI surfaces. Observed debugging, the module quickstart, and additional host qualification remain open. Integration, public distribution, and additional platform qualification remain separate work.

The independent read-only audit covered the three latest baseline implementation commits (`bc925783a`, `c589eea3c`, `419620a88`) and surrounding contracts. Primary inspection also covered target policy, native host bridges, the compiler gate, CI wiring, and roadmap consistency. This is not an exhaustive audit of all 975 changed files in the continuation. One targeted read-only confirmation of the corrective implementation reported no actionable P0–P3 findings.

## Findings and corrective evidence

### R1 — P2: package cancellation and replacement — corrected

Owner: [library package builder](../PowerForge.PowerShell/Services/Compilation/PowerShellCompilationLibraryPackageBuilder.cs) and its [I/O and publication boundary](../PowerForge.PowerShell/Services/Compilation/PowerShellCompilationLibraryPackageBuilder.IO.cs).

At the baseline, the final token check preceded hashing and archive creation. Cancellation during those stages could replace an existing package and return success. Source control flow demonstrated the defect; the original test only canceled before entering `Build`.

The corrected owner checks cancellation during snapshot copying, identity hashing, and archive entries. It hashes the completed temporary package and checks the token immediately before atomic move/replacement. Rebuild-workspace cleanup happens before publication. There are no cancellation checks or destination reads after the commit point. Cancellation observed before commit preserves the destination; a later cancellation cannot roll back a committed package.

Ten publication cases cover cancellation during/after writing, creation/replacement, writer failure, Windows destination contention, cleanup failure preserving the original exception, and successful bytes/hash. Cleanup failures retain diagnostics on the primary exception; inaccessible temporary files cannot be promised to disappear. Ordinary package consumers exercise the real archive/rebuild path too.

### R2 — P3: invalid NuGet metadata — corrected, including provider siblings

Owner: [shared compiler package identity policy](../PowerForge/Services/PowerShellCompilation/PowerShellCompilationPackageIdentity.cs), used by the library packer and provider reader.

Live probes showed the baseline library validator accepting `1.+2.3`, `1. 2.3`, `1.2.-0`, and `Valid..Package` while NuGet rejected them. The sibling provider/dependency validator used `Version.TryParse`, which accepted the same malformed version components.

Both routes now use NuGet's package-ID rules, including its length limit, and canonical stable `x.y.z` versions. Spacing/signs/leading zeros, fourth components, prerelease labels, and build metadata are rejected. Invalid library metadata fails before rebuild. Tests verify malformed library/provider/dependency metadata cannot replace prior output; NuGet reads valid metadata and ordinary consumers restore the package.

### R3 — P2 validation: incomplete recurring gate coverage — corrected

Owners: [compiler gate](../Build/Invoke-PowerShellCompilerGate.ps1), [ABI compatibility tests](../PowerForge.Tests/PowerShellCompilationAbiCompatibilityTests.cs), and [provider execution tests](../PowerForge.Tests/PowerShellCompilationProviderExecutionTests.cs).

Baseline discovery selected zero ABI compatibility cases through the gate category, although the class passed 9/9 explicitly. The M27 sweep also found six existing provider-consumer/lifecycle/dependency cases outside the bounded gate. All are now included, along with new metadata/publication regressions. The gate requires passing results from ABI, publication, library-packaging, and provider families so an omitted family cannot hide behind a nonzero total. This checks family presence, not completeness inferred from a fixed test count.

### R4 — planning: conflicting completion and support claims — corrected

The plans distinguish bounded semantic implementation, local artifacts, integration, released packages, and target execution. Stale checkpoint journals are removed. Generated libraries/modules support `net472` and `net10.0`; EXEs support `net10.0`. The shared host library independently retains `net8.0` for consumers. Static ABI manifests use schema 4; stateful manifests use schema 5.

### R5 — Windows PowerShell 5.1 loop stopping — fixture race corrected

The baseline gate passed 1,062 cases and failed one, with zero skips, in 42m15s. The Strict corpus did not run after the failure. The `CaptureTypedArray` observation in `LoopStopping_PreservesInterruptBoundariesAndFinally` differed: original `[1,1,200,[7,7]]`, compiled `["prior",100,100,101,102]`, both reporting `Stopped`. The unchanged isolated case subsequently passed both hosts (2/2).

The fixture treated public state `Stopping` as engine acknowledgment. A probe on the actual Windows PowerShell 5.1 host observed that state before the engine's stop flag in **98/100 trials**. This matches the sequencing in the upstream [PowerShell stop implementation](https://github.com/PowerShell/PowerShell/blob/v7.6.5/src/System.Management.Automation/engine/hostifaces/PowerShell.cs) and [local pipeline worker](https://github.com/PowerShell/PowerShell/blob/v7.6.5/src/System.Management.Automation/engine/hostifaces/LocalPipeline.cs).

The [test](../PowerForge.Tests/PowerShellCompilationLoopStoppingTests.cs) now waits for native `CurrentPipelineStopping` before releasing either invocation. A sibling sweep found the same handshake in seven other paused fixtures. All eight now use one [test helper](../PowerForge.Tests/PowerShellCompilationStoppingProbe.cs); the separate infinite-enumeration test already waits for actual stop completion and needs no release handshake. The shared helper passed 100 handshake probes on each host with zero early acknowledgments. Timeout/capability failures, exact original/compiled comparisons, and every loop/capture/finally assertion are retained. No runtime semantics or arbitrary delay was added. Final artifact-fixture qualification is recorded below.

## M28 executable development workflow

The [development guide](PowerForge.PowerShellCompilation.Development.md) covers source-first `run` and `watch`, application arguments and streams, offline restore, and exact-source qualification before distribution. The implementation reuses the project workflow service, dependency planner, artifact builder/cache, receipt verifier, and owned process runner. It adds no second compiler, package resolver, or process-lifetime implementation.

Already-declared local script/resource content may change against the reviewed dependency graph. Topology, module metadata, binary/provider identities, semantic profiles, and targets still require explicit lock/restore. Development artifacts record their effective source graph and reviewed baseline; edited source is not labeled fully reviewed. Normal build/test/pack retains exact-source lock validation.

Watch debounces content changes, cancels and joins the preceding build/application tree before restarting, and recovers after invalid source. Failed/canceled builds never fall back to old output. Input discovery comes from declared files and the existing resolver/planner; unrelated scripts do not invalidate the run. Application argument vectors, inherited stdin/stdout/stderr, and exit codes remain intact.

**R6 / P2 — launch integrity, corrected.** Independent inspection found that the public `starting` callback followed artifact validation, while the final launch check covered only inputs. A callback or concurrent producer could replace output before execution. The final boundary now compares the disk inventory with this attempt's previously authenticated inventory, without accepting a replacement receipt. Regression cases alter a runnable executable and its separate generated assembly during `starting`; both fail before any process starts. This check does not promise an atomic filesystem-to-process snapshot against an external writer racing OS launch; concurrent writing into the development output is unsupported.

Qualification at the implementation revision:

- All eight final API run/watch cases passed across the remediation runs: six existing development cases plus the two final launch-integrity cases. They cover source/callee/resource invalidation, unchanged cache reuse, invalid source and recovery, rapid edits, provider/profile/target rejection, exclusion of unrelated scripts, late build cancellation preserving prior output, and termination of the application plus its descendant before restart/cancellation. The initial new payload fixture assumed an adjacent resource; it was corrected to the actual generated assembly, and both final integrity cases passed in 1m17s.
- A direct final CLI run passed init → lock → restore → offline restore → run, preserved seven binary input bytes and ten arguments exactly, kept stderr separate, and returned the application's exit code 23. CLI assembly SHA-256: `622f97aaa818f958a5ce19283be073fb1cfda0d668411bd268ff02f86dc19f55`.
- Final `PowerForge.PowerShell` Release `net472` compilation passed with zero warnings/errors. Executable qualification used .NET 10 / Windows x64; these observations do not qualify another OS or architecture.
- One independent full read-only review and one targeted remediation confirmation completed. Confirmation patch hash: `17b7a65502b337c91470865c559f93b8cb01b0d9`; no remaining actionable finding in the inspected remediation. The only later implementation-commit change was milestone prose.

The broad compiler gate passed **1,104/1,104**, with zero failures/skips, in **41m11s**, followed by **6/6 Strict programs** and **24 emitted units** with zero failures/regressions. Its required run/watch/CLI and existing package/provider families passed. This run used the reviewed candidate before launch-inventory and narrower-input remediation (review fingerprint `4e75a8ac65dc6adf72a181928f3677245d0bb6fc`). Gate engine assembly SHA-256: `c8f0f8a188ae89799d152ba063a75e14409bdb19570d1d81773dca3716fa85f1`; test assembly SHA-256: `288b5b5af273647f7a4025e2f28d8cd14da8f13ebe5a62be1c21a958c5e49d09`.

The final focused API tests, direct CLI proof, and net472 build above qualify those narrow production changes at `3823979bd`. Their binaries were built into a separate task-owned directory so the running broad gate retained its original binaries. The final two launch-integrity cases also satisfy the subsequently added family-presence requirement; they were not part of the 1,104-case run. This is combined broad and focused evidence, not a claim that the final source received another full-suite run.

Strict packet `powerforge-public-powershell-modules-net10-v1`, SHA-256 `ef64781013a85a48701dd6334435facce8f8bec5c1ecc1a36817b8e4d300b00e`, ran on `net10.0` / `win-x64`. Evidence JSON SHA-256: `d4f7730d46d3c3c3a0e0d8e2b00ef633d713eb0ee2e2d1067eada8fe558da901`.

| Strict program | Emitted units | Artifact SHA-256 |
| --- | --- | --- |
| Number theory | 3 | `a2f7035b4e56dd3e605f0247033cc6da1ed9b8af4c1a93be3dabd5e58e382fed` |
| Calendar rules | 3 | `b5dc9a468188fbad6b3b8ee42d598236bd03d122d7216b0f78385b16f0da480b` |
| Recursion/local call | 3 | `831427f8c89017329d1280fddcb593f90705018f5a36f1be91ae2873942a13eb` |
| Collection mutation | 2 | `57bc24dd15982eed8dd39e0aea1e9a69abc37aa01d543acf2a6eb990baa4ac38` |
| Switch/control flow | 2 | `5e620892390cc4606307960d54b77297d6d078c36f1bf4f523bfc3695280abae` |
| Multifile application | 11 | `4f6ad817d9cef16739f9959a7c68662de3b554ff555bc7ce20e0a18cc3a1b1eb` |

The recurring gate now requires passing run, watch lifecycle, launch-integrity, and CLI-stream families in addition to the existing compiler/package/provider families. M28 remains partial; its remaining acceptance checklist is maintained in [next milestones](PowerForge.PowerShellCompilation.NextMilestones.md).

## M28 project NuGet packaging

`project pack --format nuget` now delegates one tested Strict library to the existing `PowerShellCompilationLibraryPackageBuilder`. The project supplies explicit metadata and an optional generated-ABI baseline. ZIP remains the default format, and project install continues to consume ZIPs. The [library quickstart](PowerForge.PowerShellCompilation.LibraryPackages.md) covers the complete local-feed consumer path.

Packaging validates the exact build receipt and current test evidence, then uses the isolated project package environment for the independent rebuild. Generated source now retains the exact NuGet lock and enables locked restore. The shared packer verifies that lock against the artifact's recorded hash, owns the rebuild process tree, and preserves existing output when cancellation or validation fails before publication. There is no new packer, dependency resolver, or ABI checker.

Qualification before the broad gate:

- **34/34** focused project API, CLI, library-packaging, ABI, publication, and project-workflow tests passed with zero failures/skips in **2m36s**. Ordinary local-feed consumers built and ran both `net10.0` and Windows `net472` libraries without loading PowerShell. Tests exercised explicit metadata/target selection, required test evidence, emitted lock retention, ABI rejection, artifact tampering, cancellation, and existing ZIP/install behavior.
- **2/2** provider dependency cases passed in **18s**. The Strict case also packages through the project workflow and runs an ordinary consumer using the reviewed adapter and its managed dependency. Changing the provider package blocks repacking and preserves the previous output.
- The documented CLI quickstart ran directly through init → lock → restore → build → test → NuGet pack → `dotnet add package` → consumer run, printing **42**. Its package SHA-256 was `087a391a27ae3d9f6b288a64f0ae53f8d3fe41bfda64a5d00ca53921296752b9`.
- Shared `PowerForge.PowerShell` Release `net472` build passed with **zero warnings/errors**. The repository/compiler gate selects SDK **10.0.303**; the standalone quickstart consumer outside that SDK selection used **10.0.400**. PowerShell **7.6.5**, Windows PowerShell **5.1.26100.9444**, Windows x64.
- One fresh read-only local review (`nuget_project_review`) found no actionable P0–P3 issue in staged patch `a187ea23df9a0499bae3507c8a6b09783568cb52`. The reviewed packaging/integrity boundary is current; no confirmation pass was needed.

The first added provider-project consumer exceeded the Windows consumer toolchain's path-length limit in the deeply nested temporary fixture. The fixture now uses the same compact task-owned root pattern as other artifact consumers. This is not long-path qualification; M29 retains that requirement. NuGet packaging supports one selected framework per package, unsigned Strict input, and restore/rebuild upgrades. Public feeds, signing, binary drop-in upgrades, additional platforms, and general PowerShell compatibility remain separate work.

The full compiler gate passed **1,111/1,111**, zero failures/skips, in **44m09s**, followed by **6/6 Strict programs**, **24 emitted units**, and zero failures/regressions on `net10.0` / `win-x64`. The gate now requires project NuGet consumer and CLI families in addition to existing library/provider/ABI/publication coverage. The implementation is `7ba90d82352152195472e5526e57fc4f18adbdbc`; only evidence and roadmap prose changed afterward.

Gate evidence identities: test TRX SHA-256 `cb3944eeb5968add60ab5cccf5cbdae887a8941d85dddc3d8101bfcb05d51304`; compiler assembly `8df8ac7db77332cd04d18bc1bf24260e1a8147d5b86fa0e99ba5147238b04648`; test assembly `5e53209e3a6a0eb3bfd8968dc36febcd3777ff75cb87cd1a419873e04ae4f5e6`. Strict packet `powerforge-public-powershell-modules-net10-v1` retains input SHA-256 `ef64781013a85a48701dd6334435facce8f8bec5c1ecc1a36817b8e4d300b00e`; result JSON SHA-256 `fd355ca0f3a81534fb82baa4ffd0bac819d766c8888266d7ee8632ee904cf21f`.

| Strict program | Emitted units | Artifact SHA-256 |
| --- | --- | --- |
| Number theory | 3 | `96f427d25ffba1851fdbccb79f4a27ed43e5d827caff4c5fdd86fdbdbcb52a06` |
| Calendar rules | 3 | `ed30c86939ab80bd5bd399746e536b2c04b95ed4d0da7d54446aaf621307c05e` |
| Recursion/local call | 3 | `4dc0fa65441329076aa47e21eb29ae665a2329493a0f78c292eadfccef3a3156` |
| Collection mutation | 2 | `0faed6b75502d774f486325ab0a0067a6b54b27bcf283d4d97bce6b4db40bc7f` |
| Switch/control flow | 2 | `747d99c94f8d39259ca6d88b0483ade8dca98fd7255693dde3cf20468676f47e` |
| Multifile application | 11 | `6bc917641df750de1a4f25a30b2f9aeb89b841947a75ee02cfc3fe4d41e87d1c` |

No PR, GitHub CI run, merge, or public package release was created for this goal. Local implementation and qualification do not replace M29 integration/release gates. Task-owned transient outputs are removed after this evidence is recorded.

## M28 grouped project diagnostics

The [project diagnostics guide](PowerForge.PowerShellCompilation.ProjectDiagnostics.md) explains per-function decisions, source coordinates, retained/rejected routes, and links from the existing bound local-call graph. The report projects canonical analysis and final shaping evidence; it adds no parser, eligibility engine, or call discovery by message text. Project Explain now returns the final shaping status rather than the earlier semantic plan's status. Both binary-module and executable shaping receive the same resolved providers as analysis.

Diagnose combines that report with the existing authenticated build-receipt, lock, environment, and artifact-inventory checks. Source readiness and operation success remain separate: a valid source report cannot make a missing or altered artifact pass. Input, target, dependency, semantic, shaping, and integrity failures retain their owning stage. Synthetic calls without exact authored coordinates remain explicitly unmapped. Neither command executes authored source, imports its module, rebuilds output, or refreshes reviewed locks.

**R7 / P2 — nested diagnostic identities, corrected.** Independent review found raw Windows relative paths being hashed in the new report lookup and local-call links, while explanations and disposition ledgers normalize separators. A direct CLI probe with `Public/Inner.ps1` reproduced an unstructured exception. Those sites now use the existing explanation owner's normalization. The bounded sweep found and corrected the same pre-existing mismatch in three runtime failure-map lookups, covering compiled methods, promoted regions, and retained units. Established explanation/ledger IDs and semantic fingerprints remain unchanged; newly generated failure maps use matching IDs. Existing artifact evidence remains authenticated by its recorded hashes.

**R8 / P2 — acquired package environment, corrected.** Independent review found the new diagnostic analysis using the global package root before receipt verification used the acquired project environment. Both diagnostic commands now validate and use that acquired environment when present. Invalid evidence becomes a dependency issue while source inspection is still attempted. The existing receipt check remains independent. An internal callback at the dependency-planning boundary classifies runtime-pack resolution failures without inspecting exception text.

Focused qualification:

- Initial API, CLI, explanation, provider, and artifact-diagnostic coverage passed **20/20** in **2m28s**, followed by focused provider and report-failure checks. These runs preceded the two review corrections.
- Final correction coverage passed **10/10**, zero failures/skips, in **51s**: grouped Strict/Hybrid decisions, source and integrity separation, invalid target/input/provider/environment evidence, nested local-call identity, nested fallback/compiled failure maps, and real CLI output. The self-contained Package executable was restored and built, then Explain and Diagnose both succeeded in child processes with an empty global NuGet cache. Removing acquired evidence produced a dependency-stage runtime-pack failure.
- The provider closure cases passed **2/2** in **20s** after giving the separate executable fixture its own project directory. Sharing an already-restored directory between two manifests correctly fails environment identity validation. This fixture-only adjustment followed targeted confirmation; production code did not change.
- Final shared host Release `net472` build passed with zero warnings/errors. The CLI example directly reported the shaping blocker and local call at **7:5**; the final nested CLI probe retained a structured result and matching callee ID.
- One full independent read-only review found R7 and R8; one targeted confirmation found no remaining actionable issue in the fixes and their normalization siblings. Confirmation patch fingerprint: `cfca96961fad73b7b5c15adce2232ceb19d9c1f3`.

Compiler tests at `a30e72b79a579ed3d5796f6833c2161b8d4569b4` passed **1,121/1,121**, zero failures/skips, in **48m49s**, with all required families present. The following Strict corpus produced the expected results for **6/6 programs** and **24 emitted units**, but the wrapper returned failure on two multifile startup budgets: artifact median/p95 **426/658 ms**, against **250/500 ms**. One corpus-only confirmation with the same compiler binaries, six alternating source/artifact samples, and unchanged limits passed **6/6**, with zero failures/regressions and artifact median/p95 **77/102 ms**. This is combined compiler-test and corpus-confirmation evidence; the first end-to-end invocation was not fully green. No compiler or gate limits changed between runs. Earlier superseded runs were stopped after review remediation and a provider fixture failure rather than represented as passing evidence.

Timing context: Ryzen 9 9950X3D2, 16 cores/32 logical processors, Windows 11 build 26200, High performance plan, Normal runner priority, all 32 processors eligible, and no domain pinning. Read-only load snapshots around confirmation were 80–99%; confirmation still passed. Load and scheduling are plausible contributors, not a demonstrated cause. These workstation measurements do not certify stable CI or cross-domain performance; M29 retains explicit runner-budget qualification.

Evidence SHA-256 identities: compiler assembly `c6f7a8939ee9ac7ac267358cdfa534aa5d51788d6ea2cc228f2ef0f92cfff6a7`; test assembly `d6e3d78086e238508ca9abb91932d5e5336c66e2ef8a672eb23ecc7a3d3123bd`; CLI `ba6b423a0d3f835ade48dcfba5b74d2f2690742aadb98e828af433b30f8c274d`; test TRX `55a0d73bbcb0f46b04a5080bfb6a9769d45260e794046b961415ae2d95145682`. Strict packet `powerforge-public-powershell-modules-net10-v1` retains SHA-256 `ef64781013a85a48701dd6334435facce8f8bec5c1ecc1a36817b8e4d300b00e`. Initial corpus JSON: `5aefcd64240871d26e8dd882c3f5b0bcf0b029c35cb7590c473a481c700b010b`; confirmation JSON: `d1ffdc363b3dd9d93c579cacadf2bbaa1869a19862c7bee4f51b75be25867d94`. These outputs are summarized here before task-owned transient files are removed.

A separate qualification probe encountered a plain Strict self-contained Windows x64 closure rejection before diagnostics: unoptimized, non-single-file `net10.0` `return 7` restored and published, then the existing verifier rejected a `System.Private.CoreLib, Version=0.0.0.0` reference. No closure rule was weakened. M29 explicitly tracks identification of the requesting assembly and target execution before that deployment form is promoted. Package-mode diagnostics do not qualify this Strict form. General source debugging and additional host qualification remain M28 work.

## Design and compatibility boundaries

| Area | Decision | Boundary |
| --- | --- | --- |
| Semantic ownership | Keep parser/binder/bound IR/analysis/lowering/backend and shared artifact owners | No second eligibility implementation in emitters, census, or surfaces |
| Hybrid runtime | Keep one live PowerShell state/lifecycle owner | Emission may call PowerShell extensively; it does not prove runtime-free execution or speedup |
| Strict runtime | Preserve separate certification and instance/lifetime contracts | Reject unproved dependencies/semantics; no hidden runspace |
| Region transfer | Preserve shape, direction, ownership, mutation, output, and continuation evidence | Fresh vectors differ from borrowed references; expand with complete-workflow proof |
| Host servicing | Reuse host semantics through explicit native contracts | Private API updates need exact-host qualification and actionable capability failures |
| CLR consumption | Keep independent rebuilds, local feeds, and ordinary consumers | Generated ABI comparison does not certify package/assembly replacement |
| Providers | Keep metadata-only analysis and locked adapters | Isolated/in-memory providers do not qualify live administration or destructive lifecycles |
| Performance | Keep canonical IR/profiler owners | Measure equivalent workflows after correctness; no universal speedup claim |

**Current package upgrade contract:** select the package identity/version and target explicitly, restore, and rebuild the consuming project. Provider dependencies remain locked. The optional generated-ABI baseline compares API and module lifetime; it does not certify CLR binding, package-ID, TFM, or binary drop-in compatibility. Replacement of an already-loaded assembly or running application is unsupported. If binary drop-in upgrades become a requirement, M29 must define assembly/dependency identity and prove an already-compiled consumer separately from a rebuilt consumer before advertising that guarantee.

No P0/P1 defect, runtime-free certification bypass, or incorrect-transfer regression was established in the reviewed changes. That scoped result is not a guarantee about the whole compiler.

## Earlier September 22 audit qualification

- Final focused suite at `3cfa5edad`: **55/55 passed**, zero failed/skipped, in **2m05s**. Includes all eight stopping fixtures (18 host/shape cases), publication failures, library/provider metadata, ABI, and provider consumers. The initial corrective suite passed 39/39 before consolidating the sibling fixtures.
- Final `PowerForge.PowerShell` Release `net472` build: **zero warnings/errors**.
- Canonical compiler gate at `18e443a14`: **1,097/1,097 passed**, zero failures/skips; test phase **45m48s**. Required ABI/package/provider families passed their presence checks.
- Strict corpus at the same production revision: **6/6 passed**, zero failures/regressions, **24 emitted units**, `net10.0` / `win-x64`.
- Toolchain: .NET SDK **10.0.303**, PowerShell **7.6.5**, Windows PowerShell **5.1.26100.9444**, Windows x64; canonical xUnit concurrency **4**.
- Targeted review: staged 11-file patch fingerprint `20bec86f1f25ace64fe67afba52918b4ae768ced`; no actionable findings. Read-only review did not substitute for executable checks.

The full gate ran against the production correction commit. The subsequent commit changes only stopping-test setup and was validated by the final 55-case run, including all affected artifact fixtures. Production code did not change between those runs. The focused run supplements the recorded full gate; it is not described as another full-suite run.

Canonical validation:

```powershell
pwsh -NoProfile -File ./Build/Invoke-PowerShellCompilerGate.ps1 `
    -EvidencePath <task-owned-output-folder> -RuntimeIdentifier win-x64
```

The gate runs compiler-tagged tests plus census, bound-pipeline, and fuzz tests, then six Strict programs. The VSTest/xUnit assembly targets `net10.0`; artifact tests select separate host processes. Use `-NoBuild` only after rebuilding the current test project. A passing net10 test assembly alone does not establish net472 compilation or a complete platform matrix.

The corpus invocation selected Strict programs only; module qualification comes from separate artifact tests. The latest program identities are recorded in the M28 section above.

At audit start the clean continuation and remote head matched `419620a88`; a fresh fetch found 133 continuation-only commits and 11 default-branch-only commits. There was no open PR or GitHub run. `BuildModule.yml` runs on main pushes, PRs to main, or manual dispatch; a continuation push alone provides no CI proof. Both baseline and corrective local gates exceeded the workflow's 40-minute compiler-step timeout: measure the candidate on its actual runner and adjust partition/budget if needed without dropping required cases.

No public package, branch integration, PR, Linux/NativeAOT, broad live-management, or additional physical-target qualification was performed in this correction. Those remain M29 gates. Task-owned transient validation outputs are removed after results are recorded here.

## Prior evidence and next work

The September 20 checkpoint recorded 1,063 passing compiler cases, six Strict Windows programs, and a clean net472 build. Its pinned PSSharedGoods census recorded **188/282 complete emitted functions and 36 promoted regions** across 284 source files/286 units. These are dated input-specific observations, not language-coverage percentages. The older heterogeneous 6/196-unit packet and PowerShell 7.4/net8 observations have different inputs and support boundaries.

Continue **M28** through existing project/cache/diagnostic/package owners. **M29** owns integration, host servicing, public distribution, upgrade lifecycle, and target qualification. **M30** measures workflow benefit after correctness. The [next milestones](PowerForge.PowerShellCompilation.NextMilestones.md) own the active checklist; the [architecture roadmap](PowerForge.PowerShellCompilation.Roadmap.md) owns design history.

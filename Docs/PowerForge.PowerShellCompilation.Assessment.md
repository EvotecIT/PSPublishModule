# PowerShell compilation readiness assessment

Updated: 2026-09-22. Audited baseline: `419620a88000a714cfb4450821a02fb8e5364583`. Production corrections: `18e443a1401611e7e144c39cc52b27f3e9c25ab0`. Final stopping-fixture consolidation: `3cfa5edadbf0a9352924c2d45d9e017776d65ed1` on `fix/compiler-audit-milestones`.

The compiler has a substantial semantic architecture and useful bounded Hybrid/Strict workflows. The September 22 audit found package cancellation, metadata validation, recurring test-selection, and stopping-test synchronization problems. All findings are closed with implementation, full compiler/Strict corpus validation, and a final focused fixture run. M28 is the next development milestone. Integration, public distribution, and additional platform qualification remain separate work.

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

## Current validation and branch state

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

Strict packet: `powerforge-public-powershell-modules-net10-v1`, SHA-256 `ef64781013a85a48701dd6334435facce8f8bec5c1ecc1a36817b8e4d300b00e`. The run selected Strict programs only; module qualification comes from separate artifact tests, not this corpus invocation.

| Strict program | Emitted units | Artifact SHA-256 |
| --- | --- | --- |
| Number theory | 3 | `5b3b10e93d90e729a05867d9c4f8a77b4a0ea6cea05551267f2d3130ec7a4ba5` |
| Calendar rules | 3 | `cbda476e155835c5f438c3e9edf2764b7a7e499725a4c4985936b18c3fe24d4f` |
| Recursion/local call | 3 | `05c570200c6dda260a657af1791b904e9f9d366713da9c1175e4b337a1549b2a` |
| Collection mutation | 2 | `21d275922d71f3320a46beddfba78e594da49e39579773613cf5110b3f67b354` |
| Switch/control flow | 2 | `440f8ab2687e213b955d2b0ee38fcccf5f76b2db46fbc9e9f012ae8903260bf9` |
| Multifile application | 11 | `5a2a85f92592069f2c4d825a70e59d97f7872a7069ec68b3b822ff4ea6de51c5` |

At audit start the clean continuation and remote head matched `419620a88`; a fresh fetch found 133 continuation-only commits and 11 default-branch-only commits. There was no open PR or GitHub run. `BuildModule.yml` runs on main pushes, PRs to main, or manual dispatch; a continuation push alone provides no CI proof. Both baseline and corrective local gates exceeded the workflow's 40-minute compiler-step timeout: measure the candidate on its actual runner and adjust partition/budget if needed without dropping required cases.

No public package, branch integration, PR, Linux/NativeAOT, broad live-management, or additional physical-target qualification was performed in this correction. Those remain M29 gates. Task-owned transient validation outputs are removed after results are recorded here.

## Prior evidence and next work

The September 20 checkpoint recorded 1,063 passing compiler cases, six Strict Windows programs, and a clean net472 build. Its pinned PSSharedGoods census recorded **188/282 complete emitted functions and 36 promoted regions** across 284 source files/286 units. These are dated input-specific observations, not language-coverage percentages. The older heterogeneous 6/196-unit packet and PowerShell 7.4/net8 observations have different inputs and support boundaries.

Continue **M28** through existing project/cache/diagnostic/package owners. **M29** owns integration, host servicing, public distribution, upgrade lifecycle, and target qualification. **M30** measures workflow benefit after correctness. The [next milestones](PowerForge.PowerShellCompilation.NextMilestones.md) own the active checklist; the [architecture roadmap](PowerForge.PowerShellCompilation.Roadmap.md) owns design history.

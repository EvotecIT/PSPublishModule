# PowerShell compiler readiness assessment

Assessed: 2026-09-06. Source: `045d9ccabaf1d02b06e5569c25b66548e6eec5da` on `origin/main`. SDK: `10.0.303`; local host: Windows x64, PowerShell `7.6.5`.

PowerForge has a real semantic compiler and useful bounded DLL/EXE paths. Its parser, binder, bound IR, lowering, C# backend, hosted boundaries, dependency locks, and artifact certification are the right foundation. The next investment should be accepted-code correctness and useful workload coverage. Another backend or a larger list of nominally supported constructs would not address the defects and measurement gaps found here.

This assessment found two reproducible semantic defects in accepted code. They block a broad correctness or stable-release claim. Both should be fixed before further arithmetic or region-promotion breadth. Discovery of additional source repositories can continue immediately because it need not execute or promote their code.

## Findings requiring action

### P1: Single-precision arithmetic changes both the value and public return type

The compiler accepts this function for a Strict CLR library:

```powershell
function Get-SingleSum {
    param([single] $Left, [single] $Right)
    return $Left + $Right
}
```

For arguments `[single]16777216` and `[single]1`, PowerShell returns `System.Double` with value `16777217`. The generated DLL returns `System.Single` with value `16777216`. A second accepted function using `*` also returns `Single` where PowerShell returns `Double`.

The arithmetic binder selects `TryUnify(leftType, rightType)` for addition, subtraction, and multiplication. That preserves `Single`, and the backend performs the operation at that precision. This is a silent value and ABI defect, not merely different display formatting.

Owner: [PowerShellOperatorSemanticBinder.cs](../PowerForge.PowerShell/Services/Compilation/Binding/PowerShellOperatorSemanticBinder.cs), particularly arithmetic result selection. Fix the bound operand conversions and operation type together; changing only the generated return type would retain the premature rounding. Sweep subtraction, multiplication, mixed numeric operands, overflow/non-finite inputs, typed compound assignments, and constant versus parameter inputs. Verify both returned value and CLR type against each advertised semantic profile.

### P1: Typed arithmetic overflow selects a different authored catch block

```powershell
function Get-OverflowOutcome {
    param([int] $Value)
    try { $Value += 1; return 'success' }
    catch [System.OverflowException] { return 'overflow' }
    catch [System.InvalidCastException] { return 'invalid-cast' }
    catch { return 'other' }
}
```

At `[int]::MaxValue`, PowerShell returns `invalid-cast`; the accepted Strict CLR library returns `overflow`. A `[byte]` parameter with `$Value++` at `255` produces the same mismatch. Adding `[CmdletBinding()]` and building a Strict `net8.0` binary module also reproduces the difference when the original and generated commands are invoked in separate processes.

Owner: mutation binding/lowering and [PowerShellBoundCSharpBackend.cs](../PowerForge.PowerShell/Services/Compilation/Backends/CSharp/PowerShellBoundCSharpBackend.cs). The generated checked CLR mutation leaks `OverflowException` into authored exception handling. Define the arithmetic/conversion error contract in the semantic owner and lower it consistently. Do not globally rewrite every `OverflowException`: an explicitly called CLR method may legitimately throw one. Sweep increment/decrement, compound operators, destination widths, value preservation after failure, and hosted/runtime-free artifacts.

The existing arithmetic fuzz test uses small positive `Int32` inputs and four compound-operation forms. Its green result does not cover these numeric-boundary or exception-selection contracts.

### P2: Census does not measure the requested executable artifact

This standalone script successfully builds as a Strict EXE and prints `42`:

```powershell
param([int] $Value = 41)
$Value += 1
return $Value
```

For the same file, `powershell census --framework net10.0 --output json` reports one unit, zero compilable units, and one runtime-fallback unit. The recursive census calls `TranspileForBinaryModule`, applies binary-cmdlet shaping, and builds a `BinaryModule` disposition ledger even for `.ps1` inputs.

Owner: [PowerShellCompilationCensusRunner.cs](../PowerForge.PowerShell/Services/Compilation/PowerShellCompilationCensusRunner.cs). This is usable binary-module evidence but insufficient EXE evidence. The MicrosoftIntune scan consequently reports six `binary-module.cmdlet-shape` blockers while the intended consumer is a script executable. Add explicit artifact/mode/semantic-profile selection and use the corresponding canonical final shaping path. Keep the existing module baseline distinct from a new executable baseline; do not reinterpret old numbers.

### P2: One unsupported input aborts a multi-input census

A batch containing 232 SamErde scripts exits with an error at the first nonliteral dot-source expression and returns no successful product rows. Per-file isolation recovers 230 successful assessments and two explicit staging failures:

- `General/Install-PowerShellAsDotNetTool.ps1`, line 33;
- `General/Show-BuildMermaid.ps1`, line 89.

Both failures report that dot-sourcing must use a literal `$PSScriptRoot` path for portable Hybrid staging. Rejecting an unresolvable staged dependency is appropriate. Losing the rest of a discovery batch is not useful for coverage planning.

Owner: `PowerShellCompilationCensusRunner.Run` and its result/CLI contract. Report per-input acquisition, parsing, resolution, analysis, and emission status; preserve successful rows and keep the overall failure visible. A discovery lane may record an unresolved dependency without importing it. Do not relax the build's containment or dependency-closure policy.

## Validation and release gaps

The checked-in automatic PR workflows build compiler projects but do not run the `PowerForge.Tests` compiler category or either compiler corpus runner. `BuildModule.yml` runs net472 smoke/Pester tests and selected publishing/Cloudflare C# tests. The separate C# workflows select DotNetPublish and server-recovery tests. Those are useful checks, but they do not protect these compiler semantics.

Add a bounded Windows compiler PR gate covering numeric/conversion/error contracts, hosted state, region promotion, artifact integrity, and one Strict executable plus one library consumer. Keep the larger pinned corpus and target-host matrix in a separately budgeted gate. Report missing pinned hosts as skipped or unavailable evidence, never as an ordinary passing return. Native-Linux tests still need the capability partition already recorded in Milestone 22; this assessment did not rerun Linux or NativeAOT certification.

Public-feed install, upgrade, rollback, provider acquisition, and clean-consumer qualification remain a separate release gate. This assessment establishes current source behavior, not the contents or availability of a public compiler release. The disposable management-target reboot/reconnect case and macOS/Arm64 certification should remain separate target/provider work; they need not block analysis-only corpus growth or fixes to generic language semantics.

## Current product position

| Outcome | What exists | What controls readiness |
| --- | --- | --- |
| Package EXE | A managed host that carries source and a PowerShell runtime | Dependency, host, and delivery compatibility; it provides packaging value without semantic acceleration |
| Hybrid binary module / managed EXE | Typed methods and explicit hosted or retained source paths | Compatibility is useful now; meaningful acceleration still needs coarse-region execution and crossing-cost proof |
| Strict CLR DLL | Callable generated static CLR methods, public ABI evidence, and runtime-free closure checks | The arithmetic and authored-error defects above are open; successful DLL creation alone is insufficient |
| Strict managed EXE | Real generated programs, reviewed dependencies/resources, and target execution | The four Windows corpus programs pass; broader scripts need executable-specific measurement and qualification |
| Strict NativeAOT EXE | The same runtime-free program lowered through a target-specific native backend | Existing Windows/Linux qualification remains separately dated; NativeAOT neither expands semantics nor repairs these defects |

This is enough foundation to continue toward a mature scripting toolchain. The difficult remaining work is predictable semantics across real program boundaries, an honest project/diagnostic experience, and measurable benefit. Retain generated C# as the backend: none of the findings requires replacing it.

## What the new repositories tell us

These are discovery results from exact source commits, not complete-program execution or PowerShell-language coverage. External scripts were neither imported nor executed. For script collections, files were separate census inputs; they were not combined into a fabricated module. SamErde's normal `.ps1` inventory excludes its one `*.tests.ps1` file and does not include hidden workflow scripts. CleanupMonster uses its real manifest and discovered module source closure, excluding unrelated examples and tests.

| Source | Revision | Assessed source files | Emitted functions / functions | Emitted units / units | Limits |
| --- | --- | ---: | ---: | ---: | --- |
| [SamErde/PowerShell](https://github.com/SamErde/PowerShell) | `786225096078651d0fcbc535136d074b4c500698` | 230 of 232 submitted | 8 / 282 | 8 / 412 | Two dependency-resolution/staging failures; zero parser-error files among assessed inputs |
| [CleanupMonster](https://github.com/EvotecIT/CleanupMonster) | `f215b32ca86b38d9150629c08f6c0c232bfafb27` | 68 | 2 / 68 | 2 / 69 | One emitted function remains runtime-routed; no import or operation qualification |
| [MicrosoftIntune](https://github.com/EvotecIT/MicrosoftIntune) | `876f8c8602ed3f31d5d5c36ad33818d42b5e9638` | 20 | 1 / 44 | 1 / 64 | Module-shaped census, not an executable success rate |

All successfully assessed inputs had zero parser-error files. None of the three repositories received complete-workload execution credit. The missing SamErde inputs remain in the submitted denominator; they must not silently disappear from a future baseline.

The sources add different evidence:

- **SamErde:** heterogeneous authoring, basic and advanced functions, pipeline/lifecycle behavior, host streams, AD/Exchange/Entra dependencies, loose scripts, and snippets. The most widespread reported families include lifecycle and missing lowering (61 files each), pipeline syntax (52), runtime scope (49), foreach (44), and parameter metadata (32). These labels require minimization: `semantic.lowering.missing` or `syntax.foreachstatement` is not a complete proposed feature contract.
- **CleanupMonster:** module initialization, shared state, object/member access, dependency-heavy reporting, and administration workflows. Its two emitted functions are `Get-CloudCacheComputer` and `New-EmailBodyCloudDevices`; the latter retains a hosted route. Use it to qualify real module boundaries and planning/reporting seams. A deletion workflow is not an unattended benchmark fixture.
- **MicrosoftIntune:** top-level entrypoints, exit-code-driven detection/remediation, registry access, splatting, native tools, architecture detection, and machine state. `Get-ProcessorArchitecture` is the one emitted helper. Measure complete detection scripts on an explicitly controlled machine image after executable census exists; qualify remediation with before/after state and cleanup separately.

Keep discovery inputs external and pinned. Record source paths, hashes, and license/attribution metadata before promoting or vendoring cases; GitHub's repository metadata did not identify an SPDX license for these three snapshots. Add a pure computation/data-transformation family as a counterweight: three administration-heavy repositories alone would over-prioritize command providers and runtime hosting.

## Recommended sequence

### Now: correctness and honest measurement

- [ ] Fix the two accepted-code semantic defects and add differential cases for value, type, selected catch, and state after failure. Existing green tests do not close these findings.
- [ ] Add the bounded compiler PR gate and verify that the selected tests actually execute. Several test files are partial classes with different class names, so filtering by filename can silently omit intended cases.
- [ ] Make census artifact-aware and resilient per input. Preserve the existing seven-workload and public-module baselines.
- [ ] Introduce a separate repository-discovery packet with immutable revisions, source selection, source hashes, per-input failures, dependencies, intended artifact, and execution policy. Reuse the existing acquisition and semantic owners.
- [ ] Minimize repeated blockers into generic semantic contracts and rank complete unlocks/co-blockers, not just diagnostic frequency or raw repository size.

Discovery can start now; this review has completed its first scan. Do not wait for support percentages, a public release, or completion of the management-provider reboot test. Native eligibility should expand only after the correctness findings above are closed or the affected forms are explicitly rejected.

### Next: prove a useful slice on unrelated workloads

- [ ] Choose a repeated bounded scalar/data-flow or pipeline shape exposed by discovery and represent its complete continuation, errors, ordering, and transfers in canonical IR. Continue Milestone 23 through the existing state and region owners.
- [ ] Execute promoted regions inside real commands from at least two unrelated scenario families. Compare original/generated streams, return types, mutations, errors, and cleanup. Candidate counts and retained Pester/Locksmith regions alone do not satisfy this gate.
- [ ] Measure original, Hybrid, and appropriate Strict surfaces separately. Include crossing cost, startup/import, warm throughput, allocations/working set where meaningful, build time, and artifact size. Require a repeatable user benefit for promotion, not an arbitrary emitted-function percentage.
- [ ] Add one standalone script qualification path covering parameters, exit codes, stdout/stderr, delivered resources/dependencies, and execution from a clean directory. Use controlled registry/process boundaries for an Intune-family case; do not substitute a binary-module test.

An internal pilot can begin with specifically qualified programs after these correctness and execution gates. Package remains valuable for broad script delivery; Hybrid remains suitable for explicit hosted compatibility. Neither should be advertised as broad runtime-free compilation.

### Delay until evidence justifies it

- A direct IL backend, native shared-library ABI, general ETS/dynamic-scope reimplementation, and a large new provider catalog.
- More platform promises before exact target-host qualification.
- Automatic acceleration based only on static region counts.
- A stable public toolchain claim before public-package and clean-consumer qualification.
- Compiler special cases for named repositories or rewrites of external scripts merely to inflate compiler coverage.

The next roadmap checkpoint should be a small set of safe, useful, end-to-end workflows with predictable diagnostics. Generic semantic breadth follows the blockers those workflows expose.

## Fresh evidence and reproduction

The existing seven-workload external assessment passed its unchanged baseline: 155 source files, 6/196 emitted units, 6/183 emitted functions, two promoted regions, 59 analysis-only opportunities, zero parser errors, zero regressions, and zero complete-workload executions.

The Windows Strict subset passed 4/4 programs, including the four-source-file application with its reviewed dependency lock, delivered resource, success/failure contract, and startup budget. Its six alternating fresh-process samples recorded source median 303 ms and artifact median 62 ms on this machine. This is bounded packet evidence, not a general compiler speed claim. The public Hybrid module packet, Linux, NativeAOT, and public-feed installation were not rerun in this assessment.

Focused .NET validation completed with **145 distinct cases passed, 2 skipped, and no failures**. The four selections produced 147 passing executions: 8 fuzz/interop, 6 project-workflow, 75 state/index/numeric, and 58 further region/runtime-free/target/conversion/parameter cases; two cases overlapped between selections. Two pinned-host artifact-oracle checks were skipped, so their qualification remains a proof gap. These are focused selections, not a full compiler-suite claim; the two new arithmetic defects were demonstrated by separate artifact probes outside those passing tests.

To reproduce either DLL finding, save the corresponding function above in `probe.ps1`, build a Strict library, and invoke its generated public static method from a CLR consumer. Compare with the original function using the same typed arguments:

```powershell
powerforge powershell build ./probe.ps1 --kind library --mode Strict `
    --framework net10.0 --name Probe --out ./out `
    --allow-unreviewed-dependencies --emit-source --output json
```

The unreviewed-dependency option is for this self-contained diagnostic fixture. Normal project delivery should consume the reviewed dependency lock. For binary-module reproduction of overflow, add `[CmdletBinding()]`, select `--kind dll --framework net8.0`, and invoke original and generated surfaces in separate processes so a same-name source function cannot shadow the generated cmdlet.

The existing packet commands, run against the CLI built from the assessed revision, were:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalAssessment.ps1 `
    -CliAssemblyPath ./PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll `
    -WorkspacePath ./assessment/external -EvidencePath ./assessment/external.json

./Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1 `
    -CliAssemblyPath ./PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll `
    -WorkspacePath ./assessment/strict -EvidencePath ./assessment/strict.json `
    -SkipModules -RuntimeIdentifier win-x64
```

This review changes planning documents only. The confirmed compiler defects remain open until the corrective implementation and its contract tests land.

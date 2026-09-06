# PowerShell compiler readiness assessment

Updated: 2026-09-06. The initial audit used `origin/main` at `045d9ccabaf1d02b06e5569c25b66548e6eec5da`. The corrective implementation and results below are local source evidence until its PR lands. They do not establish public-package availability.

PowerForge has a semantic compiler with useful bounded DLL and EXE paths. This corrective slice repairs accepted numeric behavior, makes coverage assessment describe the requested artifact, and adds repeatable compiler validation. It also adds a small generic remainder/data-flow slice with complete programs from two unrelated computation families. Broad administration scripts still depend heavily on retained PowerShell and external services.

## Corrected contracts

- [x] **Single arithmetic.** The binder widens operands before floating arithmetic and selects `Double`, including unary arithmetic. `([single]16777216) + ([single]1)` now returns `System.Double` with value `16777217`. This fixes both precision and the generated CLR return type.
- [x] **Typed numeric updates.** Bound and lowered mutation contracts preserve constrained integer conversion, promotion, failure state, and authored CLR catch selection. Overflowing `[int]` addition and `[byte]` increment select `InvalidCastException`; exceptions thrown while evaluating the right-hand expression retain their original identity. The sweep covers all eight integral widths, compound updates, increment/decrement, loop iterators, floating operands, and promoted 64-bit products.
- [x] **Remainder.** Same-type integral `%` and constrained `%=` use canonical numeric semantics, including signed minimum remainder negative one. Euclidean GCD, prime counting, and Gregorian leap-year calculations now qualify as complete Strict programs. Mixed integral binary operations and dynamically promoting unconstrained locals remain outside this bounded contract.
- [x] **Artifact-aware census.** `--kind`, `--mode`, and `--semantic-profile` use the shared final shaping owner. A Strict standalone entrypoint is assessed as an EXE, including script-unit emission. Baselines reject different artifact/mode/profile/recursion contracts. The legacy API remains callable; explicit options use `RunWithOptions`.
- [x] **Resilient input assessment.** Unsupported source closure and malformed paths produce `InputFailures` while successful products remain available. Incomplete results retain a failure exit code and cannot satisfy a baseline. Empty and declarations-only Strict EXEs also build and exit without output; their synthetic entrypoint does not inflate authored-unit coverage.
- [x] **Bounded PR gate.** `Build/Invoke-PowerShellCompilerGate.ps1` selects semantic, census, state, artifact, and fuzz contracts and executes all six Strict corpus programs. The Windows product workflow supplies a hash-pinned PowerShell 7.6.5 host, .NET 8 artifact references, a timeout, and uploaded TRX/JSON evidence. Hosted CI remains a separate gate after publication.

PowerShell-specific exception wrappers remain a distinct boundary. Numeric forms inside catches requiring exact `RuntimeException` or PowerShell conversion-wrapper behavior are retained or rejected when that identity cannot be represented. Named local-call observation follows the complete transitive source closure, including callers retained as authored PowerShell, and checks bound numeric operations before rejecting a callee. An unresolved target, including a dynamic call or alias, conservatively retains potentially observed numeric callees while allowing unrelated safe helpers to compile. This prevents a retained caller from observing an incorrectly compiled helper. These fixes preserve the tested CLR catch and state contracts; they do not claim to reproduce every PowerShell error record.

## Measured execution coverage

The Strict acceptance packet expanded from four programs and 18 authored units to **six programs and 24 authored units**. All six original/generated executions passed on Windows x64 and Linux x64, with **24/24 emitted units and zero runtime fallback**. The added number-theory program returns `21|25`; the calendar program returns `1900-2100|49|False|True`.

The existing four-file Strict application still checks delivered dependency/resource identity, success and controlled-failure streams, execution from a clean directory, and six alternating fresh-process samples after excluded warmups. The packet enforces its existing per-target budgets. This is bounded workload qualification, not a PowerShell-language percentage or a broad speed claim.

Numeric artifact tests compare original and generated values, CLR types, catch selection, and state through `net8.0`, `net10.0`, and Windows `net472` binary-module and runtime-free library surfaces. The Linux selection excludes Windows PowerShell 5.1 cases and reports that unavailable capability explicitly. An additional 39 hosted-command and transpiler cases passed on Windows. These focused gates do not replace the full compiler suite or historical NativeAOT/physical-platform certification.

The final bounded gate passed **198 Windows cases** and **194 Linux cases**, with zero failures. The unchanged seven-workload external acceptance baseline also passed: 155 source files, 6/196 emitted units, 6/183 emitted functions, two promoted regions, 59 analysis-only opportunities, zero regressions, and zero complete-workload executions. Direct CLI validation confirmed a failed input returns exit code 1 while preserving the successful product in JSON. Workflow syntax passed `actionlint`.

The public Hybrid ten-module baseline retains its separately dated evidence and was not rerun by this Strict-only gate. Public-feed install, upgrade, rollback, provider acquisition, macOS/Arm64, and the disposable management-target reboot/reconnect case remain separate release or target qualification work.

## Pinned repository discovery

`compiler-discovery.net10.json` is separate from the acceptance baselines. It pins immutable revisions, SHA-256 archives, license status, source selection, artifact kind, semantic profile, and analysis-only execution policy. The runner reuses verified acquisition and canonical census owners. It does not import or execute external scripts.

| Source | Revision | Artifact | Assessed / submitted inputs | Strict emitted units | Hybrid emitted units |
| --- | --- | --- | ---: | ---: | ---: |
| [SamErde/PowerShell](https://github.com/SamErde/PowerShell) | `786225096078651d0fcbc535136d074b4c500698` | EXE | 230 / 232 | 1 / 412 | 8 / 412 |
| [CleanupMonster](https://github.com/EvotecIT/CleanupMonster) | `f215b32ca86b38d9150629c08f6c0c232bfafb27` | Binary module | 1 / 1 | 0 / 69 | 2 / 69 |
| [MicrosoftIntune](https://github.com/EvotecIT/MicrosoftIntune) | `876f8c8602ed3f31d5d5c36ad33818d42b5e9638` | EXE | 20 / 20 | 0 / 64 | 1 / 64 |
| [PowerShell algorithms](https://github.com/dfinke/powershell-algorithms) | `5e1f95b3111adb8c788786f773e63f087ac3d07b` | CLR library | 26 / 37 | 1 / 29 | 1 / 29 |

Each mode submitted **290 inputs**, assessed **277**, and retained **13 failures**. CleanupMonster's one manifest input expands to its real module closure; it is not one source file. Unit denominators describe assessed source only. Failed inputs remain in the submitted-input denominator, and none of these repositories has complete-workload execution credit.

Two SamErde inputs have nonliteral dot-source expressions: `General/Install-PowerShellAsDotNetTool.ps1` and `General/Show-BuildMermaid.ps1`. Eleven algorithm inputs have source-closure failures under the standalone-file selection, including paths outside that selected closure. They remain useful discovery evidence, not newly accepted dependencies. All assessed inputs reported zero parser-error files.

The initial audit's module-shaped script numbers are superseded by this artifact-aware scan. Low emission is still the principal product limit: a helper emitted inside a retained script does not mean the script is compiled. Repository discovery and authored acceptance programs answer different questions and must keep separate denominators.

The algorithm snapshot records MIT licensing; the other three snapshots record an unrecognized SPDX license. No external source was vendored. The new acceptance programs are locally authored generic computation examples.

## Scalar continuation candidate

The continuation candidate adds a bounded prefix that transfers stable scalar locals back to the retained PowerShell function in declaration order. The canonical binder, optimizer, semantic analysis, lowerer, and region graph still decide eligibility. Whole-function selection takes precedence over overlapping regions. Authored nullable constraints survive transfer on both Windows PowerShell 5.1 and PowerShell 7; modeled CLR errors, host effects, unsupported scope, and incomplete value contracts retain the source.

At source revision `dba6175aa`, the Windows gate passed **247 compiler cases and all six Strict programs**. The compiler also built for `net472`, `net8.0`, and `net10.0` without warnings. These are local candidate results, not a merged or published release.

Two complete command workloads passed original/generated output checks but **did not meet the former performance exit gate**. The report workload used the unchanged `CreateColorLegenedReportHTA` function from the pinned SamErde snapshot above, including its file write; the surrounding administration application was not executed. The diagnostic workload called Pester 6.1.0's unchanged `Format-Hashtable2` inside the complete original and generated module contexts, including nested formatting and sorting.

| Complete workload, 300 calls per sample | Original median, CCD0 / CCD1 | Hybrid median, CCD0 / CCD1 | Interpretation |
| --- | ---: | ---: | --- |
| Report generation and file output | 151.74 / 156.98 ms | 153.04 / 153.82 ms | Within the 5% tie tolerance; no demonstrated benefit |
| Nested diagnostic formatting | 69.17 / 65.40 ms | 76.46 / 74.01 ms | 10.5% / 13.2% slower |

The canonical benchmark runner used five warmup and twenty measured samples per lane, rotated engine order, and no outlier removal. Both processor cache domains were measured independently on a Ryzen 9 9950X3D2, Windows build 26200, PowerShell 7.6.5, with matching affinity per comparison and normal process priority. Each report sample checked file-byte identity and absent success output; every formatting result matched the original. Thread allocations were slightly worse for the report and about 0.5% lower for formatting. Neither result demonstrates a useful allocation benefit. These constant-heavy prefixes do not establish that the crossing cost is amortized. That performance result does not block subsequent correctness or coverage work.

Qualification also exposed two independent correctness defects. Project restore now derives SDK properties from the actual artifact template, so single-file executable restore includes the same implicit package closure as build. Strict and Hybrid single-file projects passed lock, online restore, offline restore, build, and execution. Runtime-free targets now reject hosted command tails even when stream-provider capability is enabled. An unchanged algorithm that previously emitted a dispatcher-dependent library while claiming no PowerShell runtime now fails analysis; the old emitted-unit count is not native execution evidence.

## Numeric computation candidate

A bounded numeric representation can keep an inferred Int32 counter in Double storage when literal additive updates preserve its numeric value and every read combines it with an existing Double operand. Direct type observation, boxing, interpolation, wider mutations, opaque observers, and transfer back into retained PowerShell remain rejected. Complete typed bodies can also use the existing region ABI when binary cmdlet shaping requires retaining the authored function header. Neither change introduces another backend.

The unchanged `trialDivision` function from the pinned algorithm snapshot now emits one complete-body helper in a Hybrid binary module while retaining its original function name and parameter header. Original/generated parity covers sixteen inputs, including fractional values, NaN, and infinities, and checks Boolean type and scalar cardinality.

| Complete command, 100 calls per sample | Original median, CCD0 / CCD1 | Hybrid median, CCD0 / CCD1 | Interpretation |
| --- | ---: | ---: | --- |
| Prime input `1000000007` | 49.82 / 46.95 ms | 11.35 / 11.10 ms | 4.39 / 4.23 times faster |
| Composite input `1001` | 4.74 / 4.73 ms | 5.09 / 5.25 ms | 7.4% / 11.1% slower |

Both domains used five warmups, twenty measured samples, rotated order, no outlier removal, and the same host settings described above. Every measured result was validated. The computation-heavy case amortizes the helper call; the cheap case does not. These are workstation timings for one workload family, with no allocation-benefit or general speedup claim. Further coverage and continuation work is governed by semantic correctness; a two-family speedup is no longer required.

The binder also proves bounded Int32 addition and subtraction from operand intervals. A descending loop can refine its counter after a proven initializer and positive guard when the body cannot write or indirectly expose the counter. The fact stays within the loop's cloned symbol state; unproved arithmetic still retains PowerShell execution.

The next workload exposed a separate collection defect: an untyped `@($InputObject)` could wrap an incoming array as one element. Potentially enumerable collection expressions now retain PowerShell execution. A runtime-collection experiment recovered the shuffle's ordinary output shape and showed substantial potential benefit, but failed to preserve continuation after an enumeration error. That experiment was removed and provides no accepted second-family performance result. Broader collection compilation needs a complete value and error contract before qualification.

Final local validation passed **278 compiler cases and all six Strict programs**, including generated-artifact parity on Windows PowerShell 5.1 and PowerShell 7.6, overflow promotion, preceding success output, bounded counters, retained array shape, and enumeration-error continuation. All three compiler frameworks built without warnings. Separate independent read-only reviews covered numeric projection and the later integer-range/collection-retention contracts without actionable findings. These are local candidate results; publication and current-head CI remain separate checks.

## Detection executable qualification

The complete unchanged `Scripts/LocalSecurityAuthority/Detect-LsaProtection.ps1` from the pinned MicrosoftIntune snapshot passed project initialization, lock, restore, build, test, and pack as a framework-dependent Hybrid `net10.0` `win-x64` executable. Its source SHA-256 is `f3c152538dad482adc91a2df860d9eca9ee38981207a433060d476e5e87fe369`. The original PowerShell process and generated executable both exited zero and printed the same enabled-state message, with empty stderr. The observed `RunAsPPL` registry value was 1 before and after execution; qualification did not change the setting.

This proves the enabled-state path on Windows build 26200. It does not qualify disabled, missing-value, denied-access, reboot, or other-platform paths. The executable retains PowerShell execution and has no native computation claim. A compact temporary project directory was required on this Windows host because long-path support was disabled; the earlier deeper package location exceeded the effective path limit. This is a deployment limitation, not a reason to change machine policy automatically.

## Next implementation gate

Correctness and useful coverage take priority over speed. Preserve how the author designed the program, including errors and continuation, and unlock broader scripts/modules through reusable compiler contracts. The earlier two-family performance requirement has been retired; its measurements remain historical evidence, not a prerequisite for adding support.

- [ ] Minimize the remaining lifecycle, pipeline, scope, object/member, and collection blockers into generic contracts. Rank complete workflow unlocks and co-blockers, not diagnostic frequency alone.
- [x] Implement a bounded scalar continuation and compare two unrelated complete commands. Output parity passed; performance did not.
- [ ] Unlock additional real commands or complete workflows and verify original/generated binding, values, types, output shape, scope, ordering, errors, and continuation on supported hosts. Report compiled and retained behavior separately.
- [x] Qualify the complete detection executable's enabled-state path on a controlled Windows target without modifying the registry.
- [ ] Extend detection qualification to absent, disabled, and failure states on a disposable target. Qualify remediation separately with before/after state and cleanup.
- [ ] Keep broader target-host and public-package lifecycle qualification explicit before making a stable toolchain claim.

Retain generated C# as the backend and semantic decisions in the binder, immutable IR, analysis, and lowering. No finding requires another backend, named-repository special cases, or a second eligibility owner.

## Reproduction

Build the CLI and tests from the same checkout with SDK `10.0.303`. Artifact validation also needs .NET 8 reference assemblies/runtime and the selected PowerShell host. On Windows, the numeric matrix includes Windows PowerShell 5.1.

```powershell
./Build/Invoke-PowerShellCompilerGate.ps1 `
    -EvidencePath ./assessment/compiler-gate -RuntimeIdentifier win-x64

powerforge powershell census ./Main.ps1 --kind exe --mode Strict `
    --framework net10.0 --semantic-profile PowerForge.Oracle.PowerShell/7.6 --output json

./Benchmarks/PowerShellCompilation/Corpus/Invoke-CompilerDiscovery.ps1 `
    -CliAssemblyPath ./PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll `
    -WorkspacePath ./assessment/discovery
```

Run the gate on the actual Linux target with `-RuntimeIdentifier linux-x64`; cross-publishing alone is not execution evidence. Discovery can rerun with `-Offline` after verified acquisition. Its JSON records successful products and per-input failures; a completed discovery scan is not an acceptance pass.

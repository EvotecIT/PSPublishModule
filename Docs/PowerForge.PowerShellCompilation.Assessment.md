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

PowerShell-specific exception wrappers remain a distinct boundary. Numeric forms inside catches requiring exact `RuntimeException` or PowerShell conversion-wrapper behavior are retained or rejected when that identity cannot be represented. These fixes preserve the tested CLR catch and state contracts; they do not claim to reproduce every PowerShell error record.

## Measured execution coverage

The Strict acceptance packet expanded from four programs and 18 authored units to **six programs and 24 authored units**. All six original/generated executions passed on Windows x64 and Linux x64, with **24/24 emitted units and zero runtime fallback**. The added number-theory program returns `21|25`; the calendar program returns `1900-2100|49|False|True`.

The existing four-file Strict application still checks delivered dependency/resource identity, success and controlled-failure streams, execution from a clean directory, and six alternating fresh-process samples after excluded warmups. The packet enforces its existing per-target budgets. This is bounded workload qualification, not a PowerShell-language percentage or a broad speed claim.

Numeric artifact tests compare original and generated values, CLR types, catch selection, and state through `net8.0`, `net10.0`, and Windows `net472` binary-module and runtime-free library surfaces. The Linux selection excludes Windows PowerShell 5.1 cases and reports that unavailable capability explicitly. An additional 39 hosted-command and transpiler cases passed on Windows. These focused gates do not replace the full compiler suite or historical NativeAOT/physical-platform certification.

The final bounded gate passed **189 Windows cases** and **187 Linux cases**, with zero failures. The unchanged seven-workload external acceptance baseline also passed: 155 source files, 6/196 emitted units, 6/183 emitted functions, two promoted regions, 59 analysis-only opportunities, zero regressions, and zero complete-workload executions. Direct CLI validation confirmed a failed input returns exit code 1 while preserving the successful product in JSON. Workflow syntax passed `actionlint`.

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

## Next implementation gate

- [ ] Minimize the remaining lifecycle, pipeline, scope, object/member, and collection blockers into generic contracts. Rank complete workflow unlocks and co-blockers, not diagnostic frequency alone.
- [ ] Continue Milestone 23 with one bounded continuation/transfer shape, then qualify promoted regions in real commands from at least two unrelated families. Compare streams, state, errors, cleanup, and benefit after crossing cost.
- [ ] Qualify an administration detection script on a controlled target image, including exit codes, process/registry boundaries, resources, and failure behavior. Qualify remediation separately with before/after state and cleanup.
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

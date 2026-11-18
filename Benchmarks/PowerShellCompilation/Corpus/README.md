# Generic compiler corpus

The [coverage report](../../../Docs/PowerForge.PowerShellCompilation.Coverage.md) records the current public and external results and the commands used to refresh them. `Invoke-PublicCorpus.ps1 -RefreshBaseline` records measured module counts only after the complete module packet succeeds; partial selections cannot refresh it, and Strict timing budgets stay unchanged.

This checked-in corpus is the portable, product-neutral acceptance surface for PowerForge PowerShell compilation. It does not encode module names, command names, paths, or special cases from an external repository.

- `HybridModule` exercises parameter metadata and literal defaults, typed operators, direct self-recursion, safe runtime-state intrinsics, command-result capture, read-only environment access, known object shapes, and mutable list flows. Its current net10 baseline emits all 8 functions without fallback.
- `StrictProgram` is a multi-file script graph that must build as a PowerShell-free typed executable.
- `StrictCollections` and `StrictSwitch` add collection mutation and exhaustive control-flow programs without input-specific compiler configuration.
- `census-baseline.net10.json` records post-emission module coverage, a source fingerprint, and the required final-ledger identity/disposition of every authored function. Its relative product path remains comparable from another checkout root.
- `public-corpus.net10.json` fixes ten unrelated public module packages by URL, version, license, content hash, scenario family, entrypoint, and clean-target probe. Package contents remain external.
- `public-corpus-baseline.net10.json` records the bounded Windows Hybrid and Windows/Linux Strict outcomes, including separate floors for complete emitted CLR units and promoted regions inside retained functions. Its percentages are packet measurements, never estimates of PowerShell-language coverage.
- `external-assessment.net10.json` is a separate, replaceable frontier packet. It pins repository archives, gallery packages, and standalone files by immutable revision and SHA-256, but it does not turn their names or behavior into compiler configuration.
- `external-assessment-baseline.net10.json` records post-emission census results for the pinned frontier. Baseline schema 2 binds every authored function by its final-ledger unit identity and rejects loss of semantic eligibility or complete-function emission; loss of a promoted typed region unless that same function is now emitted completely; newly gained runtime routing; shaping fallback for a previously eligible function; incomplete/duplicate identities; source drift; parser regressions; and aggregate fallback growth. A newly supported function may advance from runtime fallback to an explicitly attributed shaping fallback, but it cannot replace a previously emitted function with a region while aggregate counts remain unchanged. Both packet runners report promoted and retained terminal-region decisions plus analysis-only typed opportunities from the canonical census, including portable evidence and separate cross-workload frontiers. Candidate, opportunity, promoted-region, and emitted-unit/function measurements remain distinct, and low coverage is not treated as successful compilation or execution.
- `m29-portfolio-ledger.net10-net472.json` pins the M29 five-module, two-target per-function dispositions, source provenance, loader closure, license disposition, and co-blocker pairs. Its 30 named execution credits refer to the bounded offline original/generated probes in the assessment; other emitted methods and promoted regions are analysis results only. The external Fibonacci application has a separate functional entry with its generated-host and performance limits.
- `Corpus.Runner.Common.ps1` owns exact-hash HTTPS acquisition, offline cache verification, contained archive extraction, expansion limits, owned child-process execution, portable candidate/opportunity projection, and evidence-only aggregation for both packets.

Run the portable coverage gate from the repository root:

```powershell
powerforge powershell census `
    .\Benchmarks\PowerShellCompilation\Corpus\HybridModule\Generic.Compiler.Corpus.psd1 `
    --framework net10.0 `
    --baseline .\Benchmarks\PowerShellCompilation\Corpus\census-baseline.net10.json
```

Identity-less legacy census baselines with authored functions intentionally fail closed. Regenerate them with `--write-baseline`, then review the source fingerprint, coverage, and every per-function disposition before accepting the replacement.

The wider external-repository census remains useful scale evidence, but every external root is optional and replaceable. Compiler eligibility is based only on generic syntax, type, binding, host-capability, and artifact contracts.

Run the fixed public packet after building the CLI:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1
```

The runner downloads only the exact declared package URLs, verifies SHA-256 before extraction, rejects escaping paths, portable case collisions, and links, and enforces entry-count, per-entry, total-expansion, and compression-ratio limits. It runs the canonical Hybrid census, writes one reviewed dependency lock per input, builds without the compiler build cache, and probes each generated module in a clean child PowerShell process. Census and the delivered artifact ledger must report the same promoted-region count. Evidence contains portable candidate and analysis-only opportunity records plus separate cross-workload frontiers; it contains no absolute source paths and does not reinterpret compiler eligibility. Opportunity records must carry an exclusively typed lowered graph and the compiler's `analysisOnly` marker or the runner rejects them. The census is an explicit assessment pass and can materially increase packet duration for large generated modules; normal `Build-Module` does not run this duplicate analysis. The runner also enforces the checked-in packet identity, clean-import totals, post-emission dispositions, complete Strict-program results, and the selected target-host baseline. The multi-file Strict application additionally consumes a delivered resource, exercises an exact success-stream contract and a bounded controlled-failure contract, excludes one warmup per surface, and records six alternating fresh-process samples. Its per-RID baseline enforces dependency/resource identity, stream hashes and exit behavior, the sampling policy, and conservative performance budgets. Filtered or skipped lanes remain identity checks and cannot rewrite or satisfy the corresponding full baseline. Use `-Offline` after the first acquisition to prove that the packet no longer depends on a package feed. Strict programs are executed directly for the selected RID; run them on the actual target host rather than treating cross-publish as execution proof.

Run the external frontier assessment after building the CLI:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalAssessment.ps1
```

The assessment runner accepts any packet that follows the checked-in schema. It supports exact-hash HTTPS files and ZIP archives, validates archive containment and portable path collisions, and bounds entry count, per-entry bytes, total expansion, and compression ratio. By default it performs acquisition and post-emission census without executing the source; that mode can rerun from its verified cache with `-Offline`. Use `-RefreshBaseline` only when intentionally changing the pinned packet or accepting a reviewed compiler-coverage change.

A refresh records only the current run. `-RefreshBaseline` without `-ExecuteWorkloads` replaces the baseline's execution section with census-only results, including zero attempted or passed workloads. Use both switches when recording complete execution evidence. Failed execution refreshes leave the baseline unchanged and write the requested assessment report with each attempt's outcome.

Add `-ExecuteWorkloads` to execute the packet's reviewed complete-workload contracts through the existing project qualification lane:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalAssessment.ps1 -ExecuteWorkloads
```

This mode reports passed, attempted, and unattempted workloads separately. A complete-workload contract must declare every authored function in the final artifact ledger, require a complete CLR method for each, and observe every function in at least one scenario on the original, built, and installed surfaces. Same-name declarations require `unitId` or `startLine` in both the required-function entry and its execution observation; a name-only observation cannot stand in for several conditional bodies. The GitHub helper-library contract executes all ten functions across ten scenarios. It fixes configuration and time at the importing module boundary, uses an owned loopback HTTP response, and withdraws compiler input before observing delivered artifacts. It does not exercise the GitHub service or every branch. The PowerShell and Scoop installer contracts add four and twenty-four owned functions respectively, each across three scenarios. They invoke the delivered root through a native script-block module adapter: advanced-script binding for PowerShell and the documented downstream loading mode for Scoop. They use verified PowerShell/Scoop/Main ZIP payloads over an owned loopback server, real extraction and file/process behavior, and process-local registry replacements with owned environment paths. The PowerShell contract executes the installed portable host; the Scoop contract creates the actual repositories, shims and configuration and checks locked-file and failure behavior. These are selected Windows installer paths, with no live package-account or global installation qualification. All compiler input is withdrawn during Built/Installed observation. The remaining four workloads have no complete-workload contract and receive no execution credit. `-ExecuteWorkloads -RefreshBaseline` records these results alongside the census and rejects failed attempts or zero attempts. It requires online project restore and cannot be combined with `-Offline`.

Run the external command and artifact checks only when local execution of the reviewed third-party packet is acceptable:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalQualification.ps1 -AllowExternalExecution
```

Command checks build a Hybrid artifact, verify that the packet's named unit was emitted as CLR, load the original and generated surfaces in separate clean child processes, and compare the selected command invocation. The reviewed metadata can select manifest import, direct root-module import, safe dot-sourcing, or the generated typed assembly independently; these are test choices and never compiler eligibility inputs. A passing command check proves only the selected command. Complete artifact checks use the lifecycle and inventory rules below; a complete-workload result additionally requires `completeFunctionInventory: true` and execution observations for every owned function.

Archive-backed workflows acquire and verify their pinned inputs when `-SourceRoot` is omitted. A supplied root is used directly and still checked against every source-input hash. Payload bytes can be cached across workloads, but each workload's extracted source stays separate until its execution finishes. Assessment and execution use the same parsed packet identity; execution rejects a changed packet before running its scenarios.

Packets with `HybridModuleWorkflow` or `HybridScriptApplication` qualification use fresh disposable project roots and the full Analyze/Lock/Restore/Build/Test/Pack/Install lifecycle. These whole-workflow lanes reject `-Offline` before CLI bootstrap, source execution or acquisition; a cached source archive cannot supply a fresh project's authenticated package environment. Run whole-workflow qualification without `-Offline` to acquire and verify that environment. For disconnected use of an existing retained project, complete a normal `powerforge powershell project restore <project>` first, then use `powerforge powershell project restore <project> --offline` to authenticate the saved environment and exact closure locks. Discovery and assessment retain their verified-cache `-Offline` behavior. In command-only qualification, `-Offline` controls source-payload acquisition; it does not promise a disconnected artifact build.

A module workflow may declare a content-pinned `moduleTest` acceptance script.
Such projects require schema 2 so an older reader rejects the required contract.
The script receives the actual built or installed path as `ArtifactPath`, with
literal named string `parameters` and an optional contained `workingDirectory`.
The child host starts with its own built-in module path. The workflow captures
and verifies script bytes before dispatch through the native binder, preserving
the original script path variables. Schema-2 test evidence authenticates the
acceptance-script hash as well as the project, target and artifact set. Default
bare-import tests retain schema 1. Installer acceptance explicitly selects the
pinned 7.6.6 observer through `POWERFORGE_PWSH76_PATH`.
The Scoop fixture requires an ASCII workspace because the unchanged installer
writes ASCII configuration. Its command environment excludes Git and pre-existing
Scoop, and every observed installation requires exactly one successful request
for each pinned repository ZIP. Loopback requests bypass inherited proxies.

## Artifact-aware discovery and the bounded compiler gate

The separate `compiler-discovery.net10.json` packet pins seven repository snapshots, archive hashes, license status, source selection, artifact kind, mode, and semantic profile. `Invoke-CompilerDiscovery.ps1` assesses each selected input independently in bounded process batches and records successful products plus source-closure failures, including inputs for which no product can be assessed. PSScriptTools exercises the dynamic dot-source boundary: its Hybrid module census sees the loader, while Strict reports the unresolved source closure. The runner never imports or executes external source and does not rewrite either acceptance baseline.

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-CompilerDiscovery.ps1 `
    -WorkspacePath ./assessment/discovery

powerforge powershell census ./Main.ps1 --kind exe --mode Strict `
    --framework net10.0 --semantic-profile PowerForge.Oracle.PowerShell/7.6 --output json

./Build/Invoke-PowerShellCompilerGate.ps1 `
    -EvidencePath ./assessment/compiler-gate-fast -RuntimeIdentifier win-x64 -Lane Fast

./Build/Invoke-PowerShellCompilerGate.ps1 `
    -EvidencePath ./assessment/compiler-gate-full -RuntimeIdentifier win-x64 -Lane Full
```

Census accepts `--kind exe|dll|library`, `--mode Strict|Hybrid`, and `--semantic-profile`. Omitted kind is inferred per input. Incomplete input assessment retains successful products and `InputFailures` in JSON with a nonzero CLI exit code. Baselines require matching artifact, mode, profile, framework, and recursion contracts. The existing overload remains available to C# callers; use `RunWithOptions` for explicit contracts.

The gate requires the repository .NET 10 SDK/runtime and the selected PowerShell host. `Fast` is the default development and pull-request lane. It selects the semantic/census/fuzz tests, generated module workflows on the available hosts, and the ABI, package, provider, project run/watch, and CLI contract families. Both lanes then run the same six Strict programs (24 authored units), including number-theory and calendar scenarios. `Full` retains the broader artifact matrix for an explicit integration or release qualification; it is not a prerequisite for each edit. The workflow's `compilerFullGate` manual-dispatch input selects that lane on the CI runner. Run `linux-x64` on Linux itself. Windows PowerShell 5.1/`net472` cases are selected only on Windows; host-capability JSON records their unavailability on Linux. The broader Hybrid module and NativeAOT lanes retain their own separately dated evidence.

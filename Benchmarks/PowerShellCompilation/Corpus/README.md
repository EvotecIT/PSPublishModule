# Generic compiler corpus

This checked-in corpus is the portable, product-neutral acceptance surface for PowerForge PowerShell compilation. It does not encode module names, command names, paths, or special cases from an external repository.

- `HybridModule` exercises parameter metadata and literal defaults, typed operators, direct self-recursion, safe runtime-state intrinsics, command-result capture, read-only environment access, known object shapes, and mutable list flows. Its current net10 baseline emits all 8 functions without fallback.
- `StrictProgram` is a multi-file script graph that must build as a PowerShell-free typed executable.
- `StrictCollections` and `StrictSwitch` add collection mutation and exhaustive control-flow programs without input-specific compiler configuration.
- `census-baseline.net10.json` records post-emission module coverage, a source fingerprint, and the required final-ledger identity/disposition of every authored function. Its relative product path remains comparable from another checkout root.
- `public-corpus.net8.json` fixes ten unrelated public module packages by URL, version, license, content hash, scenario family, entrypoint, and clean-target probe. Package contents remain external.
- `public-corpus-baseline.net8.json` records the bounded Windows Hybrid and Windows/Linux Strict outcomes, including separate floors for complete emitted CLR units and promoted regions inside retained functions. Its percentages are packet measurements, never estimates of PowerShell-language coverage.
- `external-assessment.net10.json` is a separate, replaceable frontier packet. It pins repository archives, gallery packages, and standalone files by immutable revision and SHA-256, but it does not turn their names or behavior into compiler configuration.
- `external-assessment-baseline.net10.json` records post-emission census results for the pinned frontier. Baseline schema 2 binds every authored function by its final-ledger unit identity and rejects loss of semantic eligibility, complete-function emission, or promoted typed regions; newly gained runtime routing; shaping fallback for a previously eligible function; incomplete/duplicate identities; source drift; parser regressions; and aggregate fallback growth. A newly supported function may advance from runtime fallback to an explicitly attributed shaping fallback, but it cannot replace a previously emitted function or promoted region while aggregate counts remain unchanged. Promoted regions remain separate from emitted-unit/function coverage, and low coverage is not treated as successful compilation or execution.
- `Corpus.Runner.Common.ps1` owns exact-hash HTTPS acquisition, offline cache verification, contained archive extraction, expansion limits, and owned child-process execution for both packets.

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

The runner downloads only the exact declared package URLs, verifies SHA-256 before extraction, rejects escaping paths, portable case collisions, and links, and enforces entry-count, per-entry, total-expansion, and compression-ratio limits. It writes one reviewed dependency lock per input, builds without the compiler build cache, and probes each generated module in a clean child PowerShell process. It also enforces the checked-in packet identity, clean-import totals, post-emission dispositions, complete Strict-program results, and the selected target-host baseline. The multi-file Strict application additionally consumes a delivered resource, exercises an exact success-stream contract and a bounded controlled-failure contract, excludes one warmup per surface, and records six alternating fresh-process samples. Its per-RID baseline enforces dependency/resource identity, stream hashes and exit behavior, the sampling policy, and conservative performance budgets. Filtered or skipped lanes remain identity checks and cannot rewrite or satisfy the corresponding full baseline. Use `-Offline` after the first acquisition to prove that the packet no longer depends on a package feed. Strict programs are executed directly for the selected RID; run them on the actual target host rather than treating cross-publish as execution proof.

Run the external frontier assessment after building the CLI:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalAssessment.ps1
```

The assessment runner accepts any packet that follows the checked-in schema. It supports exact-hash HTTPS files and ZIP archives, validates archive containment and portable path collisions, bounds entry count, per-entry bytes, total expansion, and compression ratio, never imports or executes the external source, and can rerun from its verified cache with `-Offline`. Use `-RefreshBaseline` only when intentionally changing the pinned packet or accepting a reviewed compiler-coverage change. Assessment success means acquisition and post-emission census completed without regression; it is not a complete-program pass.

Run the separate opt-in qualification lane only when local execution of the reviewed third-party packet is acceptable:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalQualification.ps1 -AllowExternalExecution
```

This lane builds a Hybrid artifact, verifies that the packet's named unit was emitted as CLR, loads the original and generated surfaces in separate clean child processes, and compares the selected command invocation. The reviewed metadata can select manifest import, direct root-module import, safe dot-sourcing, or the generated typed assembly independently; these are harness choices and never compiler eligibility inputs. The fixed packet currently proves four commands across three scenario families. A passing qualification proves only the selected command, not the complete workload.

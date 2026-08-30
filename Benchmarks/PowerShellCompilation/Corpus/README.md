# Generic compiler corpus

This checked-in corpus is the portable, product-neutral acceptance surface for PowerForge PowerShell compilation. It does not encode module names, command names, paths, or special cases from an external repository.

- `HybridModule` exercises parameter metadata and literal defaults, typed operators, direct self-recursion, safe runtime-state intrinsics, command-result capture, read-only environment access, known object shapes, and mutable list flows. Its current net10 baseline emits all 8 functions without fallback.
- `StrictProgram` is a multi-file script graph that must build as a PowerShell-free typed executable.
- `StrictCollections` and `StrictSwitch` add collection mutation and exhaustive control-flow programs without input-specific compiler configuration.
- `census-baseline.net10.json` records post-emission module coverage and a source fingerprint. Its relative product path remains comparable from another checkout root.
- `public-corpus.net8.json` fixes ten unrelated public module packages by URL, version, license, content hash, scenario family, entrypoint, and clean-target probe. Package contents remain external.
- `public-corpus-baseline.net8.json` records the bounded Windows Hybrid and Windows/Linux Strict outcomes. Its percentages are packet measurements, never estimates of PowerShell-language coverage.
- `external-assessment.net10.json` is a separate, replaceable frontier packet. It pins repository archives, gallery packages, and standalone files by immutable revision and SHA-256, but it does not turn their names or behavior into compiler configuration.
- `external-assessment-baseline.net10.json` records post-emission census results for the pinned frontier. Its gate protects source identity, parser health, and existing emission from regression; low coverage remains visible and is not treated as successful compilation or execution.

Run the portable coverage gate from the repository root:

```powershell
powerforge powershell census `
    .\Benchmarks\PowerShellCompilation\Corpus\HybridModule\Generic.Compiler.Corpus.psd1 `
    --framework net10.0 `
    --baseline .\Benchmarks\PowerShellCompilation\Corpus\census-baseline.net10.json
```

The wider external-repository census remains useful scale evidence, but every external root is optional and replaceable. Compiler eligibility is based only on generic syntax, type, binding, host-capability, and artifact contracts.

Run the fixed public packet after building the CLI:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1
```

The runner downloads only the exact declared package URLs, verifies SHA-256 before extraction, rejects escaping paths, portable case collisions, and links, writes one reviewed dependency lock per input, builds without the compiler build cache, and probes each generated module in a clean child PowerShell process. Use `-Offline` after the first acquisition to prove that the packet no longer depends on a package feed. Strict programs are executed directly for the selected RID; run them on the actual target host rather than treating cross-publish as execution proof.

Run the external frontier assessment after building the CLI:

```powershell
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalAssessment.ps1
```

The assessment runner accepts any packet that follows the checked-in schema. It supports exact-hash HTTPS files and ZIP archives, validates archive containment and portable path collisions, bounds entry count, per-entry bytes, total expansion, and compression ratio, never imports or executes the external source, and can rerun from its verified cache with `-Offline`. Use `-RefreshBaseline` only when intentionally changing the pinned packet or accepting a reviewed compiler-coverage change. Assessment success means acquisition and post-emission census completed without regression; it is not a complete-program pass.

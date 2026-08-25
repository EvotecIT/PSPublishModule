# Generic compiler corpus

This checked-in corpus is the portable, product-neutral acceptance surface for PowerForge PowerShell compilation. It does not encode module names, command names, paths, or special cases from an external repository.

- `HybridModule` exercises parameter metadata and literal defaults, typed operators, direct self-recursion, safe runtime-state intrinsics, command-result capture, and one intentional runtime-scope fallback.
- `StrictProgram` is a multi-file script graph that must build as a PowerShell-free typed executable.
- `census-baseline.net10.json` records post-emission module coverage and a source fingerprint. Its relative product path remains comparable from another checkout root.

Run the portable coverage gate from the repository root:

```powershell
powerforge powershell census `
    .\Benchmarks\PowerShellCompilation\Corpus\HybridModule\Generic.Compiler.Corpus.psd1 `
    --framework net10.0 `
    --baseline .\Benchmarks\PowerShellCompilation\Corpus\census-baseline.net10.json
```

The wider external-repository census remains useful scale evidence, but every external root is optional and replaceable. Compiler eligibility is based only on generic syntax, type, binding, host-capability, and artifact contracts.

# PowerShell Compilation Benchmarks

This benchmark matrix separates three different claims:

- a normal PowerShell function versus an importable generated binary cmdlet;
- genuinely typed generated CLR methods versus equivalent hand-written C#;
- a packaged single-file executable versus `pwsh -File` process startup.

The real-function workload is `Get-AllowedAverageMs`, taken from TestimoX's dashboard benchmark gate. The triangular-number loop is intentionally synthetic and exposes hot-loop behavior without command, provider, or I/O noise. Every measured lane validates its result outside the timed operation. Artifact generation, assembly loading, module import, and workload setup are also outside the timed block.

The 2026-08-23 Windows reference run measured the typed CLR lane 42.3-45.3x faster than the original real function and 8.8x faster on the synthetic loop. It also measured packaged startup 2.33x slower than `pwsh -File`. See [PowerShell Compilation](../../Docs/PowerForge.PowerShellCompilation.md#measured-performance) for the full table, environment, run IDs, interpretation, and eligibility limits.

Build or import the current PSPublishModule binary, then run:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\PowerShellCompilation\powershell-compilation.benchmark.ps1 `
    -RunMode local
```

For a quick smoke matrix:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\PowerShellCompilation\powershell-compilation.benchmark.ps1 `
    -Variable @{ Quick = $true } `
    -RunMode quick
```

Artifacts are written under `Ignore\Benchmarks\PowerShellCompilation`. The metadata pins SHA-256 values for the generated typed library, binary module, and packaged executable used by the run.

Do not interpret the binary-cmdlet lane as pure arithmetic throughput: it intentionally includes PowerShell command discovery, parameter binding, pipeline, and `PSCmdlet.WriteObject` overhead. The typed-CLR and hand-written-C# lanes perform their repeated calls inside C# and are the appropriate comparison for code-generation quality.

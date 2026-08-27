# PowerShell Compilation Benchmarks

This benchmark matrix separates three different claims:

- a normal PowerShell function versus an importable generated binary cmdlet;
- genuinely typed generated CLR methods versus equivalent hand-written C#;
- many fine binary-cmdlet calls versus one coarse generated command doing equivalent work;
- a typed executable and a packaged single-file executable versus `pwsh -File` process startup.
- repeated local PowerShell function dispatch versus direct generated CLR calls in a multi-file Strict executable.

The real-source workloads are a production threshold calculation and PowerInfoBlox's `Convert-IpAddressToPtrString`. The triangular-number and indexed-array loops are intentionally synthetic and expose hot-loop and typed-indexing behavior without command, provider, or I/O noise. Every measured lane validates its result outside the timed operation. Artifact generation, assembly loading, module import, and workload setup are also outside the timed block.

The adjacent `Corpus` directory is the compiler's product-neutral correctness and coverage gate, not a performance workload. It contains a portable Hybrid module, a multi-file Strict program, and a committed post-emission census baseline. External module workloads remain optional and replaceable scale evidence.

See [PowerShell Compilation](../../Docs/PowerForge.PowerShellCompilation.md#measured-performance) for the clean-candidate tables, environment, run IDs, interpretation, and eligibility limits. Quick runs are smoke evidence only and record `gitWorktreeClean: false` when the candidate is still changing.

Build or import the current PSPublishModule binary, then run:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\PowerShellCompilation\powershell-compilation.benchmark.ps1 `
    -Variable @{ IncludeOptimizedExecutables = $true } `
    -RunMode local
```

For a quick smoke matrix:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\PowerShellCompilation\powershell-compilation.benchmark.ps1 `
    -Variable @{ Quick = $true } `
    -RunMode quick
```

Artifacts are written under `Ignore\Benchmarks\PowerShellCompilation`. The metadata pins SHA-256 values for the generated typed library, binary module, typed executable, and packaged executable used by the run, together with executable byte sizes.

The `powershell-compilation-typed-local-calls` lane uses the same entry script and helper file for both engines. The PowerShell lane dot-sources and dispatches the helper normally; the Strict lane compiles the dependency closure into direct static CLR calls. It therefore measures the product behavior unlocked by multi-file typed executables, including process startup, rather than an unrelated hand-written C# surrogate.

Do not interpret the binary-cmdlet lane as pure arithmetic throughput: it intentionally includes PowerShell command discovery, parameter binding, pipeline, and `PSCmdlet.WriteObject` overhead. The typed-CLR and hand-written-C# lanes perform their repeated calls inside C# and are the appropriate comparison for code-generation quality.

The same quick matrix can run under Linux PowerShell. A host whose PowerShell runtime sits on an intermediate .NET major may emit reference-unification warnings while loading the supported `net8.0` benchmark artifact; those warnings must be disclosed with the run rather than treated as a clean standard baseline.

For a bounded cross-platform baseline, override `Calls`, `LoopCalls`, `Warmup`, and `Iterations`. Positional workload semantics remain identical; only the sample volume changes. `IncludeOptimizedExecutables` publishes and executes the checked-in `typed-executable-optimization.ps1` workload as both trimmed and NativeAOT single-file artifacts, then records their hashes and byte sizes in startup metadata.

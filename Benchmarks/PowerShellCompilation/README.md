# PowerShell Compilation Benchmarks

This benchmark matrix separates three different claims:

- a normal PowerShell function versus an importable generated binary cmdlet;
- genuinely typed generated CLR methods versus equivalent hand-written C#;
- many fine binary-cmdlet calls versus one coarse generated command doing equivalent work;
- a typed executable and a packaged single-file executable versus `pwsh -File` process startup.
- repeated local PowerShell function dispatch versus direct generated CLR calls in a multi-file Strict executable.

The representative workloads are a threshold calculation and an IPv4-to-PTR conversion helper. The triangular-number and indexed-array loops are intentionally synthetic and expose hot-loop and typed-indexing behavior without command, provider, or I/O noise. Every measured lane validates its result outside the timed operation. Artifact generation, assembly loading, module import, and workload setup are also outside the timed block.

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

Artifacts are written under `Ignore\Benchmarks\PowerShellCompilation`. The metadata pins SHA-256 values for the generated typed library, binary module, typed executable, packaged executable, ReadyToRun experiment, trimmed executable, and NativeAOT executable used by the run, together with executable byte sizes. It also records the ReadyToRun SDK, actual Hybrid typed/hosted boundary crossings, nanoseconds per crossing, estimated boundary-overhead ratio, and the resulting coarsening advisory.

The M22 qualification packet also records reviewed build time, generated-module import time, process allocation deltas, working-set deltas, and artifact bytes through the shared benchmark runner. Successful lanes must validate the produced method or imported command outside the timed operation. A clean baseline is compared with `Test-BenchmarkGate`; build/import duration, allocations, working set, and artifact bytes have a default 25% regression allowance until a tighter per-host baseline is approved. Cross-engine product budgets are relative so hardware does not become policy: typed kernels must remain within 2.5x of equivalent hand-written C#, at least 4x faster than the PowerShell function for the named compute packet, typed EXE startup must remain at most 0.75x `pwsh -File`, coarse dispatch at most 0.25x fine dispatch, framework-dependent output below 1 MB, and NativeAOT output below 10 MB. A budget failure is performance evidence only; it never changes semantic coverage or target mode automatically.

The `powershell-compilation-typed-local-calls` lane uses the same entry script and helper file for both engines. The PowerShell lane dot-sources and dispatches the helper normally; the Strict lane compiles the dependency closure into direct static CLR calls. It therefore measures the product behavior unlocked by multi-file typed executables, including process startup, rather than an unrelated hand-written C# surrogate.

Do not interpret the binary-cmdlet lane as pure arithmetic throughput: it intentionally includes PowerShell command discovery, parameter binding, pipeline, and `PSCmdlet.WriteObject` overhead. The typed-CLR and hand-written-C# lanes perform their repeated calls inside C# and are the appropriate comparison for code-generation quality.

The same quick matrix can run under Linux PowerShell. A host whose PowerShell runtime sits on an intermediate .NET major may emit reference-unification warnings while loading the supported `net8.0` benchmark artifact; those warnings must be disclosed with the run rather than treated as a clean standard baseline.

For a bounded cross-platform baseline, override `Calls`, `LoopCalls`, `Warmup`, and `Iterations`. Positional workload semantics remain identical; only the sample volume changes. `IncludeOptimizedExecutables` publishes and executes the checked-in `typed-executable-optimization.ps1` workload as ReadyToRun, trimmed, and NativeAOT artifacts, then records their SDK, hashes, and byte sizes in startup metadata. ReadyToRun is benchmark-only and does not become a public compilation target.

## Target-host certification

The target-host harness builds or validates the checked-in workload as Strict `net10.0` framework-dependent and NativeAOT executables. It verifies exact Unicode/resource output, invalid-argument behavior, cancellation, file permissions, executable format and architecture, native imports, and the reviewed delivered closure. A RID is promoted only after this harness runs on that RID's actual host.

Build and execute on the current host:

```powershell
.\Benchmarks\PowerShellCompilation\Test-PowerShellCompilationTargetHost.ps1 `
    -OutputDirectory .\Ignore\Benchmarks\PowerShellCompilation\target-host
```

Validate already-built artifacts from another supported PowerShell host, including Windows PowerShell 5.1:

```powershell
$artifactRoot = '.\Ignore\Benchmarks\PowerShellCompilation\target-host'
powershell.exe -NoLogo -NoProfile -File `
    .\Benchmarks\PowerShellCompilation\Test-PowerShellCompilationTargetHost.ps1 `
    -OutputDirectory .\Ignore\Benchmarks\PowerShellCompilation\target-host-ps51 `
    -ExistingManagedArtifactPath "$artifactRoot\PowerForge.TargetHost.Managed.win-x64.exe" `
    -ExistingNativeAotArtifactPath "$artifactRoot\PowerForge.TargetHost.NativeAot.win-x64.exe"
```

The current promoted target set is deliberately small: Strict `net10.0` framework-dependent and NativeAOT executables on `win-x64` and `linux-x64`. Portable managed artifacts remain `PortableManaged`; other named frameworks, deployment models, operating systems, and architectures remain `Experimental` until their exact target-host matrix passes.

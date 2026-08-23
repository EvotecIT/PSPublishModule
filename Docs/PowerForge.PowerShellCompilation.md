# PowerShell Compilation

PowerForge can turn a `.ps1` or `.psm1` file into three different artifact shapes:

- a single-file executable that packages the script and a PowerShell runtime;
- an importable binary or hybrid module whose eligible functions are compiled to typed CLR methods;
- a plain CLR library containing eligible typed methods for use from .NET.

These are deliberately separate claims. Packaging makes a script easier to distribute, but does not make its body faster. Typed compilation removes PowerShell's dynamic execution path for a conservative function subset and can improve CPU-bound code. Every build writes a JSON manifest that says which path was used.

## Build artifacts

| Kind | Default mode | Result | PowerShell required at runtime | Typed speedup expected |
| --- | --- | --- | --- | --- |
| `Executable` / `exe` | `Package` | Single-file host with embedded script and PowerShell SDK | Embedded in the application | No |
| `BinaryModule` / `dll` | `Strict` | Importable DLL when every function compiles | Yes, as the cmdlet host | Only inside sufficiently coarse compiled work |
| `BinaryModule` / `dll` | `Hybrid` | Module folder with a typed DLL and `.psm1` fallback | Yes | For eligible functions; unsupported functions remain scripts |
| `Library` / `library` | `Hybrid` | CLR DLL with eligible public static methods | No | Yes, when called as CLR code |

Supported target frameworks are:

- executable: `net8.0`, `net10.0`;
- CLR library or binary module: `net472`, `net8.0`, `net10.0`.

The `net472` binary-module lane is tested by importing and invoking the generated DLL in Windows PowerShell 5.1. The modern lanes run in PowerShell 7.

## Use the CLI

Analyze a file or a complete source tree before building:

```powershell
powerforge powershell analyze .\MyModule --mode Hybrid
```

Package a script as an executable:

```powershell
powerforge powershell build .\Invoke-Report.ps1 `
    --kind exe `
    --out .\artifacts `
    --name Invoke-Report

.\artifacts\Invoke-Report.exe --Path C:\Reports --Format Html
```

The generated host accepts positional arguments, `--Name value`, `--Name=value`, and `--` to stop named-argument parsing. Output and information records go to stdout, warnings and errors go to stderr, and an explicit `exit <code>` becomes the process exit code.

Compile a strict binary module:

```powershell
powerforge powershell build .\MathTools.psm1 `
    --kind dll `
    --mode Strict `
    --framework net8.0 `
    --out .\artifacts

Import-Module .\artifacts\MathTools.dll
```

Build a hybrid module when only part of the source is eligible:

```powershell
powerforge powershell build .\Operations.psm1 `
    --kind dll `
    --mode Hybrid `
    --out .\artifacts

Import-Module .\artifacts\Operations\Operations.psm1
```

Build a runtime-independent CLR library containing every eligible function:

```powershell
powerforge powershell build .\Calculations.psm1 `
    --kind library `
    --mode Hybrid `
    --out .\artifacts
```

Add `--output json` to either `analyze` or `build` for a stable machine-readable envelope.

## Use the PSPublishModule cmdlet

The cmdlet is a thin PowerShell surface over the same artifact builder:

```powershell
Build-PowerShellArtifact `
    -Path .\Operations.psm1 `
    -Kind BinaryModule `
    -Mode Hybrid `
    -OutputDirectory .\artifacts
```

It supports `-WhatIf`, returns `PowerShellCompilationBuildResult`, and uses the same defaults and manifests as the CLI.

## Modes

`Analyze` parses source and reports one decision per top-level script body or function. It produces no artifact.

`Package` preserves dynamic PowerShell behavior. The current executable lane embeds the source script and PowerShell SDK in a generated .NET host. It is a distribution feature, not typed compilation.

`Hybrid` compiles complete eligible functions and retains diagnostics for everything else. A hybrid binary module removes compiled function definitions from its generated `.psm1`, imports the typed DLL, and keeps unsupported functions on the script path. A hybrid CLR library extracts eligible methods without carrying script fallback because it is intended for direct .NET consumption.

`Strict` fails the build when any executable unit needs fallback. Use it when a DLL must contain only behavior covered by the typed compiler contract.

## Current typed subset

Eligibility is whole-function and intentionally conservative. One unsupported construct keeps the complete function on the PowerShell path.

The first subset supports:

- explicitly typed scalar parameters and one-dimensional typed arrays;
- typed or safely inferred local variables;
- explicit `return` values;
- `if`/`elseif`/`else`, `for`, `while`, and `foreach` over typed arrays;
- Boolean logic and scalar comparisons with known compatible types;
- string equality with PowerShell case-sensitive or case-insensitive behavior;
- floating-point and decimal arithmetic with compatible operands;
- PowerShell-style integral division, which returns a floating-point result;
- explicitly typed integral accumulators and loop counters with checked assignment semantics;
- `break` and `continue`.

The analyzer rejects dynamic behavior rather than guessing. Current blockers include:

- command, provider, pipeline, and dynamic command invocation;
- member access, method invocation, and object-property semantics;
- script blocks, closures, runtime scopes such as `$env:`, and untyped parameters;
- parameter attributes, default expressions, and `dynamicparam`, `begin`, `process`, or `clean` blocks;
- implicit pipeline output and typed top-level script generation;
- PowerShell truthiness conversions, element-wise array comparison, and coercion between incompatible CLR types;
- untyped integral arithmetic that can change CLR type after overflow;
- control flow for which the conservative emitter cannot prove declaration or return behavior.

This boundary is expected to expand through semantic proof, not syntax count. New constructs need differential tests against PowerShell before they become eligible.

## Manifest evidence

Each successful build writes `<name>.powerforge-compilation.json`. The manifest records:

- artifact kind, mode, target framework, and runtime identifier;
- whether PowerShell is required and whether script fallback is used;
- compiled method count, runtime-fallback count, omitted-unit count, and coverage percentage;
- SHA-256 for the primary artifact and every file in a hybrid module;
- exact source diagnostics and locations for unsupported units.

A packaged EXE therefore reports `requiresPowerShellRuntime: true` and `usesPowerShellRuntimeFallback: true`. A strict CLR library reports both values as `false`. A strict binary module requires PowerShell as its cmdlet host but reports no script fallback.

## Measured performance

The checked-in benchmark suite validates every result outside the timed operation and compares four execution lanes:

- the original PowerShell function;
- the generated binary cmdlet called through PowerShell;
- the generated typed CLR method called inside a C# loop;
- equivalent hand-written C#.

The standard reference run used PowerShell 7.6.4 on .NET 10.0.10, Windows x64, and an AMD64 32-logical-core machine. Duration rows are medians after three warmups, 12 measured samples, and minimum/maximum exclusion. The startup benchmark used two warmups and 10 measured samples.

The final-candidate run IDs are `20260823-082527-44a8f15d` (real function), `20260823-082626-88be2d77` (synthetic loop), and `20260823-082628-f6f3a6fd` (packaged startup).

| Workload | Calls | PowerShell | Typed CLR | Hand-written C# | Typed vs PowerShell | Typed vs C# |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Real `Get-AllowedAverageMs`, absolute-cap branch | 50,000 | 205.78 ms | 4.54 ms | 2.91 ms | **45.3x faster** | 1.56x slower |
| Real `Get-AllowedAverageMs`, relative-cap branch | 50,000 | 194.75 ms | 4.60 ms | 2.16 ms | **42.3x faster** | 2.13x slower |
| Synthetic triangular-number loop, 1,000 x 1,000 iterations | 1,000 | 34.60 ms | 3.91 ms | 2.00 ms | **8.8x faster** | 1.95x slower |

These results prove a benefit only for eligible computation executed as CLR code. They do not promise that an arbitrary script or a generated cmdlet call is faster.

The binary-cmdlet lane includes PowerShell command lookup, parameter binding, pipeline setup, and `WriteObject` for every call. It took 1,531.36 ms and 1,595.28 ms in the two 50,000-call real scenarios, versus 205.78 ms and 194.75 ms for the original function. The useful product shape is a coarse cmdlet that performs substantial compiled work per invocation, not a tiny arithmetic cmdlet called in a PowerShell loop.

Packaging also has a measurable cost. A one-shot `pwsh -File` invocation took 196.21 ms, while the framework-dependent single-file packaged executable took 456.18 ms: 2.33x slower startup. The EXE is valuable for delivery and launch ergonomics, not startup speed.

Run the same matrix locally:

```powershell
Invoke-BenchmarkSuite `
    -Path .\Benchmarks\PowerShellCompilation\powershell-compilation.benchmark.ps1 `
    -RunMode standard
```

See [the benchmark README](../Benchmarks/PowerShellCompilation/README.md) for the quick smoke command and lane definitions.

## Real-source eligibility

A scan of TestimoX covered 1,665 PowerShell files and 2,103 executable units. The current conservative subset accepted two units (0.095%): both copies of `Get-AllowedAverageMs` used by dashboard benchmark gates. That function is the real workload in the benchmark table above.

Low initial coverage is not hidden by Hybrid mode. It is written to the manifest, and every fallback has a diagnostic explaining what needs compiler support. The next useful compiler work is driven by recurring blocker classes in real repositories rather than making isolated syntax examples turn green.

## Security and distribution limits

Packaging is not obfuscation, code signing, or source protection. The executable contains an embedded script and runtime assets that a determined user can inspect. A typed DLL is normal managed code and can also be decompiled. Apply the existing PowerForge signing and release pipeline when authenticity and provenance matter.

The generated EXE carries the PowerShell SDK, so it is much larger than the input script and may start more slowly than an installed `pwsh`. Self-contained publication adds the .NET runtime as well. Runtime-packaged artifacts must be rebuilt when their embedded PowerShell or .NET dependencies need security updates.

Typed compilation is currently function-oriented. A strictly typed top-level EXE entry point is not implemented; executable output uses the packaging lane. Hybrid module composition is available for mixed `.ps1` or `.psm1` source, while a plain CLR library contains only the eligible methods and no automatic PowerShell fallback host.

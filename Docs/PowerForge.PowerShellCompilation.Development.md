# Develop compiled PowerShell projects

`powerforge powershell project run` builds and runs one executable target from source. `watch` repeats that workflow after edits. These commands are available on the `feature/powershell-compiler` development branch; this guide does not imply that a published package contains them.

Use the .NET 10 SDK for building the CLI and generated executables. The Windows examples below select `win-x64`; a declared target must match the operating system and architecture of the process running PowerForge. Additional platform qualification is tracked separately in M29.

## Build the development CLI

From a checkout of the compiler branch:

```powershell
dotnet build ./PowerForge.Cli/PowerForge.Cli.csproj -c Release -f net10.0
$powerforge = (Resolve-Path ./PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll).Path
```

Keep `$powerforge` as an absolute path so you can work from your own project directory. The following commands use that locally built CLI and do not require installing a global tool.

## Create and run an executable

Create a project directory and save `main.ps1` there:

```powershell
return 7
```

From that directory:

```powershell
dotnet $powerforge powershell project init ./main.ps1 --name Sample --kind exe --mode Strict --framework net10.0 --rid win-x64
dotnet $powerforge powershell project lock ./powerforge.psproject.json
dotnet $powerforge powershell project restore ./powerforge.psproject.json
dotnet $powerforge powershell project run ./powerforge.psproject.json
dotnet $powerforge powershell project watch ./powerforge.psproject.json
```

Change `return 7` to `return 9`. Watch stops the preceding attempt, waits for stable edits, rebuilds, verifies the new artifact, and runs it. A parse or build failure leaves watch waiting for the next edit and launches nothing. A successful application exit leaves watch active. Press Ctrl+C to stop watch or a foreground run; the CLI returns exit code 130 and terminates its owned application process tree. Process termination does not guarantee execution of the application's `finally` blocks.

For scripts that need the PowerShell runtime, choose `--mode Package` when creating the project. Strict mode requires the complete executable workflow to be admitted by the compiler. Use `project explain` to inspect unsupported source. `run` and `watch` accept one executable target; use `--target <name>` when the manifest declares several targets.

## Arguments and streams

Everything after the CLI's `--` is passed to the executable as an argument vector without shell parsing:

```powershell
dotnet $powerforge powershell project run ./powerforge.psproject.json -- -Name 'two words'
```

The executable retains its own argument-binding rules. A Package executable accepts named PowerShell parameters; its own `--` switches to positional-only input. To pass a literal dash-prefixed value into a script's `$args`, use both delimiters:

```powershell
dotnet $powerforge powershell project run ./powerforge.psproject.json -- -- --help --verbose
```

The first delimiter belongs to the project CLI, and the second belongs to the generated executable. Neither `--help` nor `--verbose` is then interpreted by PowerForge's global options.

The application runs from the project directory and inherits stdin, stdout and stderr, including redirected OS handles. A successful `run` adds no status text to those streams and returns the application's exit code. Build/validation failures write a diagnostic to stderr. Watch writes lifecycle messages to stderr; stdout remains the application's stream. Run/watch do not support `--output json`, which would conflict with application output.

## What an edit can change

Run/watch permits content edits to already-locked local scripts and ordinary resources. It keeps the reviewed lock file unchanged and records the exact current source graph in the generated artifact. It does not automatically approve changed dependency declarations, newly introduced graph nodes, module manifests, binaries, provider packages, semantic profiles, or target settings. Review those changes, then run `project lock` and `project restore` explicitly.

Watch fingerprints source files, selected resource/dependency inputs, provider packages, the project manifest, and lock/environment receipts. It excludes declared artifact directories and build/cache state. Content hashes detect same-size edits even when a timestamp is retained. Rapid edits cancel the superseded attempt and wait for stable input before the next build. Generated build output is reused only through the compiler's verified content-addressed cache in `.powerforge/cache`; analysis, integrity checks and locked restore still run.

After the initial acquisition, builds use the isolated package root with offline locked restore. You can verify its availability explicitly:

```powershell
dotnet $powerforge powershell project restore ./powerforge.psproject.json --offline
```

Only one run/watch session can own a project at a time. Stop it before starting another development session. An input change during a build prevents that superseded attempt from launching; watch then rebuilds the latest stable input. Immediately before launch, run checks that the artifact files still match the inventory authenticated for that attempt. If another producer replaces those files, run fails instead of accepting its replacement receipt.

## Test and package a reviewed revision

Development execution does not replace the exact-source review required by project test/pack. Once the source is ready, refresh the lock and restore, then follow the existing qualification path:

```powershell
dotnet $powerforge powershell project lock ./powerforge.psproject.json
dotnet $powerforge powershell project restore ./powerforge.psproject.json --offline
dotnet $powerforge powershell project build ./powerforge.psproject.json
dotnet $powerforge powershell project test ./powerforge.psproject.json
dotnet $powerforge powershell project diagnose ./powerforge.psproject.json
dotnet $powerforge powershell project pack ./powerforge.psproject.json
```

Project pack creates the existing qualified ZIP. For a tested Strict CLR library, use `project pack --format nuget` and the [library NuGet quickstart](PowerForge.PowerShellCompilation.LibraryPackages.md). Observed source debugging remains separate M28 work; portable PDB output alone is not debugger qualification.

For a module, create the project from its `.psd1` or `.psm1` with `--kind dll --mode Hybrid`; for a CLR library, use `--kind library --mode Strict`. Both use the same lock → restore → build → test → diagnose → pack sequence. Module test performs a clean import, while library test validates CLR metadata. Invoke exported commands or use an ordinary .NET consumer to verify the behavior you intend to distribute. Run/watch is the executable development path, not a substitute for those consumers.

## Diagnose a failed attempt

| Symptom | Next action |
| --- | --- |
| Source is rejected | Run `project explain` and inspect the target's evidence under `.powerforge/explain`. |
| Dependencies or declarations changed | Review `project lock`, then run `project restore`; watch never approves these automatically. |
| Environment belongs to another manifest revision | Restore the updated project and target selection. |
| Missing or altered package/closure | Restore the reviewed closure; use online restore if the isolated package root needs reacquisition. |
| Another development session owns the project | Stop the preceding run/watch process. |
| Target belongs to another OS or architecture | Select a target matching the actual host. |
| Inputs changed during build | Retry run, or let watch build the next stable revision. |
| Artifact output changed before launch | Stop the competing output writer, then retry run. Do not build into the active development output from another process. |
| Test/diagnose rejects a development artifact after source edits | Refresh the exact source lock and restore, then build and test the reviewed revision. |

The [compilation guide](PowerForge.PowerShellCompilation.md) describes artifact contracts. The [next milestones](PowerForge.PowerShellCompilation.NextMilestones.md) and [assessment](PowerForge.PowerShellCompilation.Assessment.md) record remaining work and qualification evidence.

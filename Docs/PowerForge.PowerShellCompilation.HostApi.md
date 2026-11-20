# Check and compile a script from a simple host

The shared `Check`, `Compile` and `Doctor` service and the reviewed CLI workflow are available. See the [compiler guide](PowerForge.PowerShellCompilation.md) for the current commands. This page describes the planned host experience and tracks packaging support in the [feature roadmap](#feature-roadmap).

This page specifies the engine surface for a small "drop a script, get an executable or a clear list of what's wrong" host: a desktop window, an editor extension, or a CI step. The host stays thin. Every decision, message and result comes from the engine, so the CLI and the cmdlets gain the same behaviour.

The target experience:

1. Drop a `.ps1`, a `.psm1`/`.psd1` or a module folder.
2. Within a few seconds, see whether it compiles, in which modes, and every problem with its file, line, severity and how to fix it.
3. Pick a mode and target, then confirm the dependencies for that selection.
4. Compile with phase progress and cancel.
5. Open the output and see what compiled natively and what runs on the hosted PowerShell runtime.

## Non-goals

- No project tree, Git, release or module build pages. Module building and publishing stay in PSPublishModule and `powerforge`.
- No new compiler capability. This surface reports what the compiler already decides; the milestones in [next milestones](PowerForge.PowerShellCompilation.NextMilestones.md) still decide what compiles.
- No bypass of dependency review. A host makes review easy; it never removes it.

## What exists today

| Need | Current engine surface | Gap for a simple host |
|---|---|---|
| Build from a bare script | `powershell build <file.ps1>`; `PowerShellCompilationInputResolver.Resolve`, a `PowerShellCompilationBuildSpec`, then `PowerShellCompilationArtifactBuilder.Build(spec, ILogger?, CancellationToken)`. Defaults: `.ps1` builds an executable in Package mode, `net10.0`, the host runtime, `<script dir>/artifacts`, no signing | A build without `ExpectedDependencyLock` or `AllowUnreviewedDependencyResolution` throws in `ValidateExpectedDependencyLock`, so the bare command fails |
| Analysis without a build | `powershell analyze` returns `PowerShellCompilationPlan` (`CanProceed`); `powershell explain` returns `PowerShellCompilationExplanation` with Typed, RuntimeFallback or Rejected per unit | Three result shapes; the grouped `PowerShellCompilationDiagnosticReport` needs a project file |
| Diagnostics | `PowerShellCompilationDiagnostic`: `Code`, `Message`, `FilePath`, `Line`, `Column`, `FeatureId` | No severity and no remediation text |
| Mode advice | `PowerShellCompilationProfileRecommendationService.Create(plan, target, …)` returns eligible units, an action and reasons for one target | Takes one target at a time; not reachable from a bare script |
| Package-mode limits | `PowerShellPackagedScriptRewriter` rejects `#requires`, `using module`/`using assembly`, `exit` in dot-sourced dependencies and some host interaction | Reported as exceptions in the middle of a build, not found up front |
| Targets | `PowerShellCompilationSupportMatrixService.Create()` | Fine as it is; hosts read it instead of hard-coding targets |
| Progress | `ILogger` text lines from the internal `PowerShellCompilationBuildProgress` | No phases, no `IProgress<T>`; analysis takes no `CancellationToken` |
| Result | `PowerShellCompilationBuildResult` with `Manifest` (sizes, hashes, `CompiledMethods`, `FallbackUnits`, `UnitDispositionLedger`) and `Failure` | No timings; fallback detail is deep in the manifest; invalid input throws while build failures return a result |
| Machine prerequisites | SDK resolution in `PowerShellCompilationToolchainFingerprint`; `powershell support` lists profiles | Nothing checks the local machine before a build; NativeAOT linker absence appears mid-build |

## Proposed surface

One host-facing service in `PowerForge.PowerShell`, with three operations. The CLI maps each one to a command with the existing `CliJsonEnvelope`.

```csharp
public sealed class PowerShellCompilationHostService
{
    public PowerShellCompilationCheckResult Check(
        PowerShellCompilationCheckRequest request,
        IProgress<PowerShellCompilationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public PowerShellCompilationCompileResult Compile(
        PowerShellCompilationCompileRequest request,
        IProgress<PowerShellCompilationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public PowerShellCompilationDoctorResult Doctor(
        PowerShellCompilationDoctorRequest request,
        CancellationToken cancellationToken = default);
}
```

| Operation | CLI | Purpose |
|---|---|---|
| `Check` | `powershell check <path> [--mode <mode> --target <id>]` | Shows the available selections and prepares dependency review for one selection |
| `Compile` | `powershell build <path>` keeps its name and gains the same request shape | Builds one artifact from a confirmed check |
| `Doctor` | `powershell doctor [--target <id>]` | Checks this machine against a target profile |

### Error contract

None of the three throws for problems in the user's input or machine. Missing files, unsupported input kinds, rejected constructs, missing prerequisites and build failures all come back as diagnostics in the result. Only cancellation (`OperationCanceledException`) and engine defects throw. A host then has one path for "show what's wrong".

### Check

Request:

| Field | Meaning |
|---|---|
| `Paths` | One script, manifest, module file or module folder (the same inputs as `Resolve`) |
| `EntryPoint` | Optional, as today |
| `Kind` | Optional; inferred as today when absent |
| `Targets` | Optional target profile IDs from the support matrix; default is every profile supported on this host |
| `LockSelection` | Optional mode and target ID for dependency review. Defaults to the recommended selection when one is available |
| `ExecutableOptions` | Optional [executable settings](#executable-details) to validate for a bare script or project target |

Result:

| Field | Meaning |
|---|---|
| `SchemaVersion` | Starts at 1 |
| `Input` | Resolved kind, entry point and source files |
| `Modes` | One entry each for Package, Hybrid and Strict with its `Outcome` (`Compiles`, `CompilesWithFallback`, `Blocked`) and unit counts from the plan. Each mode has recommendations keyed by requested target ID, with that target's action and reasons |
| `Recommended` | The mode and target the engine suggests for this input, with one sentence of reasoning taken from the recommendation |
| `Diagnostics` | Every finding, in source order, in the shape below |
| `Dependencies` | The external modules, scripts and native libraries for the selected mode and target, with `Disposition` (`Bundled`, `RequiredOnTarget`, `Blocked`) and the source location that introduced each one |
| `ProposedLock` | The selected mode, target ID and proposed dependency graph. Absent when there is no selected pair or the pair is blocked |
| `Prerequisites` | The `Doctor` result for the requested targets |
| `Timings` | Elapsed time per phase |

Check runs the analyzer and explanation service in Package, Hybrid and Strict and computes a profile recommendation for each mode and target. It plans dependencies for the selected pair. It also reports the Package-mode validations that `PowerShellPackagedScriptRewriter` performs today. Check never builds or runs the script.

### Diagnostics

Extend the existing type rather than adding a second one:

| Field | Source |
|---|---|
| `Code`, `FeatureId`, `Message`, `FilePath`, `Line`, `Column` | Existing fields, unchanged |
| `Severity` | `Error` blocks the selected mode, `Warning` compiles with runtime fallback or changed behaviour, `Info` is advice |
| `Modes` | Which modes the finding affects; a construct can block Strict and be fine in Hybrid |
| `Title` | A short name for the problem from the catalog |
| `Fix` | One or two sentences on what to change, from the catalog; empty when there is no user-side fix |
| `HelpUri` | Optional link to the guide or support-gaps entry |

Severity, title, fix and help link come from one catalog keyed by `PowerShellCompilationDiagnosticCode` and, where needed, `FeatureId`. The catalog is source-controlled next to the enum, and a test fails when a code has no catalog entry. The CLI's text output uses the same catalog, so the improvement is not GUI-only.

### Dependencies and the reviewed lock

The dependency-lock requirement stays. The host flow makes the review explicit and quick:

1. `Check` returns `Dependencies` and a `ProposedLock` for the selected mode and target. To review another selection, the host calls `Check` with that pair.
2. The host shows each `RequiredOnTarget` item as "this executable needs module X on the machine that runs it" and each `Blocked` item as a diagnostic.
3. The user confirms the lock for the named selection.
4. `Compile` verifies that the requested mode and target match the confirmed selection, then passes the lock to the builder as `ExpectedDependencyLock`. A different selection or a stale lock returns a diagnostic asking for a fresh check before the build starts.

`AllowUnreviewedDependencyResolution` remains for scripted development builds and is never the host default.

### Package-mode script forms

Retained executable entries preserve compatible `#requires -Version` and `#requires -PSEdition` directives. Check and build validate the embedded host's supported major and minor versions and edition. Embedded script dependencies with directives remain blocked.

- Module requirements remain blocked for retained executables. The planned contract records the name and version constraints as target-machine dependencies in the reviewed lock and enforces them at startup.
- Elevation, assembly and application-specific shell requirements remain blocked.

`using module`, `using assembly`, and `exit` inside dot-sourced dependencies stay unsupported for now, but are reported by `Check` with a severity and a fix.

### Compile

The request includes the source input, `Mode`, `Target`, `OutputDirectory`, optional `ArtifactName`, optional signing settings, optional `ExecutableOptions`, and the confirmed `DependencyLock` with its mode and target ID. Omitted mode and target use the confirmed selection. The other defaults match the existing build command.

Result:

| Field | Meaning |
|---|---|
| `Succeeded` | As today |
| `ArtifactPath`, `ManifestPath`, `ArtifactSizeBytes` | As today |
| `Summary` | `Native`, `Mixed` or `Packaged`, plus compiled and fallback unit counts. A host shows this in plain words so a packaged script is never presented as natively compiled |
| `FallbackUnits` | A flat list of units that run on the hosted runtime: name, file, line and the deciding diagnostic. Built from `UnitDispositionLedger` |
| `Diagnostics` | Build-time findings in the shape above, including the source-mapped `Failure` |
| `Timings` | Elapsed time per phase |
| `Manifest` | The full `PowerShellCompilationArtifactManifest`, unchanged, for tools that need detail |

### Executable details

The optional `ExecutableOptions` accepted by both `Check` and `Compile` describes the executable's identity and how it starts. The settings flow into `PowerShellCompilationBuildSpec` and the generated project, so the CLI, cmdlets and hosts share them.

| Setting | Generated project | Platforms |
|---|---|---|
| `IconPath` (`.ico`) | `ApplicationIcon` | Windows; ignored elsewhere with an `Info` diagnostic |
| `Version` (`x.y.z`) | `Version`, `FileVersion`, `AssemblyVersion`, `InformationalVersion` | All |
| `ProductName`, `CompanyName`, `Copyright`, `FileDescription` | `Product`, `Company`, `Copyright`, `AssemblyTitle` | All (shown in Windows file properties) |
| `WindowsSubsystem` (`Console` or `Windows`) | `OutputType` `Exe` or `WinExe` | Windows; `Windows` suits scripts that only show forms or dialogs |
| `RequestedExecutionLevel` (`AsInvoker`, `HighestAvailable`, `RequireAdministrator`) | a generated `app.manifest` | Windows |

`Check` validates these up front: a missing or unreadable icon, a version that is not `x.y.z`, or a `Windows` subsystem for a script that writes to the console all come back as diagnostics. The values are recorded in the artifact manifest so a rebuild reproduces them. A project file (`powerforge.psproject.json`) gains the same fields per target.

### Signing

`SignArtifact` today signs the files the build itself produces (the primary executable, generated assemblies and module manifests) after publishing, through Windows Authenticode. It does not sign what goes inside them. That leaves gaps on locked-down machines:

- Scripts embedded and extracted at run time (`EmbeddedAndExtracted`) land on disk unsigned, so they fail under an `AllSigned` execution policy, WDAC or AppLocker script rules.
- In a single-file executable, build-owned assemblies and native libraries that the bundle extracts are signed only as part of the outer file, not individually.
- Third-party dependencies keep whatever signature they shipped with, and unsigned ones stay unsigned.

Proposed order, all in one signing session so a hardware key prompts once:

1. Finish composing and rewriting the staged scripts and modules (`.ps1`, `.psm1`, `.psd1`), then sign those final files. Hash and embed the signed bytes so the extracted copies carry valid signatures.
2. Sign build-owned assemblies and native libraries before single-file bundling.
3. Optionally sign unsigned third-party assemblies (`SignDependencies`, off by default, matching PSPublishModule's dependency-signing option); never replace an existing valid signature.
4. Sign the outer executable or module last.

The manifest lists every signed file, including those inside the bundle, with its role and signer thumbprint, and `Check` reports when signing is requested on a platform without a supported signer. Signing stays Windows Authenticode for now; macOS `codesign` and Linux detached signatures are a later step.

### What the output reveals

Packaging a script is not protecting it. Commercial packagers store the script in a resource, sometimes encrypted with a key that ships in the same executable, and the full script can be recovered from it. This compiler should say plainly what its output contains rather than suggest protection it cannot give:

- Package and Hybrid executables carry the authored script. In a Hybrid build today the whole script is embedded as a UTF-16 string in the generated assembly, readable with any strings tool. The manifest already records this as `ContainsEmbeddedPowerShellSource`; `Compile` returns it, and hosts show it next to the result.
- Strict output contains no script text, but its logic is visible to any .NET decompiler. Hosts describe it as compiled code, not as protected code.
- The embedded source currently includes the absolute path of the build machine's source file. Embed the path relative to the module root instead, so builds don't leak local paths and are reproducible across machines.
- `Check` warns about likely secrets in the input: string literals assigned to names such as `password`, `secret`, `key`, `token` or `connectionString`, literal byte arrays passed to `Aes`/`Rijndael` keys, and values that match common credential formats. The warning names the line and says that anything in the script ships to every machine that runs the output.

No obfuscation or encryption option is planned: any key needed to run the script has to ship with it.

### Progress

```csharp
public sealed class PowerShellCompilationProgress
{
    public PowerShellCompilationPhase Phase { get; init; }
    public int? Completed { get; init; }
    public int? Total { get; init; }
    public string Message { get; init; } = string.Empty;
}

public enum PowerShellCompilationPhase
{
    Resolving, Analyzing, PlanningDependencies, Recommending,
    Generating, Restoring, Compiling, Publishing, Signing, Verifying
}
```

Warnings from the generated project are the engine's to fix, not the user's. Today a Hybrid build reports `NU1510` (redundant `System.Reflection.Metadata` and `System.Collections.Immutable` references) and `CS8321` (an unused `__powerForgeConvertInvariant` helper) for an ordinary script. Generated code should build without warnings, and any remaining generator warnings go to verbose output, not to the user's progress lines.

Phases come from the points `PowerShellCompilationBuildProgress` already logs. `Completed`/`Total` are set only where the count is real (units analyzed, projects compiled); hosts show an indeterminate bar otherwise. The `ILogger` lines stay for the CLI.

Analysis must honour the `CancellationToken` between units, so a large module can be cancelled from a host.

### Doctor

For each requested target, report a list of checks, each `Passed`, `Missing` or `Unsupported`, with a fix:

- a .NET SDK the builder can select, and the version it would pin;
- the runtime pack and, for trimmed or self-contained output, the ILLink pack for the target runtime;
- for NativeAOT targets, the platform linker and native toolchain;
- for signing, whether a signing certificate is configured (without opening a certificate store or prompting).

Doctor never installs anything. A host can link to the install instructions in the fix text.

## Delivery order

Each slice is useful on its own and adds a CLI command or option before any host exists:

1. **Diagnostic catalog and severity**, with the catalog test. The CLI's text output uses it immediately.
2. **`Check` and `powershell check`**: one result, no project file, Package-mode validations moved up front, cancellable analysis.
3. **Reviewed lock from check** for one mode and target. The host passes it to `Compile` and scripts use `powershell build --dependency-lock <file>`.
4. **`Doctor` and `powershell doctor`.**
5. **Compile result additions**: `Summary`, `FallbackUnits`, `Timings`, and returning input problems as diagnostics instead of throwing.
6. **Progress phases** through `IProgress<PowerShellCompilationProgress>`.
7. **`#requires` support in Package mode.**
8. **Executable details**: icon, version and file properties, console or windowed subsystem, and requested execution level.
9. **Signing inside the output**: embedded scripts and bundled build-owned files signed before packaging, optional dependency signing, one signing session.
10. **Source exposure**: relative embedded paths, `ContainsEmbeddedPowerShellSource` in the compile result, and the secret warnings in `Check`.
11. **Startup manifest and threading**: roadmap items E5 to E7.
12. **Windowed host**: H2 and H3.
13. **Tray host**: H4 and E8.
14. **Bundled installed modules**: D3.
15. **Target breadth**: T3 and T4, after the decision in T5.
16. **Installer output**: S4.
17. **Service host**: H5, only when there is a concrete request.

A simple host can start once slices 1 to 3 exist; the rest improve it.

## Feature roadmap

Everything people expect from a tool that turns PowerShell into an executable, compared with the packagers they already know (ps2exe and its forks ps12exe and PS2EXE.Core, PowerShell Pro Tools, SAPIEN PowerShell Studio), and where this engine stands. The IDs are stable; refer to them from issues and PRs. "Slice" points to the delivery order above.

Competitor columns record what their documentation describes; confirm a specific claim before quoting it publicly.

### Identity and startup

| ID | Feature | Others | Engine today | Plan |
|---|---|---|---|---|
| E1 | Executable icon | ps2exe, Pro Tools, SAPIEN | Missing | Slice 8 |
| E2 | Version, product, company, copyright, description | ps2exe, Pro Tools, SAPIEN | Missing | Slice 8 |
| E3 | Console or windowed subsystem | ps2exe (`-noConsole`), Pro Tools, SAPIEN | Console only | Slice 8 (subsystem), slice 12 (windowed host) |
| E4 | Requested execution level (run as administrator) | ps2exe (`-requireAdmin`), Pro Tools, SAPIEN | Missing | Slice 8 |
| E5 | Manifest `supportedOS` and `longPathAware` entries | ps2exe (`-supportOS`, `-longPaths`) | Missing | Slice 11 |
| E6 | DPI awareness for forms (`PerMonitorV2`) | ps2exe (`-DPIAware`, `-winFormsDPIAware`), SAPIEN | Missing | Slice 11 |
| E7 | Thread apartment: STA for windowed and tray hosts, MTA on request | ps2exe (`-STA`, `-MTA`), SAPIEN | Not configurable | Slice 11 |
| E8 | Single instance (a second start activates the first) | SAPIEN tray applications | Missing | Slice 13 |

### Hosts

The host is the program inside the executable that runs the script and implements PowerShell's prompts and output.

| ID | Feature | Others | Engine today | Plan |
|---|---|---|---|---|
| H1 | Console host: arguments passed to the script, `exit` codes preserved | All | Supported (`PowerShellPackagedConsoleHost`, `SetShouldExit`) | Done; console encoding (UTF-8 in and out) still to verify |
| H2 | Windowed host: no console window, and `Read-Host`, `PromptForChoice`, `Get-Credential` and `Write-Progress` shown as dialogs | ps2exe (`-noConsole`, `-credentialGUI`), SAPIEN Windows Forms engine | Missing | Slice 12 |
| H3 | Output routing in windowed mode: where `Write-Output`, `Write-Warning` and `Write-Error` go (an output window, a log file, or nowhere), and an error dialog for unhandled failures | ps2exe (`-noOutput`, `-noError`), SAPIEN | Missing | Slice 12 |
| H4 | Tray host: notification-area icon, a message loop that keeps running, a menu whose items call script functions, a timer function, notifications, About and Exit | SAPIEN tray engine | Missing | Slice 13 |
| H5 | Windows service host | Pro Tools, SAPIEN | Missing | Slice 17, on request; a scheduled task covers most background work |

### Content and dependencies

| ID | Feature | Others | Engine today | Plan |
|---|---|---|---|---|
| D1 | Dot-sourced scripts and files inside the module bundled | All | Supported | Done |
| D2 | Extra files and resources (include, exclude, whole module) | ps2exe (`-embedFiles`), Pro Tools, SAPIEN | Supported in the engine | Expose in hosts |
| D3 | Bundle installed modules from the build machine | Pro Tools, SAPIEN | Missing: reported as needed on the target machine | Slice 14 |
| D4 | Executable `#requires` directives | ps2exe and SAPIEN run them as PowerShell would | Compatible version and edition requirements in retained entries. Module and elevation requirements remain blocked. | Slice 7 |
| D5 | Native libraries | Varies | Carried in Package and Hybrid, rejected in Strict | Done |
| D6 | A settings file next to the executable | ps2exe (`-configFile`) | Missing | Later; the script can read its own settings file today |

### Targets

| ID | Feature | Others | Engine today | Plan |
|---|---|---|---|---|
| T1 | Windows and Linux x64 | ps2exe and SAPIEN are Windows only | Supported (Strict profiles) | Done |
| T2 | macOS and Arm64 | None | Preview (Milestone 30) | Next milestones |
| T3 | Windows x86 | ps2exe (`-x86`), SAPIEN | Runtime identifier mapping exists; no support-matrix profile | Slice 15 |
| T4 | Choice of PowerShell 7 version for executables (7.4 on `net8.0`, 7.6 on `net10.0`) | SAPIEN, Pro Tools | Semantic profiles exist for both; executables accept `net10.0` only | Slice 15 |
| T5 | Windows PowerShell 5.1 executables | ps2exe, SAPIEN, Pro Tools | Not supported | Decision needed: it means a .NET Framework host and a separate semantic profile |

### Signing and distribution

| ID | Feature | Others | Engine today | Plan |
|---|---|---|---|---|
| S1 | Authenticode signing of the output | SAPIEN; ps2exe output is signed afterwards | Supported for build-owned files | Done |
| S2 | Signing content inside the output | None documented | Missing | Slice 9 |
| S3 | macOS `codesign` and notarization, Linux detached signatures | None | Missing | Later, with T2 |
| S4 | MSI installer from the compiled output | SAPIEN | WiX installer support exists elsewhere in PowerForge, not connected | Slice 16 |

### Trust and transparency

| ID | Feature | Others | Engine today | Plan |
|---|---|---|---|---|
| X1 | Say whether the output carries readable script text | None | Recorded in the manifest only | Slice 10 |
| X2 | No absolute build paths in the output | — | The embedded source carries the build machine's path | Slice 10 |
| X3 | Warn about likely secrets before building | None | Missing | Slice 10 |
| X4 | Malware scan of the script before packaging (AMSI on Windows) | SAPIEN | Missing | Later, as a `Check` diagnostic |
| X5 | Obfuscation or encryption of the script | SAPIEN, Pro Tools, some ps2exe forks | Not planned: the key ships with the executable, so it only hides the script from casual readers |
| X6 | Restrict the executable to a domain, machine, user or MAC address | SAPIEN | Not planned: easy to bypass and it gives a false sense of control |
| X7 | Run as stored alternate credentials | SAPIEN | Not planned: it needs a credential inside the executable |

### Project settings

| ID | Feature | Others | Engine today | Plan |
|---|---|---|---|---|
| P1 | Saved build settings with several targets | Pro Tools package config, SAPIEN project | Supported (`powerforge.psproject.json`) | Hosts open and save it |
| P2 | E1 to E8, H2 to H4 and D3 settings per target in the project file | — | Missing | With each slice |

### Slice details

**Slice 11, startup manifest and threading.** Generate `app.manifest` entries for `supportedOS` and `longPathAware`, set `PerMonitorV2` DPI awareness for windowed and tray hosts, and run those hosts on an STA thread (forms and many COM objects need it). `ApartmentState` stays MTA for console hosts unless the build asks for STA.

**Slice 12, windowed host.** A `PSHostUserInterface` that shows prompts, choices, credential requests and progress as dialogs; an output policy (`Window`, `LogFile`, `None`) for the success, warning and error streams; and an error dialog with the message and a "copy details" action for unhandled failures. Exit codes behave as in the console host. `Check` warns when a windowed build contains `Read-Host` inside a loop, or console-only calls such as `[Console]::ReadKey()`.

**Slice 13, tray host.** Built on the windowed host. The build settings can declare the tray:

```json
{
  "Tray": {
    "Tooltip": "IT helper",
    "Menu": [
      { "Text": "Create a ticket", "Function": "New-HelpdeskTicket" },
      { "Separator": true },
      { "Text": "Open the IT portal", "Function": "Open-ItPortal" }
    ],
    "Timer": { "Function": "Test-PasswordExpiry", "IntervalMinutes": 60 },
    "SingleInstance": true
  }
}
```

Each menu item and the timer call a function in the script, so in Hybrid builds those functions can compile to .NET. The script can also reach a `$PowerForgeTray` object to add items and show notifications at run time. `Check` reports a declared function that doesn't exist. The icon is the executable icon (E1). About and Exit are always present.

**Slice 14, bundled installed modules.** `Check` resolves each external module from `PSModulePath` and offers `Bundle` or `RequiredOnTarget` for each. Bundled modules are recorded in the reviewed lock with name, version, path, file hashes and signature status, embedded in the output, and loaded from there at startup before `PSModulePath`. A module with native binaries for another platform is a diagnostic. Bundling never changes the module's files, so their signatures stay valid.

**Slice 15, target breadth.** Add `win-x86` profiles once tested, and allow `net8.0` executables for PowerShell 7.4 alongside `net10.0`. Windows PowerShell 5.1 waits for the decision in T5.

**Slice 16, installer output.** Optionally wrap the compiled executable in an MSI through PowerForge's existing WiX support: product name, version and icon come from E1 and E2, and the MSI is signed in the same session as the executable.

**Slice 17, service host.** A host that runs the script as a Windows service with start, stop and an optional loop function. Build it only for a concrete request.

## Acceptance

- `powershell check` on a bare script with `#requires -Version 7.4` and `Import-Module SomeModule` returns success, Package mode `Compiles`, one `RequiredOnTarget` dependency and a proposed lock, without a project file. Before slice 7, the `#requires` line is an `Error` for Package mode with a fix.
- Every `PowerShellCompilationDiagnosticCode` has a catalog entry; the test fails otherwise.
- `Check` and `Compile` return a result, not an exception, for a missing file, an unsupported input kind, a missing SDK and a rejected construct.
- Cancelling `Check` on a large module stops within one unit.
- A check with two requested targets keeps each target's action and reasons addressable in each mode.
- `Compile` succeeds when the selection and sources match the confirmed lock. Changing the mode, the target or an imported module name returns a diagnostic asking for a new check.
- `Check` on a bare script reports an unreadable icon supplied through `ExecutableOptions`.
- An executable built with an icon, version `1.2.3`, product and company names, and file description shows those values in Windows file properties. `RequireAdministrator` prompts for elevation on start.
- A rewritten Hybrid script dependency has a valid signature after extraction and matches the embedded hash.
- A windowed build of a script that calls `Read-Host` and `Get-Credential` shows both as dialogs and never opens a console window.
- A tray build with two menu items and a timer calls the named functions, keeps a single instance, and exits cleanly from the Exit item.
- A script that imports an installed module, built with that module bundled, runs on a machine where the module isn't installed.
- The JSON for `check`, `build` and `doctor` carries `SchemaVersion` and is covered by contract tests on the serialized shape.

## Open questions

- Should `Check` default to every supported target, or only to the host's own runtime, to keep it fast?
- Should the catalog live in code (an attribute on each enum member) or in a JSON resource that docs can also render?
- Should `Recommended` ever pick Strict automatically, or only suggest it when Strict compiles the whole program?
- T5: is a Windows PowerShell 5.1 executable worth a .NET Framework host, or is PowerShell 7 the only executable target?
- Slice 14: should bundling a module also bundle its license file and record it in the manifest?
- Slice 13: is the declared tray menu enough, or do scripts need to build the whole menu themselves?

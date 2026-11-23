# Check and compile a script from a simple host

Desktop tools, editor extensions and CI steps can use the same compiler service as the CLI. Check a script, module manifest, script module or module folder for one artifact selection, review its dependencies, then build from that review. See the [compiler guide](PowerForge.PowerShellCompilation.md) for commands and the [feature roadmap](#feature-roadmap) for packaging work.

## Host API

Use `PowerForge.PowerShellCompiler` from the `PowerForge.Compiler` NuGet
package. The desktop app, CLI and PowerShell cmdlet use the same compiler to
resolve source paths, select a semantic profile and prepare a build.

| Method | Use |
|---|---|
| `Check(request)` | Analyze an artifact selection and propose a dependency review. |
| `Prepare(request)` | Resolve a selection into a build specification. Set build controls such as caching and signing on the returned specification. |
| `BuildReviewed(spec, review)` | Build after confirming the reviewed target and dependency graph. |
| `Build(spec)` | Build with the specification's dependency lock or explicit unattended resolution policy. |
| `CheckEnvironment(target)` | Validate a target and check the selected .NET SDK. |

```csharp
var compiler = new PowerShellCompiler();
var request = new PowerShellCompilationRequest("Example.ps1")
{
    Mode = PowerShellCompilationMode.Strict
};

var check = compiler.Check(request);
// Display check.Diagnostics and review check.Dependencies before confirming.
if (check.ProposedReview is { } confirmedReview)
{
    var spec = compiler.Prepare(request);
    var result = compiler.BuildReviewed(spec, confirmedReview);
}
```

A request can also contain an ordered set of loose scripts. Set `EntryPoint`
when they form one executable. Keep executable sources under the entrypoint's directory. A loose set selected as a binary module
defaults to Strict. An exact `Target` supplies the framework, semantic profile
and deployment settings. Other explicit artifact and target settings must
agree with it. The CLI accepts the same source, target and resource flags
on `powershell check` and `powershell build` so a review can cover a loose
source set or a custom executable deployment. Trimmed and NativeAOT builds require an explicit RID.
`Build-PowerShellArtifact -WhatIf` validates source and target selection before it reports the proposed action.

### Check

Supply a source path and any mode, kind, target, resource or output settings for the intended build. A bare `.ps1` defaults to a Package executable. Hosts use `PowerShellCompilationSupportMatrixService` for target choices and call `Check` separately for each mode or target they want to display. Check analyzes the source without executing it or producing an artifact.

`Explanation` reports compiled, retained and rejected units. `Recommendation` gives the action and reasons for the selected target. `Diagnostics` carries the code, feature ID, message, severity, source location when available and suggested fix. An error blocks the selection. A warning can indicate a retained unit or another advisory. `CanProceed` indicates that the result contains a `ProposedReview`.

### Dependencies and the reviewed lock

1. Check the intended build. Review the dependency decisions in `Dependencies`, including each `ExternalRequirement` that the target PowerShell must resolve.
2. Confirm the proposed review for that selection. It carries schema version 1, the normalized target contract and the dependency graph. In the CLI, save it with `--write-review`, inspect it and pass it to `--reviewed-check`.
3. Compile with the matching source, kind, mode, target and resource settings. The compiler verifies the target and resolves the dependency graph again before emission. Run a fresh check when the selection or reviewed inputs change.

Save review files outside a module's source directory so they do not change the dependency inventory. The review writer requires a new file path. The desktop app uses the same compiler when you select **Review dependencies** and **Confirm and compile**. See the [compiler app](../PowerForge.Compiler.App/README.md).

The existing CLI `--dependency-lock` option accepts a dependency graph for scripted builds. Custom command providers use the provider and dependency lock workflow.

Check and reviewed library builds both require the selected source to compile without omitted units. A direct Hybrid library build using an explicit dependency lock can emit a partial DLL. Inspect its omitted units and verify the intended public ABI before distributing it.

### Package-mode script forms

Retained executable entries preserve compatible `#requires -Version` and `#requires -PSEdition` directives. Check and build validate the embedded host's supported major and minor versions and edition. Embedded script dependencies use the same requirements policy.

Retained executables reject module, elevation, assembly and application-specific shell requirements.

`using module`, `using assembly`, and `exit` inside dot-sourced dependencies stay unsupported for now, but are reported by `Check` with a severity and a fix.

The console host gets the root script's native `exit` code from PowerShell.
PowerShell runs `finally` and keeps a child command's exit separate from the
application's exit. Direct plain `Read-Host` reads standard input. Redirected
input preserves the original line terminator, empty lines and end-of-input observed
on the pinned hosts. Secure and masked input, confirmation, credentials, and
general host access remain outside the console contract. Check rejects input
when it cannot rule out protected options in parameter defaults.

### Build and progress

On success, `PowerShellCompilationBuildResult` gives the artifact and manifest paths. The manifest contains the compiled and fallback units and their dispositions. On failure, inspect `Error` and `Failure` for the stage and any source information. `ContainsEmbeddedPowerShellSource` identifies builds that carry PowerShell source. Package and Hybrid can carry readable script content. Strict compiles the admitted code. A Strict module can still carry PowerShell source as a staged resource, which also sets `ContainsEmbeddedPowerShellSource`.

`Check`, `Build` and `BuildReviewed` accept a cancellation token and can report phase changes through `IProgress<PowerShellCompilationProgress>`. Each report carries a phase. Dependency planning and shaping can finish their current operation before cancellation takes effect.

### Check the environment

`CheckEnvironment` reports the selected .NET SDK version. With an explicit modern target contract, it validates the target options and checks that the selected SDK describes the target framework. An invalid target has its own diagnostic and corrective action. Prerequisite checks for platform linkers, NativeAOT tools and signing certificates remain open.

## Packaging work

The packaging slices extend the current console and module builds. The identifiers correspond to the feature tables below.

| Slice | Feature and remaining work |
|---|---|
| 7 | Complete executable `#requires` support beyond the version and edition checks available today. |
| 8 | Set the executable icon, file properties, Windows subsystem and requested execution level (E1 to E4). Validate options before building and record them in the manifest. |
| 9 | Sign rewritten scripts and other build-owned content before embedding, then sign the outer artifact. Keep existing third-party signatures. Record signing roles and thumbprints in the manifest. |
| 10 | Surface embedded source in host results, use relative source paths in retained output, and warn about likely secrets before packaging. |
| 11 | Add startup manifest and threading options (E5 to E7). |
| 12 | Add a windowed host and its output policies (H2 and H3). |
| 13 | Add a tray host and single instance support (H4 and E8). |
| 14 | Bundle installed modules (D3). |
| 15 | Expand executable targets (T3 and T4). T5 requires a separate decision. |
| 16 | Package a compiled executable as an MSI (S4). |
| 17 | Add a Windows service host (H5) when there is a concrete request. |

## Feature roadmap

Everything people expect from a tool that turns PowerShell into an executable, compared with the packagers they already know (ps2exe and its forks ps12exe and PS2EXE.Core, PowerShell Pro Tools, SAPIEN PowerShell Studio), and where this engine stands. The IDs are stable; refer to them from issues and PRs. "Slice" identifies the packaging work listed above and described below.

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
| H1 | Console host: script arguments, native root `exit`, and plain `Read-Host` | All | Supported for retained console executables | Both-host exit and redirected input comparisons pass. Interactive terminal editing and broader encoding remain open |
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
| T5 | Windows PowerShell 5.1 executables | ps2exe, SAPIEN, Pro Tools | No executable profile. The compiler uses Windows PowerShell 5.1 as a semantic reference and supports applicable module targets | A .NET Framework executable host would need its own profile |

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

## Packaging acceptance

- A Windows executable shows the selected icon, version and product details in file properties. `RequireAdministrator` prompts for elevation on start.
- A signed, rewritten Hybrid script dependency keeps a valid signature after extraction and matches its recorded hash.
- A windowed build shows `Read-Host` and `Get-Credential` as dialogs without opening a console window.
- A tray build with two menu items and a timer calls the named functions, keeps a single instance and exits cleanly from its Exit item.
- A build that bundles an installed module runs on a machine where that module is absent from `PSModulePath`.

## Open questions

- T5 asks whether a Windows PowerShell 5.1 executable is worth a .NET Framework host.
- Slice 14 needs a decision on bundling the module license and recording it in the manifest.
- Slice 13 needs a decision on whether scripts can build the entire tray menu at runtime.

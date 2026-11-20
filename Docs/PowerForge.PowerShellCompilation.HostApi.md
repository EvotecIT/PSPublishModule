# Check and compile a script from a simple host

Status: proposal. Nothing on this page is implemented yet.

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

Most real scripts start with `#requires`. Package mode should handle the common forms instead of refusing them:

- `#requires -Version` and `-PSEdition`: emit an equivalent startup check in the executable, with the same failure message PowerShell gives.
- `#requires -Modules` with names and versions: treat each as a `RequiredOnTarget` dependency in the lock and check it at startup.
- `#requires -RunAsAdministrator`: check elevation at startup.
- Anything else (`-Assembly`, `-ShellId`): stays a diagnostic with a fix.

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

1. Sign authored scripts and modules (`.ps1`, `.psm1`, `.psd1`) before they are embedded, so the extracted copies carry valid signatures.
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

A simple host can start once slices 1 to 3 exist; the rest improve it.

## Acceptance

- `powershell check` on a bare script with `#requires -Version 7.4` and `Import-Module SomeModule` returns success, Package mode `Compiles`, one `RequiredOnTarget` dependency and a proposed lock, without a project file. Before slice 7, the `#requires` line is an `Error` for Package mode with a fix.
- Every `PowerShellCompilationDiagnosticCode` has a catalog entry; the test fails otherwise.
- `Check` and `Compile` return a result, not an exception, for a missing file, an unsupported input kind, a missing SDK and a rejected construct.
- Cancelling `Check` on a large module stops within one unit.
- A check with two requested targets keeps each target's action and reasons addressable in each mode.
- `Compile` succeeds when the selection and sources match the confirmed lock. Changing the mode, the target or an imported module name returns a diagnostic asking for a new check.
- `Check` on a bare script reports an unreadable icon supplied through `ExecutableOptions`.
- An executable built with an icon, version `1.2.3`, product and company names, and file description shows those values in Windows file properties. `RequireAdministrator` prompts for elevation on start.
- The JSON for `check`, `build` and `doctor` carries `SchemaVersion` and is covered by contract tests on the serialized shape.

## Open questions

- Should `Check` default to every supported target, or only to the host's own runtime, to keep it fast?
- Should the catalog live in code (an attribute on each enum member) or in a JSON resource that docs can also render?
- Should `Recommended` ever pick Strict automatically, or only suggest it when Strict compiles the whole program?

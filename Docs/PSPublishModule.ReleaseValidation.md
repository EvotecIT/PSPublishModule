# Validate release artifacts

Use `Invoke-ReleaseValidation` or `powerforge validate-release` to inspect final packages and run isolated product probes without publishing anything. Keep package names, expected files, runtime matrices, and product assertions in the consuming repository. PowerForge handles package inspection, signature verification, process timeouts, tool installation, and temporary workspaces.

## Run a JSON contract

```powershell
Import-Module PSPublishModule
Invoke-ReleaseValidation -Config './Build/package-validation.json' -Variables @{
    PackageRoot = './Artifacts/packages'
}
```

The same contract works from the CLI:

```text
powerforge validate-release --config Build/package-validation.json --variable PackageRoot=./Artifacts/packages --output json
```

Paths in the contract are relative to `ProjectRoot`, which is relative to the JSON file. Pass `-ProjectRoot` or `--project-root` to override it. `Version` is optional for a package set: PowerForge can obtain it from the first package and require the remaining packages to match.

With `Packages.SameVersion = false`, package consumers choose their individual versions and artifact probes use their own inspected versions. PowerForge does not infer a release-wide version from those artifacts: the report and top-level `{Version}` remain empty unless the caller supplies one. A supplied version still constrains module, unified CLI manifest, and tool checks; the mixed package set keeps its individual versions. Raw dotnet publish manifests do not carry version metadata, so they cannot support this version comparison.

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.release-validation.schema.json",
  "SchemaVersion": 1,
  "ProjectRoot": "..",
  "Packages": {
    "Path": "{PackageRoot}",
    "ExactSet": true,
    "SameVersion": true,
    "Items": [
      {
        "Id": "Example.Library",
        "RequiredEntries": ["lib/net8.0/Example.Library.dll", "README.md"],
        "ForbiddenEntries": ["tools/**"],
        "DependencyFrameworks": ["net8.0"]
      }
    ]
  }
}
```

`*` matches within a directory; `**` spans directories. Package dependency checks use NuGet metadata, not text searches through the `.nuspec`. Set `VerifySignatures`, `RequireAuthorSignature`, or `AuthorCertificateFingerprints` when the package contract requires signing. Author fingerprints use SHA-256; payload Authenticode checks use certificate thumbprints.

Module signature `Include` patterns are alternatives: PowerForge verifies every file matched by any pattern. The selection must contain at least one file; empty or unmatched selections fail validation.

On Unix, extracted module files and staged consumer inputs retain ordinary permission bits, including executable permissions; setuid, setgid, and sticky bits are not applied. Package, tool, and CLI artifact versions use NuGet version identity, so equivalent spellings such as `1.2.3` and `1.2.3.0` compare equal.

## Choose the checks that belong to the product

- `Packages`: package identity, version, contents, symbol contents, dependency groups, runtime-only dependencies, and signatures. Primary and symbol archives are matched by their embedded NuGet identity, not their filenames. Consumer and tool feeds use canonical package filenames without renaming the input artifacts.
- `Modules`: module ZIP or directory contents, full module identity including `PrivateData.PSData.Prerelease`, architecture, assembly versions, and optional Authenticode requirements. Each configured `Hosts` entry receives a fresh copy of the validated module through `POWERFORGE_MODULE_PATH` and a disposable `POWERFORGE_TEST_ROOT`, with its working directory set to the module copy. Directory inputs are copied before validation; probes shipped inside that directory run from the copy so their `PSScriptRoot` is isolated too. These copies isolate ordinary module and relative writes, not scripts that deliberately access external paths.
- `Consumers`: copy a product-owned smoke project into a temporary workspace, restore the exact staged first-party package bytes, and run it for the selected frameworks. Configure at least one nonblank entry in `Frameworks` or `WindowsFrameworks`. Validation rejects a consumer with no executable framework on the current host before restore. Other dependencies may come from `DependencySources`.
- `Tools`: install the exact local `.NET` tool package into an isolated tool directory, optionally also through a local manifest, and run commands using `{ToolPath}` and `{WorkRoot}`. Validation checks the declared command's shim and local manifest entry even when `Commands` is empty. The user's global tools and NuGet package cache are not used for these installations. With `IncludeManifestInstall`, `{ToolPath}` probes must leave `WorkingDirectory` unset or set it to `{WorkRoot}` so `dotnet tool run` selects the isolated manifest. Use tool-path-only validation when a probe requires another working directory.
- `CliArtifacts`: compare a publish manifest with the runtime/framework/style matrix from `PublishConfigPath`, or with an explicit matrix. Staged checks also validate file existence, containment, and the supplied asset set. Unzipped outputs are checked through the declared executable or, when no executable is declared, a nonempty output directory.
- `Commands`: run a product probe with structured arguments, an expected exit code, output assertions, and a timeout.

CLI and module manifests read by validation, and validation/publish JSON configuration files, have a 16 MiB byte limit. Validation directory inputs, package archives, and metadata readers reject Unix special files such as FIFOs before opening them, so a missing pipe writer cannot stall preparation. Staged CLI payload and metadata paths must stay inside `StagingRoot` without traversing symbolic links or reparse points, including the staging root itself. This is a filesystem preflight, not protection against another process replacing files concurrently.

## Keep domain-specific interpretation local

For a probe whose output needs product-specific interpretation, use the shared process command and inspect its result:

```powershell
$probe = Invoke-ValidationCommand -Command @{
    FileName = './Artifacts/example.exe'
    Arguments = @('diagnostics', '--json')
    TimeoutSeconds = 60
    OutputJsonKind = 'Object'
}
$diagnostics = $probe.StdOut | ConvertFrom-Json
if ($diagnostics.pendingWrites -gt 0) { throw 'The product has unfinished writes.' }
```

The expected exit code defaults to zero. Set `ExpectedExitCode = $null` only when the product intentionally interprets nonzero exit codes itself, such as a licensed feature being unavailable. Timeout, cancellation, and output limits still apply. The result exposes `ExitCode`, `StdOut`, and `StdErr` separately; ordering between the two streams is not guaranteed.

Command `Platforms` accepts `Windows`, `Linux`, and `OSX`, case-insensitively. An empty list runs on every platform. Unknown or empty names fail validation instead of silently skipping the probe.

An overridden command `PATH` controls executable lookup on Windows and Unix; removing it disables bare-name lookup. Use an absolute executable path when no search path is wanted. Relative search entries use the command's working directory. The standalone C# `RunCommandAsync` API defaults `ProjectRoot` to the current directory and overlays caller variables case-insensitively without changing the caller's dictionary.

Variable names use ASCII letters, digits, and underscores, starting with a letter or underscore. For example, `Package_Root` expands in `{package_root}`. Unsupported variable names fail before validation starts; missing supported placeholders report an error. JSON object braces, such as in `{"count":2}`, are not placeholders.

Every validation process retains at most 1,048,576 characters per output stream. This includes product probes, package verification, tool installation, consumer restore/build commands, and staged PowerShell actions. Exceeding either limit fails validation even when the process exits successfully. Temporary-directory cleanup is best-effort and does not replace a probe's result or cancellation.

Validation probes run in an owned Windows job or a process group on 64-bit Linux/macOS. PowerForge terminates remaining processes in that scope when a probe returns, times out, or is cancelled, even if the original process has already exited. Output captured before the boundary includes unfinished lines. On Unix, a program can deliberately leave the group by creating another group or session; process ownership is a cleanup mechanism, not a security sandbox.

On Linux systems whose C library lacks the spawn working-directory extension, launch uses `/bin/sh` to change the child directory and replace itself with the probe. Arguments remain separate literal values, and the process-group identity is preserved. This fallback requires `/bin/sh` and follows that shell's standard environment initialization.

PowerShell can also author the complete contract with `New-ConfigurationReleaseValidation` inside `Invoke-ReleaseValidation -Settings { ... }`. Use `-JsonOnly -JsonPath` to export it without running probes.

## Validate after release staging

An existing unified release can invoke the same JSON contract before publication:

```json
"Validation": {
  "AfterStaging": [
    {
      "Name": "Final package and runtime checks",
      "ConfigPath": "release-validation.json",
      "TimeoutSeconds": 1800
    }
  ]
}
```

PowerForge supplies the selected release lanes and paths such as `{ModuleArchive}`, `{PackageRoot}`, `{ReleaseManifestPath}`, and `{StagingRoot}`. Unselected lanes are not validated. If selection removes every contract, the action reports a successful skip; an originally empty contract still fails. Additional `Commands` remain active regardless of lane selection. A tools-only run retains the CLI target's version instead of borrowing a module version.

When an after-staging action is enabled, the standalone `Packages` lane builds without publishing. NuGet and project GitHub publication wait until every enabled action succeeds, then use the captured staged package, symbol, and release ZIP files without rebuilding or resolving versions again. A validation failure or cancellation prevents those publication steps. Existing publication choices and preflight checks remain in effect; build-only requests do not start publishing. Deferred NuGet publication uses the same all-package preflight and dependency ordering as an ordinary repository release, including blocking dependents when a selected dependency fails to publish. Same-run package checkpoints retain the resolved feed and repository context.

An explicit release target filter also skips a `CliArtifacts` contract whose target is absent from the effective selected tool plan. Targets retained by the planner as dependencies are still validated, and product commands remain active. Without an explicit target filter, a missing target remains an error.

JSON actions retain their own `ProjectRoot`, resolved relative to the validation file, just as standalone validation does. Script context uses the resolved root of the first executed module, package, tool, or Apple lane, falling back to the release configuration directory.

The action's `TimeoutSeconds` budget starts before preparation and configuration loading, and continues through artifact checks and probes. Caller cancellation stays distinct from an action timeout. C# callers can use `ReleaseValidationService.LoadAsync(path, cancellationToken)` for cancellable configuration loading; `Load(path)` remains available for synchronous callers.

The shared validation version includes the module's prerelease label. For a tools-only release whose tool artifacts share one version, PowerForge supplies that version to script context, `POWERFORGE_RELEASE_VERSION`, and JSON command `{Version}` variables. If tool targets have different versions, the shared version is empty; scripts can inspect each asset's version, and a `CliArtifacts` contract selects its named target's version.

When `CliArtifacts.PublishConfigPath` names an input of the current release's publish plan, validation uses that plan's effective runtime/framework/style combinations, including release overrides. An unrelated publish configuration or an explicit matrix remains an independent requirement; standalone validation continues to use the configured matrix.

Selecting the tools release lane does not prohibit its portable bundles, installers, or Store outputs. When `ToolOutputs` or `SkipToolOutputs` deselects the base `Tool` output, its `CliArtifacts` contract is skipped; additional product commands still run and receive the common version of the selected packaged outputs. Set `CliArtifacts.ToolsOnly` explicitly only when the product contract permits tool and metadata entries alone. Standalone validation of a unified CLI manifest infers the version from the selected target and rejects missing or inconsistent artifact versions.

`ConfigPath` and the existing script-based `FilePath` are mutually exclusive. Script action options such as `Environment`, `WorkingDirectory`, and `PreferWindowsPowerShell` do not apply to a JSON action: declare each command's environment and directory, or a module's `Hosts`, in the validation contract.

Named HTTP NuGet feeds are captured as concrete endpoints. Their existing named-source authentication stays in the repository's NuGet configuration; if validation changes that source or its credentials, publication fails before pushing rather than selecting a new destination or copying credentials into a temporary file.

This validates artifacts and probe behavior. It does not prove that a package is publicly available, authorize publication, or deploy a running service.

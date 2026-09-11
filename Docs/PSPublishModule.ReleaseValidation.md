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

## Choose the checks that belong to the product

- `Packages`: package identity, version, contents, symbol contents, dependency groups, runtime-only dependencies, and signatures.
- `Modules`: module ZIP or directory contents, manifest version and architecture, assembly versions, and optional Authenticode requirements. `ProbeScript` runs in each configured `Hosts` entry with `POWERFORGE_MODULE_PATH` and a disposable `POWERFORGE_TEST_ROOT`.
- `Consumers`: copy a product-owned smoke project into a temporary workspace, restore the exact staged first-party package bytes, and run it for the selected frameworks. Other dependencies may come from `DependencySources`.
- `Tools`: install the exact local `.NET` tool package into an isolated tool directory, optionally also through a local manifest, and run commands using `{ToolPath}` and `{WorkRoot}`. The user's global tools and NuGet package cache are not used for these installations.
- `CliArtifacts`: compare a publish manifest with the runtime/framework/style matrix from `PublishConfigPath`, or with an explicit matrix. Staged checks also validate file existence, containment, and the supplied asset set.
- `Commands`: run a product probe with structured arguments, an expected exit code, output assertions, and a timeout.

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

PowerForge supplies the selected release lanes and paths such as `{ModuleArchive}`, `{PackageRoot}`, `{ReleaseManifestPath}`, and `{StagingRoot}`. Unselected lanes are not validated. A tools-only run retains the CLI target's version instead of borrowing a module version.

`ConfigPath` and the existing script-based `FilePath` are mutually exclusive. Script action options such as `Environment`, `WorkingDirectory`, and `PreferWindowsPowerShell` do not apply to a JSON action: declare each command's environment and directory, or a module's `Hosts`, in the validation contract.

This validates artifacts and probe behavior. It does not prove that a package is publicly available, authorize publication, or deploy a running service.

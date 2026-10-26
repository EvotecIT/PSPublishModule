# Explicit Desktop dependency assets

`New-ConfigurationBuild -NETDesktopDependencyAssetFrameworks` selects an asset
framework for named NuGet packages in the Windows PowerShell Desktop payload.
The map is empty by default. Adding a selection changes only the declared package
assets in the Desktop publish output; PowerShell Core keeps its published assets.

```powershell
New-ConfigurationBuild -NETProjectPath "$PSScriptRoot/../Sources/Example/Example.csproj" `
    -NETFramework @('net472', 'net8.0') `
    -NETDesktopDependencyAssetFrameworks @{
        'System.Memory' = 'netstandard2.0'
        'System.Buffers' = 'netstandard2.0'
        'System.Threading.Tasks.Extensions' = 'netstandard2.0'
        'SQLitePCLRaw.provider.e_sqlite3' = 'netstandard2.0'
    }
```

Package IDs are compared with `OrdinalIgnoreCase`. Values initially support only
`netstandard2.0`, and the Desktop target must be .NET Framework 4.7.2 or later.
The setting requires the binary project's resolved NuGet assets. It does not
select a package version, add a dependency, or replace the project's restore graph.

The same map is available as `Build.DesktopDependencyAssetFrameworks` in pipeline
JSON and as `BuildLibraries.DesktopDependencyAssetFrameworks` in a configuration
segment:

```json
{
  "Type": "BuildLibraries",
  "BuildLibraries": {
    "DesktopDependencyAssetFrameworks": {
      "System.Memory": "netstandard2.0"
    }
  }
}
```

A supplied map replaces the earlier map as a whole. An omitted or null segment
map preserves the previous selection; an explicit empty object clears it. The
PowerShell equivalent is `-NETDesktopDependencyAssetFrameworks @{}`. The typed
`BuildConfigurationRequest` uses `NETDesktopDependencyAssetFrameworksSpecified`
to distinguish an omitted parameter from an explicitly supplied empty map.

The normalizer resolves each selected asset from the **same package ID and
version** recorded in the entry project's assets file. All selections are
validated before copying. Other dependencies, runtime/native files and Core
payloads retain their existing publish/copy behavior.

Assembly identity changes are restricted to the three qualified Microsoft facade
pairs for `System.Memory`, `System.Buffers` and
`System.Threading.Tasks.Extensions`. Their Desktop binding policy records the
exact before/after assembly versions, public key token and selected file's
SHA-256; the resolver requires that recorded identity and hash. Adding a package
to the configuration does not extend that policy. Selecting
`SQLitePCLRaw.provider.e_sqlite3` changes its package asset only and does not add a
runtime binding rule for the provider.

The qualified facade pairs use neutral culture and public key token
`cc7b13ffcd2ddd51`:

| Assembly | Published assembly version | Selected assembly version |
| --- | --- | --- |
| `System.Memory` | `4.0.5.0` | `4.0.2.0` |
| `System.Buffers` | `4.0.5.0` | `4.0.2.0` |
| `System.Threading.Tasks.Extensions` | `4.2.4.0` | `4.2.1.0` |

The hash is computed from the selected resolved-package file's bytes for each
build. A policy entry authorizes only that exact file hash and identity pair;
another file with the same simple assembly name is not sufficient.

A nonempty selection is explicit binary-build intent. When the project cannot be
resolved, planning records `NETDesktopDependencyAssetFrameworks` as a reason a
`.csproj` is required. Configured frameworks also contribute the existing
`NETFramework` reason. Refresh-only and no-build modes retain their existing
gates; the option does not authorize an implicit rebuild. The selection is part
of the compiled-module release-contract fingerprint, so frozen publication
cannot silently change the asset map.

Command help is generated from the XML documentation on
`NewConfigurationBuildCommand.NETDesktopDependencyAssetFrameworks`.

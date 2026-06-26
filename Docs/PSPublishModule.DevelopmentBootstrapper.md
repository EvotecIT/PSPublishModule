# PowerShell module development bootstrappers

PSPublishModule already generates production module bootstrappers during staging and packaging. Development bootstrappers are different: they live in the source checkout and make local F5/import workflows easier while the final packaged module remains production-shaped.

Use this feature when a source checkout needs to import a freshly built local .NET binary before the module is packaged. It is intentionally a development-time convenience, not a replacement for the packaged bootstrapper.

## Inventory summary

A scan across the Evotec repository root found three useful families:

- Script-only modules that do not need binary development loading.
- Simple binary modules with older always-on development `.psm1` loaders.
- A small set of newer or conflict-prone binary modules, such as PSWriteOffice, that need environment-gated local binary loading and sometimes AssemblyLoadContext support on PowerShell Core.

That does not justify many per-module templates. The shared scope is intentionally small:

- `Off`: do not generate development binary loading logic.
- `Environment`: generate development binary loading logic, but only activate it when the module-specific environment variable is `true`.
- `Auto`: generate development binary loading logic and use it automatically when a matching local build output exists.

`Environment` is the default when `New-ConfigurationBuild -NETDevelopmentBinaries` is used. This keeps local source `bin` outputs from taking over a module import unless the maintainer opts in for that shell.

## Use cases

Use development bootstrappers for:

- A binary-backed module where maintainers run `dotnet build` or F5 from an IDE and then import the source module without packaging it first.
- A module with dependency conflicts that should use `NETAssemblyLoadContext` on PowerShell Core during local development, matching the packaged module behavior more closely.
- A source tree with checked-in `Public`, `Private`, `Classes`, `Enums`, or `Lib` layout where PowerForge can regenerate the source `.psm1` safely.
- A team repository where the source `.psm1` should include the loader, but local binaries should only win when a maintainer explicitly sets an environment variable.

Avoid development bootstrappers for:

- Pure script modules with no local binary output.
- Hand-authored single-file `.psm1` modules unless you explicitly choose `ReplaceSingleFile`.
- Source layouts that depend on custom `Information.IncludePS1` folders not represented by the standard source loader. PowerForge preserves those files by default so custom dot-sourcing is not lost.
- CI/package validation. The packaged/staged bootstrapper remains the proof for release behavior.

## Build configuration

The main entrypoint is still `New-ConfigurationBuild` / `Invoke-ModuleBuild`.

```powershell
New-ConfigurationBuild `
    -NETProjectPath "$PSScriptRoot\..\Sources\Example\Example.csproj" `
    -NETProjectName 'Example' `
    -NETFramework 'net8.0', 'net472' `
    -NETAssemblyLoadContext `
    -NETDevelopmentBinaries
```

Optional overrides exist for unusual layouts:

- `-NETDevelopmentBinariesMode Environment|Auto|Off`
- `-NETDevelopmentBinariesPath <path-to-bin-root>`
- `-NETDevelopmentBinariesEnvironmentVariable <name>`
- `-NETDevelopmentConfigurationEnvironmentVariable <name>`
- `-NETDevelopmentSourceBootstrapperMode PreserveSingleFile|ReplaceSingleFile`

When no environment names are supplied, PowerForge derives:

- `<MODULE>_USE_DEVELOPMENT_BINARIES`
- `<MODULE>_DEVELOPMENT_CONFIGURATION`

The generated source `.psm1` probes `bin\<Configuration>\<TFM>\<Module>.dll`, preferring Core-compatible TFMs in PowerShell Core and desktop-compatible TFMs in Windows PowerShell. PowerShell Core uses the generated development ALC loader when `NETAssemblyLoadContext` is enabled; Windows PowerShell keeps the direct-import compatibility path.

## Opt-in import flow

With the default `Environment` mode, a maintainer opts into local binaries per shell:

```powershell
$env:EXAMPLE_USE_DEVELOPMENT_BINARIES = 'true'
$env:EXAMPLE_DEVELOPMENT_CONFIGURATION = 'Debug'
Import-Module .\Example.psd1 -Force
```

Unset the variable, or set it to anything other than `true`, to go back to the packaged/source fallback:

```powershell
Remove-Item Env:\EXAMPLE_USE_DEVELOPMENT_BINARIES -ErrorAction SilentlyContinue
Import-Module .\Example.psd1 -Force
```

Use `Auto` only when a repository's normal source import should always prefer a matching local build output when one exists:

```powershell
New-ConfigurationBuild `
    -NETProjectPath "$PSScriptRoot\..\Sources\Example\Example.csproj" `
    -NETProjectName 'Example' `
    -NETDevelopmentBinaries `
    -NETDevelopmentBinariesMode Auto
```

## Source PSM1 maintenance

PowerForge only overwrites source `.psm1` files when the layout is generated-safe:

- Standard script folders: `Classes`, `Enums`, `Private`, `Public`.
- Binary source layout: checked-in `Lib\<layout>` folders.
- Existing PowerForge-generated development bootstrapper files.

Hand-authored single-file source modules are preserved by default. To replace one intentionally:

```powershell
New-ConfigurationBuild `
    -NETProjectPath "$PSScriptRoot\..\Sources\Example\Example.csproj" `
    -NETDevelopmentBinaries `
    -NETDevelopmentSourceBootstrapperMode ReplaceSingleFile
```

When `DevelopmentBinariesMode` is `Off`, PowerForge removes development loading logic from known generated source bootstrappers. For `Lib`-based source layouts it regenerates the normal source binary loader instead of deleting the module entrypoint.

## JSON configuration

Build-level JSON uses unprefixed `Build` properties:

```json
{
  "Build": {
    "Name": "Example",
    "SourcePath": "Module",
    "CsprojPath": "Sources/Example/Example.csproj",
    "DevelopmentBinariesMode": "Environment",
    "DevelopmentBinariesPath": "Sources/Example/bin",
    "DevelopmentBinariesEnvironmentVariable": "EXAMPLE_USE_DEVELOPMENT_BINARIES",
    "DevelopmentConfigurationEnvironmentVariable": "EXAMPLE_DEVELOPMENT_CONFIGURATION",
    "DevelopmentSourceBootstrapperMode": "PreserveSingleFile"
  }
}
```

BuildLibraries segments may use either the unprefixed names or the cmdlet-style `NET*` aliases:

```json
{
  "Type": "BuildLibraries",
  "BuildLibraries": {
    "NETProjectPath": "Sources/Example/Example.csproj",
    "NETAssemblyLoadContext": true,
    "NETDevelopmentBinaries": true,
    "NETDevelopmentBinariesMode": "Environment",
    "NETDevelopmentBinariesPath": "Sources/Example/bin"
  }
}
```

For build-level `DevelopmentBinariesPath`, relative paths are resolved from the config file directory, like `CsprojPath`. For segment-level `DevelopmentBinariesPath`, relative paths stay source-root relative, matching the rest of the BuildLibraries segment behavior.

## Repository guidance

Prefer fixing PowerForge/PSPublishModule when several repositories need the same development import behavior. Repository-local `.psm1` loaders should be kept for truly unusual source layouts only. Common patterns that belong in PowerForge include:

- Environment-gated local binary loading.
- AssemblyLoadContext source imports on PowerShell Core.
- Direct-import reuse when a development assembly is already loaded.
- Runtime/native probing for development outputs.
- Safe source `.psm1` regeneration rules for standard script folders and `Lib` layouts.

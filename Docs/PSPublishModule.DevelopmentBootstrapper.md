# PowerShell module development bootstrappers

PSPublishModule already generates production module bootstrappers during staging and packaging. Development bootstrappers are different: they live in the source checkout and make local F5/import workflows easier while the final packaged module remains production-shaped.

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

When no environment names are supplied, PowerForge derives:

- `<MODULE>_USE_DEVELOPMENT_BINARIES`
- `<MODULE>_DEVELOPMENT_CONFIGURATION`

The generated source `.psm1` probes `bin\<Configuration>\<TFM>\<Module>.dll`, preferring Core-compatible TFMs in PowerShell Core and desktop-compatible TFMs in Windows PowerShell. PowerShell Core uses the generated development ALC loader when `NETAssemblyLoadContext` is enabled; Windows PowerShell keeps the direct-import compatibility path.

# Packaging modules and runnable delivery bundles

## Understand the three boundaries

1. **Source project:** your module functions, manifest and delivery files in the repository.
2. **Module package:** the finalized module payload after merging, file selection and signing.
   `New-ConfigurationDelivery -InternalsPath 'Files'` includes that delivery directory in this payload.
3. **Delivery bundle:** an additional folder or ZIP that contains the complete module, any bundled
   runtime dependencies and selected copies of the delivery files. This is what a deployment system extracts.

Publishing to a PowerShell repository sends the normal module package. Building a delivery ZIP does
not change the repository package format or move files out of the module. Declare `Unpacked` for a
directory and `Packed` for a ZIP; both support the same delivery layout settings.

| Setting | Meaning | Relative to |
|---|---|---|
| `Delivery.InternalsPath` | Source of packaged launcher, configuration and support files | Module root |
| Artefact `Path` | Where the output folder or ZIP is saved on the build machine | Project root |
| Artefact `DeliveryPath` | Base destination for promoted delivery files; default `.` | Inside the bundle |
| Artefact `DeliveryMappings[].Source` | File or directory to select from the finalized delivery payload | Packaged `InternalsPath` |
| Artefact `DeliveryMappings[].Destination` | Target file name or directory | Bundle `DeliveryPath` |
| Artefact `ModulesPath` | Directory containing the main module's named folder | Inside the bundle |
| Artefact `RequiredModulesPath` | Directory containing remaining dependencies' named folders | Inside the bundle |

In JSON, the last two settings are `Configuration.RequiredModules.ModulesPath` and
`Configuration.RequiredModules.Path`. The cmdlet exposes them as top-level parameters.

## Whole-tree layout

Use `New-ConfigurationArtefact -Layout Delivery` to create a deployment folder or
ZIP containing launcher and configuration files at its root by default. This layout supports
`Unpacked` and `Packed`; the module remains a module with its manifest and runtime
assets. `Script` and `ScriptPacked` generate script entry points and use their own
layout instead.

Declare delivery content through `New-ConfigurationDelivery`. The artefact copies
the packaged content from its configured `InternalsPath` to the bundle root and
places the main module and remaining dependencies under `Modules` by default:

```text
Run.ps1
config.json
Modules/
  DemoTool/
    DemoTool.psd1
    DemoTool.psm1
    Files/...
  RemainingDependency/...
```

The complete original module payload stays under `Modules/DemoTool`, including
its delivery internals. This preserves module-relative assets and the generated
install/update commands. Root delivery files are copies of those packaged files;
the builder does not move or modify the signed source payload.

## Build configuration

Inside the normal module build configuration:

```powershell
New-ConfigurationBuild -Enable -MergeModuleOnBuild -MergeFunctionsFromApprovedModules `
    -SignModule:$SignModule -CertificateThumbprint $CertificateThumbprint

New-ConfigurationDelivery -Enable -InternalsPath 'Files' -Sign:$SignModule `
    -GenerateInstallCommand -GenerateUpdateCommand

New-ConfigurationArtefact -Type Unpacked -Layout Delivery -Enable `
    -Path 'Artefacts/Unpacked' -AddRequiredModules -RequiredModulesSource Installed

New-ConfigurationArtefact -Type Packed -Layout Delivery -Enable `
    -Path 'Artefacts/Packed' -AddRequiredModules -RequiredModulesSource Installed `
    -IncludeTagName -ID 'VersionedBundle'

New-ConfigurationArtefact -Type Packed -Layout Delivery -Enable `
    -Path 'Artefacts/Packed' -AddRequiredModules -RequiredModulesSource Installed `
    -ArtefactName 'DemoTool.zip' -ID 'CurrentBundle'
```

The two packed declarations produce a versioned ZIP and a stable filename through
the native artefact builder. No post-build `Move-Item` or `Compress-Archive` is
needed. Use relative `ModulesPath` and `RequiredModulesPath` only when a different
module subdirectory is required. Delivery files cannot overwrite those module
directories. Layout validation runs before existing output is cleared.

### Choosing a different bundle structure

Set `DeliveryPath` on each artefact to put delivery files in a relative folder.
Omit it or use `.` to keep them at the bundle root. Configure the module folders
independently with `ModulesPath` and `RequiredModulesPath`:

```powershell
New-ConfigurationArtefact -Type Packed -Layout Delivery -Enable `
    -Path 'Artefacts/Packed' -DeliveryPath 'Launchers/Operations' `
    -ModulesPath 'Application/Modules' -RequiredModulesPath 'Shared/Modules' `
    -AddRequiredModules -RequiredModulesSource Installed
```

This produces `Launchers/Operations/Run.ps1` and configuration files beside it,
the main module under `Application/Modules`, and remaining dependencies under
`Shared/Modules`. The original delivery files remain inside the module payload.
Each artefact can choose a different destination without changing its source
`InternalsPath` or the module layout.

`DeliveryPath` accepts a relative folder; absolute paths, parent traversal and
collisions with module directories are rejected before output is changed.
Launcher contents are copied as authored. A launcher in the example folder can
refer to the main module with a path such as
`../../Application/Modules/DemoTool/DemoTool.psd1` relative to `$PSScriptRoot`.

### Select, rename and rearrange delivery content

Use `DeliveryMappings` when the bundle needs a different hierarchy from the source tree.
For example, keep this source directory inside the module:

```text
Files/
  Start.ps1
  Scripts/
    Synchronize.ps1
    Diagnostics/Check.ps1
  Configuration/
    default.json
    config.schema.json
  Documentation/OperatorGuide.md
```

Map individual files or directory contents in the artefact declaration:

```powershell
$deliveryMappings = @(
    @{ Source = 'Start.ps1';     Destination = 'bin/Run.ps1' }
    @{ Source = 'Scripts';       Destination = 'jobs' }
    @{ Source = 'Configuration'; Destination = 'etc' }
    @{ Source = 'Documentation'; Destination = 'docs' }
)

New-ConfigurationDelivery -Enable -InternalsPath 'Files'

New-ConfigurationArtefact -Type Packed -Layout Delivery -Enable `
    -Path 'Artefacts/Packed' -DeliveryPath '.' `
    -DeliveryMappings $deliveryMappings `
    -ModulesPath 'Modules' -RequiredModulesPath 'Modules' `
    -AddRequiredModules -RequiredModulesSource Installed
```

The ZIP contains:

```text
bin/Run.ps1
jobs/Synchronize.ps1
jobs/Diagnostics/Check.ps1
etc/default.json
etc/config.schema.json
docs/OperatorGuide.md
Modules/DemoTool/DemoTool.psd1
Modules/DemoTool/DemoTool.psm1
Modules/DemoTool/Files/...
Modules/RemainingDependency/...
```

`Source` names are relative to `Files`, so write `Scripts`, not `Files/Scripts`.
A **file source** maps to an exact target file name, allowing renaming. A **directory source**
maps its packaged contents recursively into the destination folder; the source folder name is
not added automatically. Directory substructure is preserved. `Source = '.'` selects the whole
packaged delivery tree; `Destination = '.'` is valid for directory sources.

When `DeliveryMappings` is nonempty it replaces the default whole-tree promotion. Unmapped files
remain inside `Modules/DemoTool/Files`; they do not appear as extra top-level delivery copies.
Omit mappings, use `null`, or pass an empty array for the whole-tree default. Each artefact has
its own mappings, so an operator ZIP and an automation ZIP can select different layouts from the
same finalized module. `DeliveryPath = 'Application'` prefixes every mapped destination with
`Application/`; it does not affect the module or dependency directories.

Sources and destinations are **literal relative paths**. Either slash style is accepted. Wildcards,
absolute paths and parent traversal are rejected. A source must exist and select at least one
file from the finalized package; mappings cannot reintroduce an excluded file or copy an arbitrary
repository file. A selected directory contributes only files included in that package. Conflicting
file destinations, file/directory conflicts and overlap with module directories fail before output
is changed. Duplicate destinations are errors even if both rules select the same source file;
there is no last-rule-wins behavior. Files can be copied to multiple distinct destinations.

The mapping above renames `Start.ps1` to `bin/Run.ps1` without changing its bytes. Author imports
relative to its **destination**, for example:

```powershell
Import-Module (Join-Path $PSScriptRoot '../Modules/DemoTool/DemoTool.psd1') -ErrorAction Stop
```

If dependencies are in a separate directory, the launcher must also make that directory available
through `PSModulePath` or import those dependencies before the main module. Choosing a folder name
does not configure module discovery automatically.

### JSON configuration

The same artefact configuration can be supplied through the module pipeline JSON:

```json
{
  "Type": "Packed",
  "Configuration": {
    "Enabled": true,
    "Layout": "Delivery",
    "Path": "Artefacts/Packed",
    "DeliveryPath": ".",
    "DeliveryMappings": [
      { "Source": "Start.ps1", "Destination": "bin/Run.ps1" },
      { "Source": "Scripts", "Destination": "jobs" },
      { "Source": "Configuration", "Destination": "etc" },
      { "Source": "Documentation", "Destination": "docs" }
    ],
    "RequiredModules": {
      "Enabled": true,
      "ModulesPath": "Modules",
      "Path": "Modules",
      "Source": "Installed"
    }
  }
}
```

Place this object in `Segments` alongside the enabled delivery options segment. It describes
the artefact only; the full pipeline still declares the module build and delivery internals.
The schema requires both `Source` and `Destination` on each mapping and rejects unknown fields.

### Dependencies and signing

See [module signing configuration](PSPublishModule.Signing.md) for approved signing identities and PFX secret references.

`AddRequiredModules` bundles dependencies that remain required at runtime. An approved donor
whose eligible helpers have been merged and which has no remaining runtime requirement is removed
from the built manifest and is not copied into the bundle. Approval alone does not make a module
safe to remove: binary/provider exports, other runtime state and explicit module references can
still require the original module.

Mapped files follow the same delivery signing preference as the whole-tree layout. The complete
module is signed according to module signing settings. Mapping a launcher changes its destination,
not its contents or its signing policy. Use `CopyFiles`/`CopyDirectories` for additional repository
content; those existing options are separate from mappings over the finalized delivery payload.

Use one build declaration and pass the selected certificate thumbprint as build
configuration. Certificate enumeration and interactive selection do not belong
before the builder: unsigned builds need no certificate, and unattended builds
need an explicit signing identity. Keep delivery and module signing controlled by
the same operator setting when both are intended to be signed.

`Packed` and `Unpacked` preserve the delivery signing preference at default and custom destinations. With delivery
signing disabled (and `IncludeInternals` not enabled), promoted launcher files
remain unsigned while module signing still applies to module payloads.

## Publishing to private repositories

Artefact layout is independent of repository publication. `Build` can produce
the bundle locally; `Publish` can additionally publish the module to its configured
repository, including Artifactory through PSResourceGet. Configure the build's
run mode once through `Build-Module -RunMode $RunMode` or `New-ConfigurationGate`.

Use supported deferred credential sources rather than reading a secret in the
wrapper:

```powershell
New-ConfigurationPublish -Type PowerShellGallery -Tool PSResourceGet `
    -JFrogBaseUri 'https://packages.example.com/artifactory' `
    -JFrogRepository 'powershell-local' -RepositoryName 'CompanyModules' `
    -RepositoryCredentialUserName 'build-user' `
    -RepositoryCredentialSecretEnvironmentVariable 'ARTIFACTORY_TOKEN' -Enabled
```

The private repository receives the normal module package. A deployment ZIP is a
separate artefact for the deployment system; it is not an alternative NuGet feed
format. Required-module bundling uses the configured installed/download source
and does not change which repository receives the published module.

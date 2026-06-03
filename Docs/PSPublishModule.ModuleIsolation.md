# PSPublishModule Module Isolation

`Import-IsolatedModule` imports known dependency-sensitive PowerShell modules through
curated PowerForge isolation profiles.

The feature is intended for PowerShell 7+ sessions where one service module's binary
dependencies cannot safely share the default load context with another module or host.
Instead of trying to control import order in the default context, PowerForge copies the
profiled module to a temporary workspace, patches the configured script or manifest
surface, loads selected assemblies through a module-scoped `AssemblyLoadContext`, and
imports the generated wrapper into the current session.

This is a maintained-profile model, not a generic arbitrary-module loader. Each supported
module has explicit rules for what to copy, patch, preload, import, and expose.

## Quick Start

Import the module that owns `Import-IsolatedModule`, then import one or more supported
profiles:

```powershell
Import-Module PSPublishModule

$exo = Import-IsolatedModule -Profile ExchangeOnlineManagement -PassThru
$teams = Import-IsolatedModule -Profile MicrosoftTeams -PassThru
$graph = Import-IsolatedModule -Profile MicrosoftGraphAuthentication -PassThru

$exo, $teams, $graph |
    Format-Table ProfileName, ModuleName, ContextName, IsolatedImportPath
```

After the isolated import, use the profiled module's normal commands:

```powershell
Connect-ExchangeOnline -ShowBanner:$false
Connect-MicrosoftTeams
Connect-MgGraph -NoWelcome
```

The isolated import only prepares and imports the command surface. It does not authenticate
to Exchange, Teams, Graph, or any other service.

## Built-In Profiles

### ExchangeOnlineManagement

```powershell
Import-IsolatedModule -Profile ExchangeOnlineManagement
Connect-ExchangeOnline -ShowBanner:$false

Get-ConnectionInformation |
    Format-List UserPrincipalName, ConnectionUri, ModuleName

Get-EXOMailbox -ResultSize 5 |
    Select-Object DisplayName, PrimarySmtpAddress

Disconnect-ExchangeOnline -Confirm:$false
```

The profile currently:

- resolves `ExchangeOnlineManagement` from `PSModulePath` when `-Path` is omitted,
- requires `ExchangeOnlineManagement` 3.9.0 or newer,
- copies the module into `%TEMP%\PowerForge\IsolatedModules\ExchangeOnlineManagement`
  unless `-WorkRoot` is supplied,
- patches `netCore\ExchangeOnlineManagement.psm1`,
- patches the copied `ExchangeOnlineManagement.psd1` manifest so EXO keeps its upstream
  module name, version, and export metadata,
- loads `Microsoft.Exchange.Management.RestApiClient.dll` and
  `Microsoft.Exchange.Management.ExoPowershellGalleryModule.dll` through the same
  `ExchangeOnlineManagement.ALC` context,
- exposes public types from `Microsoft.Exchange.Management.*` and
  `Microsoft.Online.CSE.RestApiPowerShellModule.*` as type accelerators so EXO script
  type literals keep working.

### MicrosoftTeams

```powershell
Import-IsolatedModule -Profile MicrosoftTeams
Connect-MicrosoftTeams

Get-Team |
    Select-Object -First 10 DisplayName, GroupId, Visibility

Disconnect-MicrosoftTeams
```

The profile currently:

- resolves `MicrosoftTeams` from `PSModulePath` when `-Path` is omitted,
- requires `MicrosoftTeams` 7.8.0 or newer,
- copies the module into `%TEMP%\PowerForge\IsolatedModules\MicrosoftTeams` unless
  `-WorkRoot` is supplied,
- generates `MicrosoftTeams.ALC.psm1` and a patched `MicrosoftTeams.ALC.psd1` manifest
  that preserves the upstream export contract,
- loads the Teams connect, Teams cmdlet, policy administration, and ConfigAPI binary
  surfaces through the same `MicrosoftTeams.ALC` context,
- rewrites selected copied submodule script imports so they load binary modules through
  the shared context,
- imports the required Teams submodule manifests after the isolated binary load,
- exposes public types from `Microsoft.Teams.*` as type accelerators.

### MicrosoftGraphAuthentication

```powershell
Import-IsolatedModule -Profile MicrosoftGraphAuthentication
Connect-MgGraph -NoWelcome

Get-MgContext |
    Format-List Account, TenantId, Scopes

Disconnect-MgGraph
```

The profile currently:

- resolves `Microsoft.Graph.Authentication` from `PSModulePath` when `-Path` is omitted,
- requires `Microsoft.Graph.Authentication` 2.36.0 or newer,
- copies the module into
  `%TEMP%\PowerForge\IsolatedModules\MicrosoftGraphAuthentication` unless `-WorkRoot`
  is supplied,
- generates `Microsoft.Graph.Authentication.ALC.psm1` and a patched
  `Microsoft.Graph.Authentication.ALC.psd1` manifest,
- clears copied `NestedModules` so Graph binaries are not imported through the default
  loader before the isolated wrapper runs,
- loads Graph, Kiota, MSAL, Azure.Identity, and supporting dependency assemblies into
  `Microsoft.Graph.Authentication.ALC`,
- imports `Microsoft.Graph.Authentication.Core.dll` and `Microsoft.Graph.Authentication.dll`
  through the same isolated context,
- removes copied Authenticode signature blocks from the patched script because the
  generated wrapper necessarily changes the original script,
- replaces Graph's path-based binary command discovery with explicit cmdlet and alias
  exports for the supported command surface,
- exposes public types from `Microsoft.Graph.*` as type accelerators.

## Path Resolution

When `-Path` is omitted, the command resolves the profile's module name with:

```powershell
Get-Module -ListAvailable -Name <profile module name>
```

If several versions are visible on `PSModulePath`, the highest version is selected.

Use `-Path` when you want a specific installed copy, a saved module payload, or an
alternate manifest name in the module base:

```powershell
$module = Get-Module -ListAvailable ExchangeOnlineManagement |
    Sort-Object Version -Descending |
    Select-Object -First 1

Import-IsolatedModule -Profile ExchangeOnlineManagement -Path $module.ModuleBase
```

Directory paths are treated as module bases:

```powershell
Import-IsolatedModule `
    -Profile MicrosoftGraphAuthentication `
    -Path 'C:\Modules\Microsoft.Graph.Authentication\2.37.0'
```

Manifest paths are accepted. The manifest file is used for version validation and manifest
patching, while its parent directory is treated as the module base:

```powershell
Import-IsolatedModule `
    -Profile MicrosoftGraphAuthentication `
    -Path 'C:\Modules\Microsoft.Graph.Authentication\2.37.0\Microsoft.Graph.Authentication.psd1'
```

Alternate manifest names are also supported when the manifest lives beside the module
payload that the profile expects:

```powershell
Import-IsolatedModule `
    -Profile MicrosoftTeams `
    -Path 'C:\Lab\ContosoTeams\7.9.0\ContosoTeams.psd1'
```

In that example, `C:\Lab\ContosoTeams\7.9.0` is the module base. The profile still expects
the configured Teams script and binary layout below that directory. A manifest stored away
from the module payload is not enough by itself; the parent directory of the manifest must
contain the module files that the selected profile knows how to patch.

Non-manifest file paths are resolved to their parent directory. The service then tries to
find a manifest for validation and patching from the profile's configured manifest path,
then from a single `.psd1` in the module base, then from `<moduleBase>\<moduleBaseName>.psd1`.

## Validation Behavior

`Import-IsolatedModule` fails before importing when a required contract is missing.

Runtime validation:

- Windows PowerShell 5.1 is rejected because `AssemblyLoadContext` requires PowerShell 7+
  on CoreCLR.

Profile validation:

- unknown profile names fail and list the available profiles,
- profile names are resolved case-insensitively.

Module discovery validation:

- without `-Path`, the module must be visible through `PSModulePath`,
- with `-Path`, the supplied file or directory must exist,
- if the profile declares a minimum version, a parseable module manifest with
  `ModuleVersion` must be available,
- the resolved version must be greater than or equal to the profile minimum.

Profile-layout validation:

- the configured source script path must exist under the module base,
- profiles that preserve a manifest must have a source manifest available,
- the source manifest must contain a `RootModule` entry that can be patched,
- profile-declared binary imports must exist when the generated wrapper runs.

Work-root behavior:

- if `-WorkRoot` is omitted, the generated copy is created under
  `%TEMP%\PowerForge\IsolatedModules\<profile>\<guid>`,
- if `-WorkRoot` is supplied, the directory is created when missing,
- each import uses a new GUID child folder,
- generated copies can remain locked until the PowerShell process exits.

## Inspecting Results

Use `-PassThru` to get the generated import details:

```powershell
$result = Import-IsolatedModule -Profile MicrosoftGraphAuthentication -PassThru

$result |
    Format-List ProfileName, ModuleName, SourceModuleBase, ContextName,
        IsolatedImportPath, IsolatedScriptPath, IsolatedManifestPath, WorkPath
```

Inspect the exported commands:

```powershell
Get-Command -Module Microsoft.Graph.Authentication.ALC |
    Sort-Object CommandType, Name |
    Format-Table CommandType, Name, ModuleName
```

Inspect assemblies loaded into the profile context:

```powershell
[System.Runtime.Loader.AssemblyLoadContext]::All |
    Where-Object Name -eq $result.ContextName |
    ForEach-Object {
        $_.Assemblies |
            Sort-Object { $_.GetName().Name } |
            Select-Object @{Name = 'Name'; Expression = { $_.GetName().Name } },
                @{Name = 'Version'; Expression = { $_.GetName().Version } },
                Location
    }
```

Confirm that a profile did not place Graph assemblies in the default context:

```powershell
[System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies |
    Where-Object { $_.GetName().Name -like 'Microsoft.Graph*' } |
    Select-Object @{Name = 'Name'; Expression = { $_.GetName().Name } }, Location
```

## Diagnostics With WorkRoot

Use `-WorkRoot` when you want deterministic generated files for inspection:

```powershell
$workRoot = 'C:\Temp\PowerForge-Isolated'
$result = Import-IsolatedModule `
    -Profile MicrosoftTeams `
    -WorkRoot $workRoot `
    -PassThru

Get-ChildItem -LiteralPath $result.WorkPath -Recurse |
    Select-Object -First 40 FullName

Get-Content -LiteralPath $result.IsolatedScriptPath -TotalCount 80
```

Use `-WhatIf` to preview without creating or importing the generated wrapper:

```powershell
Import-IsolatedModule -Profile ExchangeOnlineManagement -WhatIf
```

## Multiple Profiles In One Session

Supported profiles can be imported in the same PowerShell 7+ process:

```powershell
$profiles = foreach ($profile in @(
    'ExchangeOnlineManagement',
    'MicrosoftTeams',
    'MicrosoftGraphAuthentication'
)) {
    Import-IsolatedModule -Profile $profile -PassThru
}

$profiles |
    Format-Table ProfileName, ModuleName, ContextName
```

Each profile uses its own context name. PowerShell command names are still global in the
runspace, so normal command-name conflicts can still occur if two modules export the same
command. The isolation layer is about assembly loading, not command renaming.

## Profile Design

Profiles are maintained in PowerForge so support is explicit and testable. A profile
declares:

- module name and minimum supported version,
- source script path and generated script name,
- optional manifest path and generated manifest name when the upstream export contract
  should be preserved,
- whether copied manifests should clear `NestedModules`,
- whether the generated wrapper appends the source script body,
- number of original bootstrap lines to replace when source script content is used,
- source line fragments to skip when appending the original script body,
- whether copied Authenticode signature blocks should be removed from patched scripts,
- additional profile-maintained script lines to append after isolated binary loading,
- dependency assemblies to load into the context without importing as PowerShell modules,
- binary assemblies to import as PowerShell modules through the isolated context,
- copied script-module binary imports that should be rewritten to use the shared context,
- namespace prefixes to bridge into PowerShell type resolution,
- stable load-context name.

This keeps the cmdlet surface small while allowing future profiles to add module-specific
patching rules without duplicating loader code.

## Current Limitations

- PowerShell 7+ only. Windows PowerShell cannot use `AssemblyLoadContext`.
- Profiles are curated; arbitrary third-party modules are not automatically isolated.
- The generated wrapper imports commands into the current runspace; command names are not
  namespaced or renamed.
- Generated module copies are process-local working artifacts and can remain locked until
  the PowerShell process exits.
- Assembly isolation does not isolate service authentication state or remote service state.

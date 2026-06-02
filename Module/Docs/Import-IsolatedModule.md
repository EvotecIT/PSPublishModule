---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Import-IsolatedModule
## SYNOPSIS
Imports a known PowerShell module through a curated AssemblyLoadContext isolation profile.

## SYNTAX
### __AllParameterSets
```powershell
Import-IsolatedModule [-Profile] <string> [-Name <string>] [-Path <string>] [-WorkRoot <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Import-IsolatedModule is intended for PowerShell 7+ sessions where one service module's binary
dependencies cannot safely share the default AssemblyLoadContext with another module. The command
resolves the selected profile, copies the target module to a temporary working folder, generates a
profile-specific wrapper, loads selected binary assemblies through a module-scoped AssemblyLoadContext,
and imports the generated wrapper into the current runspace.

Built-in profiles are maintained by PowerForge and are deliberately curated. Use the
ExchangeOnlineManagement profile when Exchange Online needs to coexist with Az.Storage or another
module that has already loaded incompatible Microsoft.OData assemblies. Use the MicrosoftTeams
profile when Teams should keep its MSAL, IdentityModel, policy administration, and ConfigAPI binary
stack out of the default context.

This command does not authenticate to the isolated module. After importing the profile, call the
target module's normal connection command, such as Connect-ExchangeOnline or Connect-MicrosoftTeams.
Windows PowerShell 5.1 is not supported because AssemblyLoadContext is only available on CoreCLR.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-IsolatedModule -Profile ExchangeOnlineManagement
```

Imports the latest available ExchangeOnlineManagement module through the
ExchangeOnlineManagement.ALC load context.

### EXAMPLE 2
```powershell
Import-Module Az.Storage
Import-IsolatedModule -Profile ExchangeOnlineManagement
Connect-ExchangeOnline
Get-EXOMailbox -ResultSize 1
```

Keeps Az.Storage's Microsoft.OData 7.6 assemblies in the default context while Exchange Online loads
Microsoft.OData 7.22 assemblies in ExchangeOnlineManagement.ALC.

### EXAMPLE 3
```powershell
Import-IsolatedModule -Profile MicrosoftTeams
Connect-MicrosoftTeams -UseDeviceAuthentication
Get-Team
```

Imports Teams cmdlets from MicrosoftTeams.ALC and then uses the normal Teams connection workflow.

### EXAMPLE 4
```powershell
$result = Import-IsolatedModule -Profile MicrosoftTeams -PassThru
$result | Format-List ProfileName, ContextName, IsolatedImportPath, WorkPath
```

Returns the generated wrapper location, selected profile, and load-context name.

### EXAMPLE 5
```powershell
$path = "$HOME\Documents\PowerShell\Modules\ExchangeOnlineManagement\3.9.2"
Import-IsolatedModule -Profile ExchangeOnlineManagement -Path $path
```

Uses the profile rules but bypasses PSModulePath discovery by pointing at a specific module base folder.

### EXAMPLE 6
```powershell
Import-IsolatedModule -Profile MicrosoftTeams -WorkRoot C:\Temp\PowerForge-Isolated -PassThru
```

Creates the generated module copy under the supplied root instead of the default temp location.

### EXAMPLE 7
```powershell
Import-IsolatedModule -Profile ExchangeOnlineManagement -WhatIf
```

Uses ShouldProcess support to show the intended operation without preparing or importing the wrapper.

## PARAMETERS

### -Name
Use this only when testing a compatible module installed under a non-standard module name. The
selected profile still controls script patching, binary imports, and the load-context name.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
The result includes the source module path, generated script and manifest paths, import path,
work root, load-context name, and profile counts.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Use Path to test a specific installed version or a copied module payload. Directory paths are treated
as module bases. File paths are resolved to their parent directory.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Profile
Supported built-in profiles are ExchangeOnlineManagement and MicrosoftTeams. Profile names are
resolved case-insensitively.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -WorkRoot
When omitted, generated module copies are created under the system temp folder in
PowerForge\IsolatedModules\<profile>. Use WorkRoot when you want to inspect generated wrappers
or keep diagnostic artifacts in a known location.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.IsolatedModuleImportResult`

## RELATED LINKS

- None

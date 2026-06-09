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
Import-IsolatedModule [-Profile] <string> [-Name <string>] [-Path <string>] [-WorkRoot <string>] [-PassThru] [-PreferIsolatedModulePath] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Import-IsolatedModule is intended for PowerShell 7+ sessions where one service module's binary
dependencies cannot safely share the default AssemblyLoadContext with another module. The command
resolves the selected profile, copies the target module to a temporary working folder, generates a
profile-specific wrapper, loads selected binary assemblies through a module-scoped AssemblyLoadContext,
and imports the generated wrapper into the current runspace.

Built-in profiles are maintained by PowerForge and are deliberately curated. Use the
ExchangeOnlineManagement profile when Exchange Online needs to keep its binary surface out of the
default context. Use the MicrosoftTeams profile when Teams should keep its MSAL, IdentityModel,
policy administration, and ConfigAPI binary stack isolated. Use the MicrosoftGraphAuthentication
profile when Graph authentication assemblies should be loaded through a profile-specific context.

This command does not authenticate to the isolated module. After importing the profile, call the
target module's normal connection command, such as Connect-ExchangeOnline or Connect-MicrosoftTeams.
Windows PowerShell 5.1 is not supported because AssemblyLoadContext is only available on CoreCLR.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-IsolatedModule -Profile ExchangeOnlineManagement
$commands = 'Get-EXOMailbox', 'Get-ConnectionInformation'
Connect-ExchangeOnline -ShowBanner:$false -CommandName $commands
Get-ConnectionInformation | Format-Table UserPrincipalName, ConnectionUri
Get-EXOMailbox -ResultSize 5 | Select-Object DisplayName, PrimarySmtpAddress
Disconnect-ExchangeOnline -Confirm:$false
```

Imports ExchangeOnlineManagement through the curated profile, connects with the normal EXO
cmdlets, runs a small mailbox query, and disconnects cleanly.

### EXAMPLE 2
```powershell
Import-Module Az.Storage
$defaultODataBefore = [System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies |
    Where-Object { $_.GetName().Name -like 'Microsoft.OData*' }
Import-IsolatedModule -Profile ExchangeOnlineManagement
Connect-ExchangeOnline -ShowBanner:$false
$defaultODataBefore | ForEach-Object { $_.GetName().Name + ' ' + $_.GetName().Version }
Get-EXOMailbox -ResultSize 1 | Select-Object DisplayName, PrimarySmtpAddress
Disconnect-ExchangeOnline -Confirm:$false
```

Keeps Az.Storage's Microsoft.OData 7.6 assemblies in the default context while Exchange Online loads
Microsoft.OData 7.22 assemblies in ExchangeOnlineManagement.ALC.

### EXAMPLE 3
```powershell
Import-IsolatedModule -Profile MicrosoftTeams
Connect-MicrosoftTeams -UseDeviceAuthentication
$teams = Get-Team
$teams | Select-Object -First 10 DisplayName, GroupId, Visibility
Disconnect-MicrosoftTeams
```

Imports Teams cmdlets from MicrosoftTeams.ALC and then uses the normal Teams connection workflow.

### EXAMPLE 4
```powershell
$result = Import-IsolatedModule -Profile MicrosoftTeams -PassThru
$result | Format-List ProfileName, ContextName, IsolatedImportPath, IsolatedScriptPath, WorkPath
Get-Command -Module MicrosoftTeams.ALC | Measure-Object
[System.Runtime.Loader.AssemblyLoadContext]::All |
    Where-Object Name -eq $result.ContextName |
    ForEach-Object {
        $_.Assemblies |
            Where-Object { $_.GetName().Name -like 'Microsoft.Teams*' } |
            Select-Object -ExpandProperty FullName
    }
```

Returns the generated wrapper details and confirms that Teams assemblies were loaded in the
profile-specific AssemblyLoadContext.

### EXAMPLE 5
```powershell
$module = Get-Module -ListAvailable ExchangeOnlineManagement |
    Sort-Object Version -Descending |
    Select-Object -First 1
$result = Import-IsolatedModule -Profile ExchangeOnlineManagement -Path $module.ModuleBase -PassThru
$result | Select-Object ProfileName, ModuleName, SourceModuleBase, IsolatedImportPath
Get-Module ExchangeOnlineManagement | Select-Object Name, Version, ModuleBase
```

Uses the profile rules but bypasses PSModulePath discovery by pointing at a specific module base
folder. The profile still validates its minimum supported module version.

### EXAMPLE 6
```powershell
$workRoot = Join-Path $env:TEMP 'PowerForge-Isolated'
$result = Import-IsolatedModule -Profile MicrosoftTeams -WorkRoot $workRoot -PassThru
Get-ChildItem -LiteralPath $result.WorkPath -Recurse | Select-Object -First 20 FullName
Get-Content -LiteralPath $result.IsolatedScriptPath -TotalCount 40
```

Creates the generated module copy under the supplied root and inspects the generated wrapper.

### EXAMPLE 7
```powershell
Import-Module Az.Storage
$result = Import-IsolatedModule -Profile ExchangeOnlineManagement -PreferIsolatedModulePath -PassThru
Connect-ExchangeOnline -ShowBanner:$false
Import-Module Contoso.ExchangeWorker
Invoke-ContosoExchangeWorker
$result | Format-List IsolatedModuleResolutionPath, PreferIsolatedModulePath
Disconnect-ExchangeOnline -Confirm:$false
```

Prepends the generated isolated module parent path to PSModulePath for the current process. This is
useful when a downstream module imports ExchangeOnlineManagement by name and should bind to the
isolated copy instead of the original installed module.

### EXAMPLE 8
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

### -PreferIsolatedModulePath
When set, Import-IsolatedModule prepends the generated module parent directory to PSModulePath after
the isolated import succeeds. Later Import-Module calls and RequiredModules resolution in the same
PowerShell process can then find the isolated copy before the original installed module. This is
session-scoped and intentionally opt-in because it changes module-name resolution for the process.

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

### -Profile
Supported built-in profiles are ExchangeOnlineManagement, MicrosoftTeams, and
MicrosoftGraphAuthentication. Profile names are resolved case-insensitively.

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

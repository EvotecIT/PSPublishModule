---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ManagedModuleRepository
## SYNOPSIS
Gets, tests, or exports saved managed module repository profiles.

## SYNTAX
### __AllParameterSets
```powershell
Get-ManagedModuleRepository [[-Name] <string[]>] [-Test] [-ExportPath <string>] [-Force] [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Repository profiles contain non-secret feed settings. Use this cmdlet to review repository shape, test local
onboarding readiness, or export profile definitions for another machine without creating another command family.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-ManagedModuleRepository
```

Returns all repository profiles visible to the current user.

### EXAMPLE 2
```powershell
Get-ManagedModuleRepository -Name Company
```

Returns the saved Azure Artifacts profile named Company.

### EXAMPLE 3
```powershell
Get-ManagedModuleRepository -Name Company -Test
```

Returns local prerequisite and bootstrap readiness for the saved repository profile.

### EXAMPLE 4
```powershell
Get-ManagedModuleRepository -Name Company -ExportPath .\Company.repository.json -Force
```

Writes a non-secret JSON profile file that can be imported with Initialize-ManagedModuleRepository -Path.

## PARAMETERS

### -ExportPath
Optional destination JSON file for exporting the selected profiles.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Overwrite an existing export file.

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

### -Name
Optional repository profile names. When omitted, all visible profiles are returned.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ProfileName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Return profile objects after exporting.

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

### -Scope
Profile store scope to read. The default reads user profiles first, then machine-wide profiles.

```yaml
Type: ModuleRepositoryProfileScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: User, Machine, All

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Test
Return readiness information instead of profile definitions.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PSPublishModule.ModuleRepositoryProfileResult
PSPublishModule.ModuleRepositoryProfileReadinessResult` — Readiness information for a saved private module repository profile.

## RELATED LINKS

- None

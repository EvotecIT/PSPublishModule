---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Import-ManagedModuleRepository
## SYNOPSIS
Imports managed module repository profiles from a non-secret profile file.

## SYNTAX
### __AllParameterSets
```powershell
Import-ManagedModuleRepository [-Path] <string> [-Force] [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Imports managed module repository profiles from a non-secret profile file.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-ManagedModuleRepository -Path 'C:\Path'
```


## PARAMETERS

### -Force
Replaces existing managed repository profiles with matching names.

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

### -PassThru
Returns imported profiles. The command is quiet by default, like Import-PSGetRepository.

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
Path to a profile file exported by Get-ManagedModuleRepository -ExportPath.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: RequiredResourceFile
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Profile store scope to import into.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PSPublishModule.ModuleRepositoryProfileResult` — User-facing private module repository profile saved by PSPublishModule.

## RELATED LINKS

- None

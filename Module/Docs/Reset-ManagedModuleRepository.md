---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Reset-ManagedModuleRepository
## SYNOPSIS
Resets managed module repository profiles to PSPublishModule defaults.

## SYNTAX
### __AllParameterSets
```powershell
Reset-ManagedModuleRepository [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Resets managed module repository profiles to PSPublishModule defaults.

## EXAMPLES

### EXAMPLE 1
```powershell
Reset-ManagedModuleRepository -PassThru
```


## PARAMETERS

### -PassThru
Returns the default profile written by the reset operation.

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
Profile store scope to reset.

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

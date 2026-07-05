---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Unregister-ManagedModuleRepository
## SYNOPSIS
Unregisters a saved managed module repository profile.

## SYNTAX
### __AllParameterSets
```powershell
Unregister-ManagedModuleRepository [-Name] <string[]> [-PassThru] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Unregisters a saved managed module repository profile.

## EXAMPLES

### EXAMPLE 1
```powershell
Unregister-ManagedModuleRepository -Name @('Name')
```


## PARAMETERS

### -Name
Profile name to unregister.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ProfileName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### -PassThru
Returns true when a profile was removed, otherwise false.

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
Profile store scope to unregister from.

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

- `System.String[]`

## OUTPUTS

- `System.Boolean`

## RELATED LINKS

- None

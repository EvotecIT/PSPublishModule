---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Remove-ModuleRepositoryProfile
## SYNOPSIS
Removes a saved private module repository profile.

## SYNTAX
### __AllParameterSets
```powershell
Remove-ModuleRepositoryProfile [-Name] <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Removes a saved private module repository profile.

## EXAMPLES

### EXAMPLE 1
```powershell
Remove-ModuleRepositoryProfile -Name 'Name'
```


## PARAMETERS

### -Name
Profile name to remove.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ProfileName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Boolean`

## RELATED LINKS

- None

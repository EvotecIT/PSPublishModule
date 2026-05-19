---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleRepositoryProfile
## SYNOPSIS
Gets saved private module repository profiles.

## SYNTAX
### __AllParameterSets
```powershell
Get-ModuleRepositoryProfile [[-Name] <string>] [<CommonParameters>]
```

## DESCRIPTION
Gets saved private module repository profiles.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-ModuleRepositoryProfile -Name 'Name'
```


## PARAMETERS

### -Name
Optional profile name. When omitted, all profiles are returned.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ProfileName
Possible values:

Required: False
Position: 0
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

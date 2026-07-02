---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-BenchmarkArtifacts
## SYNOPSIS
Sets requested benchmark artifacts.

## SYNTAX
### __AllParameterSets
```powershell
Set-BenchmarkArtifacts [-Kind] <Object[]> [<CommonParameters>]
```

## DESCRIPTION
Sets requested benchmark artifacts.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-BenchmarkArtifacts -Kind @('Value')
```


## PARAMETERS

### -Kind
Artifact kinds.

```yaml
Type: Object[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
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

- `System.Object`

## RELATED LINKS

- None

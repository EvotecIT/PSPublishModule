---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Add-BenchmarkMetadata
## SYNOPSIS
Adds a suite-specific provenance value to benchmark metadata artifacts.

## SYNTAX
### __AllParameterSets
```powershell
Add-BenchmarkMetadata [-Name] <string> [-Value] <string> [<CommonParameters>]
```

## DESCRIPTION
Adds a suite-specific provenance value to benchmark metadata artifacts.

## EXAMPLES

### EXAMPLE 1
```powershell
Add-BenchmarkMetadata -Name 'runtime' -Value 'net10.0'
```


## PARAMETERS

### -Name
Metadata name.

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

### -Value
Metadata value.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 1
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

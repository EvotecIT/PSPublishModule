---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-BenchmarkIntInput
## SYNOPSIS
Gets a caller-supplied benchmark input variable as one or more integers.

## SYNTAX
### __AllParameterSets
```powershell
Get-BenchmarkIntInput [-Name] <string> [[-Default] <int[]>] [-Required] [<CommonParameters>]
```

## DESCRIPTION
Gets a caller-supplied benchmark input variable as one or more integers.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-BenchmarkIntInput -Name 'Name'
```


## PARAMETERS

### -Default
Default values used when the variable was not supplied.

```yaml
Type: Int32[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Benchmark variable name.

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

### -Required
Fail when the variable was not supplied or is empty.

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

- `System.Int32`

## RELATED LINKS

- None

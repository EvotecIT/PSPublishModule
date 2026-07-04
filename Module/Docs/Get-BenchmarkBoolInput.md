---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-BenchmarkBoolInput
## SYNOPSIS
Gets a caller-supplied benchmark input variable as a boolean.

## SYNTAX
### __AllParameterSets
```powershell
Get-BenchmarkBoolInput [-Name] <string> [[-Default] <bool>] [-Required] [<CommonParameters>]
```

## DESCRIPTION
Gets a caller-supplied benchmark input variable as a boolean.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-BenchmarkBoolInput -Name 'Name'
```


## PARAMETERS

### -Default
Default value used when the variable was not supplied.

```yaml
Type: Boolean
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

- `System.Boolean`

## RELATED LINKS

- None

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-BenchmarkInput
## SYNOPSIS
Gets a caller-supplied benchmark input variable.

## SYNTAX
### Text (Default)
```powershell
Get-BenchmarkInput [-Name] <string> [[-Default] <Object>] [-Required] [<CommonParameters>]
```

### Int
```powershell
Get-BenchmarkInput [-Name] <string> [[-Default] <Object>] -Int [-Required] [<CommonParameters>]
```

### Bool
```powershell
Get-BenchmarkInput [-Name] <string> [[-Default] <Object>] -Bool [-Required] [<CommonParameters>]
```

## DESCRIPTION
Gets a caller-supplied benchmark input variable.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-BenchmarkInput -Name 'Name'
```


### EXAMPLE 2
```powershell
Get-BenchmarkInput -Bool
```


### EXAMPLE 3
```powershell
Get-BenchmarkInput -Int
```


## PARAMETERS

### -Bool
Return the benchmark variable as a boolean.

```yaml
Type: SwitchParameter
Parameter Sets: Bool
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Default
Default value used when the variable was not supplied.

```yaml
Type: Object
Parameter Sets: Text, Int, Bool
Aliases: None
Possible values:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Int
Return the benchmark variable as one or more integers.

```yaml
Type: SwitchParameter
Parameter Sets: Int
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Benchmark variable name.

```yaml
Type: String
Parameter Sets: Text, Int, Bool
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
Parameter Sets: Text, Int, Bool
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

- `System.String
System.Int32
System.Boolean`

## RELATED LINKS

- None

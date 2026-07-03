---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-BenchmarkPolicy
## SYNOPSIS
Sets benchmark run policy defaults.

## SYNTAX
### __AllParameterSets
```powershell
Set-BenchmarkPolicy [-Warmup <int>] [-Iteration <int>] [-RunMode <string>] [-Order <PowerShellBenchmarkRunOrder>] [-CooldownMilliseconds <int>] [-OutlierMode <PowerShellBenchmarkOutlierMode>] [<CommonParameters>]
```

## DESCRIPTION
Sets benchmark run policy defaults.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-BenchmarkPolicy -CooldownMilliseconds 'Value'
```


## PARAMETERS

### -CooldownMilliseconds
Delay between measured samples, in milliseconds.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Iteration
Measured iteration count.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: Iterations
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Order
Work-item ordering strategy.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutlierMode
Summary outlier policy.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RunMode
Run mode label.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Warmup
Warmup iteration count.

```yaml
Type: Nullable`1
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

- `System.Object`

## RELATED LINKS

- None

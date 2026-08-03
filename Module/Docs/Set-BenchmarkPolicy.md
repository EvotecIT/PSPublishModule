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
Set-BenchmarkPolicy [-Warmup <Int32>] [-Iteration <Int32>] [-RunMode <string>] [-Order <PowerShellBenchmarkRunOrder>] [-MemoryCleanup <PowerShellBenchmarkMemoryCleanupMode>] [-CooldownMilliseconds <Int32>] [-OutlierMode <PowerShellBenchmarkOutlierMode>] [<CommonParameters>]
```

## DESCRIPTION
Sets benchmark run policy defaults.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-BenchmarkPolicy -CooldownMilliseconds 1
```


## PARAMETERS

### -CooldownMilliseconds
Delay between measured samples, in milliseconds.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Iteration
Measured iteration count.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: Iterations
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MemoryCleanup
Managed-memory cleanup performed outside timed operations.

```yaml
Type: PowerShellBenchmarkMemoryCleanupMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: None, BeforeIteration

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Order
Work-item ordering strategy.

```yaml
Type: PowerShellBenchmarkRunOrder
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Sequential, Rotated, Randomized, GroupedRotated

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutlierMode
Summary outlier policy.

```yaml
Type: PowerShellBenchmarkOutlierMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: None, ExcludeMinMax

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -Warmup
Warmup iteration count.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `None`

## RELATED LINKS

- None

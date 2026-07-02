---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-BenchmarkGate
## SYNOPSIS
Tests normalized benchmark summaries against a JSON baseline.

## SYNTAX
### __AllParameterSets
```powershell
Test-BenchmarkGate -SummaryPath <string> -BaselinePath <string> [-Metric <string>] [-GroupBy <string[]>] [-Update] [-AllowNew] [-RelativeTolerance <double>] [-AbsoluteToleranceMs <double>] [-MetricDirection <BenchmarkMetricDirection>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Tests normalized benchmark summaries against a JSON baseline.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-BenchmarkGate -SummaryPath .\Build\Benchmarks\summary.json -BaselinePath .\Build\Benchmarks\baseline.json -Metric MedianMs
```


## PARAMETERS

### -AbsoluteToleranceMs
Absolute tolerance in milliseconds.

```yaml
Type: Double
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AllowNew
Allows new metrics missing from baseline.

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

### -BaselinePath
Baseline JSON path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GroupBy
Fields used to construct stable metric keys.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Metric
Metric name to evaluate.

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

### -MetricDirection
Metric direction for regression checks.

```yaml
Type: BenchmarkMetricDirection
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, LowerIsBetter, HigherIsBetter

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RelativeTolerance
Relative tolerance.

```yaml
Type: Double
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SummaryPath
Summary JSON path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Update
Updates the baseline instead of verifying it.

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

- `PowerForge.BenchmarkGateResult`

## RELATED LINKS

- None

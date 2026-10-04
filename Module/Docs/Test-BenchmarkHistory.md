---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-BenchmarkHistory
## SYNOPSIS
Checks duration medians against accepted history from the same workload and runner environment.

## SYNTAX
### __AllParameterSets
```powershell
Test-BenchmarkHistory -ResultPath <string> -HistoryPath <string> -WorkloadId <string> -RunnerIdentity <string> [-Update] [-AllowCalibration] [-MinimumRuns <int>] [-MinimumSamples <int>] [-WindowSize <int>] [-RelativeTolerance <double>] [-AbsoluteToleranceMs <double>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Checks duration medians against accepted history from the same workload and runner environment.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-BenchmarkHistory -ResultPath ./run-report.json -HistoryPath ./history.json -WorkloadId topology-v1 -RunnerIdentity windows-renderer
```


## PARAMETERS

### -AbsoluteToleranceMs
Minimum absolute duration allowance in milliseconds.

```yaml
Type: Double
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -AllowCalibration
Return an uncalibrated report without a terminating error; regressions still fail.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -HistoryPath
Local accepted timing history JSON path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MinimumRuns
Minimum independent accepted runs required per lane.

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

### -MinimumSamples
Minimum independent measured iterations required per lane.

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

### -RelativeTolerance
Minimum relative duration allowance in addition to measured noise.

```yaml
Type: Double
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ResultPath
Normalized run report including raw measured samples and environment metadata.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RunnerIdentity
Stable runner pool or dedicated machine identity.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Update
Explicitly accept this run for calibration instead of verifying it.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WindowSize
Maximum recent accepted comparable runs used.

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

### -WorkloadId
Stable workload version or fixture hash.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
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

- `PowerForge.BenchmarkHistoryResult`

## RELATED LINKS

- None

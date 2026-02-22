---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetBenchmarkGate
## SYNOPSIS
Creates a benchmark gate definition for DotNet publish DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetBenchmarkGate -Id <string> -SourcePath <string> -BaselinePath <string> [-Enabled <bool>] [-BaselineMode <DotNetPublishBaselineMode>] [-FailOnNew <bool>] [-RelativeTolerance <double>] [-AbsoluteToleranceMs <double>] [-OnRegression <DotNetPublishPolicyMode>] [-OnMissingMetric <DotNetPublishPolicyMode>] [-Metrics <DotNetPublishBenchmarkMetric[]>] [<CommonParameters>]
```

## DESCRIPTION
Creates a benchmark gate definition for DotNet publish DSL.

## EXAMPLES

### EXAMPLE 1
```powershell
$m = New-ConfigurationDotNetBenchmarkMetric -Name 'storage.ms' -Source JsonPath -Path 'storage.ms'
New-ConfigurationDotNetBenchmarkGate -Id 'storage' -SourcePath 'Artifacts\bench.json' -BaselinePath 'Build\Baselines\storage.json' -Metrics $m
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

### -BaselineMode
Baseline operation mode.

```yaml
Type: DotNetPublishBaselineMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Verify, Update

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BaselinePath
Baseline file path.

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

### -Enabled
Enables this gate.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FailOnNew
Fail when new metrics appear.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Id
Gate identifier.

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

### -Metrics
Metric extraction rules.

```yaml
Type: DotNetPublishBenchmarkMetric[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OnMissingMetric
Policy on missing metrics.

```yaml
Type: DotNetPublishPolicyMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Fail, Skip

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OnRegression
Policy on regression.

```yaml
Type: DotNetPublishPolicyMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Warn, Fail, Skip

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

### -SourcePath
Source benchmark file path.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishBenchmarkGate`

## RELATED LINKS

- None


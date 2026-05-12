---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetBenchmarkMetric
## SYNOPSIS
Creates a benchmark metric extraction rule for DotNet publish gates.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetBenchmarkMetric -Name <string> [-Source <DotNetPublishBenchmarkMetricSource>] [-Path <string>] [-Pattern <string>] [-Group <int>] [-Aggregation <DotNetPublishBenchmarkMetricAggregation>] [-Required <bool>] [<CommonParameters>]
```

## DESCRIPTION
Creates a benchmark metric extraction rule for DotNet publish gates.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetBenchmarkMetric -Name 'dashboard.storage.ms' -Source JsonPath -Path 'results.storageMs'
```


## PARAMETERS

### -Aggregation
Aggregation method.

```yaml
Type: DotNetPublishBenchmarkMetricAggregation
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: First, Last, Min, Max, Average

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Group
Regex capture group index.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Metric identifier.

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

### -Path
JSON path when using JsonPath source.

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

### -Pattern
Regex pattern when using Regex source.

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

### -Required
Marks metric as required.

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

### -Source
Metric source type.

```yaml
Type: DotNetPublishBenchmarkMetricSource
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: JsonPath, Regex

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

- `PowerForge.DotNetPublishBenchmarkMetric`

## RELATED LINKS

- None


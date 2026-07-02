---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Update-BenchmarkDocument
## SYNOPSIS
Updates a marker-delimited benchmark block in a Markdown document.

## SYNTAX
### __AllParameterSets
```powershell
Update-BenchmarkDocument [-Path] <string> -BlockId <string> [-SummaryPath <string>] [-ComparisonPath <string>] [-Renderer <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Updates a marker-delimited benchmark block in a Markdown document.

## EXAMPLES

### EXAMPLE 1
```powershell
Update-BenchmarkDocument -Path .\README.MD -BlockId managed-module-benchmark-table -SummaryPath .\Build\Benchmarks\summary.json
```


## PARAMETERS

### -BlockId
Marker block identifier.

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

### -ComparisonPath
Optional comparison JSON path.

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

### -Path
Markdown document path.

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

### -Renderer
Renderer name. Use SummaryTable or ComparisonTable.

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

### -SummaryPath
Summary JSON path.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.BenchmarkDocumentUpdateResult`

## RELATED LINKS

- None

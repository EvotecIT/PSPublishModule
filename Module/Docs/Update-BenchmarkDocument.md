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
### Summary (Default)
```powershell
Update-BenchmarkDocument [-Path] <string> -BlockId <string> [-SummaryPath <string>] [-ComparisonPath <string>] [-Renderer <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Catalog
```powershell
Update-BenchmarkDocument [-Path] <string> -BlockId <string> -Catalog <string> -Comparison <string[]> [-Platform <string>] [-Framework <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Updates a marker-delimited benchmark block in a Markdown document.

## EXAMPLES

### EXAMPLE 1
```powershell
Update-BenchmarkDocument -Path .\README.MD -BlockId managed-module-benchmark-table -SummaryPath .\Build\Benchmarks\summary.json
```


### EXAMPLE 2
```powershell
Update-BenchmarkDocument -Path .\README.MD -BlockId excel-read -Catalog .\Website\static\data\benchmarks\index.json -Comparison read -Platform windows -Framework net10.0
```


## PARAMETERS

### -BlockId
Marker block identifier.

```yaml
Type: String
Parameter Sets: Summary, Catalog
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Catalog
Schema 5 evidence catalog (index.json). The block shows the published runs of the selected comparisons.

```yaml
Type: String
Parameter Sets: Catalog
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Comparison
Comparison identifiers or group names to show.

```yaml
Type: String[]
Parameter Sets: Catalog
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ComparisonPath
Optional comparison JSON path.

```yaml
Type: String
Parameter Sets: Summary
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Framework
Target framework of the run to show, such as net10.0. A published full run is shown before a standard one,
then the newest; when omitted, runs of every framework on the platform are considered.

```yaml
Type: String
Parameter Sets: Catalog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Markdown document path.

```yaml
Type: String
Parameter Sets: Summary, Catalog
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Platform
Platform whose published run is shown: windows, linux or macos.

```yaml
Type: String
Parameter Sets: Catalog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Renderer
Renderer name. Use SummaryTable or ComparisonTable.

```yaml
Type: String
Parameter Sets: Summary
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SummaryPath
Summary JSON path.

```yaml
Type: String
Parameter Sets: Summary
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

- `PowerForge.BenchmarkDocumentUpdateResult`

## RELATED LINKS

- None

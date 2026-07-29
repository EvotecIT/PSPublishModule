---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Update-BenchmarkEvidenceCatalog
## SYNOPSIS
Adds one normalized benchmark result to a platform-aware evidence catalog.

## SYNTAX
### __AllParameterSets
```powershell
Update-BenchmarkEvidenceCatalog [-Path] <string> -InputObject <BenchmarkRunResult> -ComparisonId <string> -ResultPath <string> -RunMode <string> [-Publish] [-ExpectedPlatform <string[]>] [-Platform <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Adds one normalized benchmark result to a platform-aware evidence catalog.

## EXAMPLES

### EXAMPLE 1
```powershell
$result = Import-BenchmarkResult .\BenchmarkDotNet.Artifacts
$result.Metadata['gitSha'] = (git rev-parse HEAD).Trim()
$result | Update-BenchmarkEvidenceCatalog -Path .\Website\data\benchmark-index.json -ComparisonId tabular-65k-v1 -ResultPath .\Website\data\windows-full.json -RunMode full -Publish
```


## PARAMETERS

### -ComparisonId
Stable identifier shared only by equivalent workloads and fixture versions.

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

### -ExpectedPlatform
Platforms expected before the public comparison is complete.

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

### -InputObject
Normalized benchmark result, usually supplied by Import-BenchmarkResult.

```yaml
Type: BenchmarkRunResult
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### -Path
Evidence catalog JSON path.

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

### -Platform
Producing operating-system platform for artifacts, such as BenchmarkDotNet CSV, that do not
carry OS metadata. Conflicting embedded labels are rejected.

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

### -Publish
Marks this lane as suitable for public benchmark claims. Published evidence must contain
successful measurements without failures and exact source provenance in metadata key gitSha.

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

### -ResultPath
Portable path or URL to the normalized result consumed by the website.

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

### -RunMode
Run mode such as quick or full.

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

- `PowerForge.BenchmarkRunResult`

## OUTPUTS

- `PowerForge.BenchmarkEvidenceCatalog`

## RELATED LINKS

- None

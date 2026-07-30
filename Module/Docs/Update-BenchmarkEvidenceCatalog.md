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
Update-BenchmarkEvidenceCatalog [-Path] <string> -InputObject <BenchmarkRunResult> -ComparisonId <string> -ResultPath <string> -RunMode <string> [-ResultArtifactPath <string>] [-Publish] [-ExpectedPlatform <string[]>] [-Platform <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Adds one normalized benchmark result to a platform-aware evidence catalog.

## EXAMPLES

### EXAMPLE 1
```powershell
$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts -Metadata @{ 'benchmark.workload.id' = 'tabular-65k-v1' } -RunMode full
dotnet run -c Release --project .\Benchmarks -- --artifacts .\Build\BenchmarkDotNet.Artifacts
$capture | Complete-BenchmarkProvenanceCapture
$result = Import-BenchmarkResult .\Build\BenchmarkDotNet.Artifacts
$result | Update-BenchmarkEvidenceCatalog -Path .\Website\data\benchmark-index.json -ComparisonId tabular-65k-v1 -ResultPath windows-full.json -RunMode full -Publish
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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -Publish
Marks this lane as suitable for public benchmark claims. Published evidence must contain
successful measurements without failures, runtime and runner identity, and exact clean-worktree
source provenance in metadata keys gitSha and gitWorktreeClean.

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

### -ResultArtifactPath
Optional local filesystem destination for the normalized result artifact.
Specify this when ResultPath is a website URL or another
portable consumer path rather than a local path.

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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `PowerForge.BenchmarkRunResult`

## OUTPUTS

- `PowerForge.BenchmarkEvidenceCatalog`

## RELATED LINKS

- None

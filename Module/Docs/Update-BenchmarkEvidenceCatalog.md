---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Update-BenchmarkEvidenceCatalog
## SYNOPSIS
Adds one normalized benchmark result to a schema 5 evidence catalog.

## SYNTAX
### __AllParameterSets
```powershell
Update-BenchmarkEvidenceCatalog [-Path] <string> -InputObject <BenchmarkRunResult> -ComparisonId <string> -ResultPath <string> -Profile <string> [-ResultArtifactPath <string>] [-Framework <string>] [-LaneParameter <hashtable>] [-DeclarationPath <string>] [-Policy <BenchmarkPublishPolicy>] [-Publish] [-ExpectedPlatform <string[]>] [-Platform <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Each lane is keyed by comparison, platform, framework, profile and lane parameters such as processor
affinity. The replaced lane is kept in the catalog's superseded history with its date and hash.

Publishing applies the catalog's publish policy: the diagnostic profile is never published; standard and
full lanes need clean source provenance, the runner block, at least the policy's warmups, a comparison
declaration with a library policy, and per-cell sample counts, confidence intervals and passed output
validation. Failed cells are recorded with their reason and left out of public claims instead of failing
the whole lane. Call it once per comparison as results arrive so a later failure never loses earlier evidence.

## EXAMPLES

### EXAMPLE 1
```powershell
$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts -Metadata @{ 'benchmark.workload.id' = 'tabular-65k' } -RunMode full
dotnet run -c Release --project .\Benchmarks -- --artifacts .\Build\BenchmarkDotNet.Artifacts
$capture | Complete-BenchmarkProvenanceCapture
$result = Import-BenchmarkResult .\Build\BenchmarkDotNet.Artifacts -ValidationPath .\Build\validation.json
$result | Update-BenchmarkEvidenceCatalog -Path .\Website\data\benchmark-index.json -ComparisonId tabular-65k -ResultPath windows-full.json -Profile full -Framework net10.0 -DeclarationPath .\Benchmarks\benchmarks.json -Publish
```


## PARAMETERS

### -ComparisonId
Stable identifier shared only by equivalent workloads. It must not encode framework or affinity.

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

### -DeclarationPath
Schema 5 declaration document (library policy and comparisons) that replaces the catalog's declarations.

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

### -Framework
Target framework of the lane, such as net10.0. Resolved from metadata or the runtime when omitted.

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

### -LaneParameter
Additional lane parameters, such as @{ affinity = '0xffff0000' }.

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
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

### -Policy
Publish policy that replaces the catalog's policy. Defaults to the catalog's current policy.

```yaml
Type: BenchmarkPublishPolicy
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Profile
Evidence profile: diagnostic (never published), standard or full (complete BenchmarkDotNet).
The older run-mode name quick is accepted as diagnostic.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: RunMode
Possible values: diagnostic, standard, full, quick

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Publish
Marks this lane as suitable for public benchmark claims. The lane must meet the publish policy.

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
Local filesystem destination for the normalized result artifact. Required when ResultPath
is root-relative (such as /data/benchmarks/x.json), absolute or a URL; the update fails without it rather
than writing to a drive root. The result is always written, as the lane's current run or, when a newer run
already holds the lane, as a superseded run. The file name gets the result's SHA-256 so no run is overwritten.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `PowerForge.BenchmarkRunResult`

## OUTPUTS

- `PowerForge.BenchmarkEvidenceCatalog`

## RELATED LINKS

- None

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Merge-BenchmarkEvidenceCatalog
## SYNOPSIS
Consolidates independently produced platform benchmark evidence bundles into one schema 5 catalog.

## SYNTAX
### __AllParameterSets
```powershell
Merge-BenchmarkEvidenceCatalog [-SourcePath] <string[]> [-Path] <string> [-ExpectedPlatform <string[]>] [-DeclarationPath <string>] [-Policy <BenchmarkPublishPolicy>] [-ResultPathPrefix <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Every lane is verified against its result hash and re-evaluated against the declarations and publish policy.
Older runs of the same lane are kept in the superseded history. Catalogs from earlier schemas are rejected.

## EXAMPLES

### EXAMPLE 1
```powershell
Merge-BenchmarkEvidenceCatalog -SourcePath .\windows\index.json, .\linux\index.json -Path .\Website\data\index.json -DeclarationPath .\Benchmarks\benchmarks.json
```


## PARAMETERS

### -DeclarationPath
Schema 5 declaration document: the library policy and every comparison, including workloads without results.
When omitted, declarations are merged from the source catalogs.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ComparisonPath
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

### -Path
Destination catalog path. Normalized results are published beside it under immutable content-addressed names.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Policy
Publish policy every lane is re-evaluated against. Defaults to the standard policy.

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

### -ResultPathPrefix
Root-relative public URL directory for result links, such as /data/benchmarks/library-comparisons.

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

### -SourcePath
Source bundle catalog paths. Each normalized result must be beside its catalog.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.BenchmarkEvidenceCatalog`

## RELATED LINKS

- None

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Merge-BenchmarkEvidenceCatalog
## SYNOPSIS
Consolidates independently produced platform benchmark evidence bundles.

## SYNTAX
### __AllParameterSets
```powershell
Merge-BenchmarkEvidenceCatalog [-SourcePath] <string[]> [-Path] <string> [-ExpectedPlatform <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Consolidates independently produced platform benchmark evidence bundles.

## EXAMPLES

### EXAMPLE 1
```powershell
Merge-BenchmarkEvidenceCatalog -SourcePath .\windows\index.json, .\linux\index.json -Path .\Website\data\index.json
```


## PARAMETERS

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
Accept wildcard characters: True
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
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.BenchmarkEvidenceCatalog`

## RELATED LINKS

- None

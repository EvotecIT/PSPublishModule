---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Import-BenchmarkResult
## SYNOPSIS
Imports BenchmarkDotNet or normalized benchmark artifacts into the common benchmark schema.

## SYNTAX
### __AllParameterSets
```powershell
Import-BenchmarkResult [-Path] <string> [-Suite <string>] [-OutputPath <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Imports BenchmarkDotNet or normalized benchmark artifacts into the common benchmark schema.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -OutputPath .\Build\Benchmarks\normalized.json
```


## PARAMETERS

### -OutputPath
Optional output path for normalized JSON.

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
Input file or directory.

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

### -Suite
Optional suite name override.

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

- `PowerForge.BenchmarkRunResult`

## RELATED LINKS

- None

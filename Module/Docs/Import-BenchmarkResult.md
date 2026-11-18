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
Import-BenchmarkResult [-Path] <string> [-Suite <string>] [-Culture <string>] [-LibraryMap <hashtable>] [-ValidationPath <string>] [-AcceptLicense <string[]>] [-OutputPath <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Imports BenchmarkDotNet or normalized benchmark artifacts into the common benchmark schema.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -OutputPath .\Build\Benchmarks\normalized.json
```


### EXAMPLE 2
```powershell
Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -LibraryMap @{ ReadOfficeImo = 'OfficeIMO'; ReadClosedXml = 'ClosedXML' } -ValidationPath .\Build\validation.json
```


## PARAMETERS

### -AcceptLicense
Library ids whose licence acceptance you gave explicitly for this run. Required before
acceptance-required libraries can appear in evidence; it is never assumed.

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

### -Culture
Optional numeric culture used by the producing CSV report, for example de-DE.
Supply it when punctuation such as 1,234 would otherwise be ambiguous.

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

### -LibraryMap
Map from benchmark method (case) name to library id. Every row must resolve to a library through this map
or a [BenchmarkCategory("library:<id>")] category; library identity is never guessed from names.

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
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -ValidationPath
JSON array of output validations recorded before timing: objects with library, case (optional),
status (passed or failed) and detail.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.BenchmarkRunResult`

## RELATED LINKS

- None

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-BenchmarkDeclaration
## SYNOPSIS
Validates a benchmark declaration document and, optionally, checks a result against it for drift.

## SYNTAX
### __AllParameterSets
```powershell
Test-BenchmarkDeclaration [-Path] <string> [-InputObject <BenchmarkRunResult>] [-ComparisonId <string>] [-PassThru] [<CommonParameters>]
```

## DESCRIPTION
The declaration document holds the library policy (id, package, pinned version, licence, restriction class,
reason) and the comparisons (area, group, title, subject, libraries, axes, cases, platforms, frameworks).
Without a result the command lints the document. With a result it also reports declaration drift: cases,
libraries or axis values that ran but are not declared, and declared cases that did not run.

The command stops with an error when it finds anything, so a build can run it before measuring.
Use -PassThru to receive the findings as objects instead.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-BenchmarkDeclaration -Path .\Benchmarks\benchmarks.json
```


### EXAMPLE 2
```powershell
Import-BenchmarkResult .\artifacts | Test-BenchmarkDeclaration -Path .\Benchmarks\benchmarks.json -ComparisonId pdf-merge
```


## PARAMETERS

### -ComparisonId
Comparison the result belongs to. Required with a result.

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
Optional normalized result to check for drift.

```yaml
Type: BenchmarkRunResult
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -PassThru
Return findings as objects instead of stopping with an error.

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

### -Path
Schema 5 declaration document path.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `PowerForge.BenchmarkRunResult`

## OUTPUTS

- `PowerForge.BenchmarkDeclarationIssue`

## RELATED LINKS

- None

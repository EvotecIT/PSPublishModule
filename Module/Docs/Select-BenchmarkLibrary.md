---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Select-BenchmarkLibrary
## SYNOPSIS
Selects the libraries a benchmark run may execute from the library policy.

## SYNTAX
### __AllParameterSets
```powershell
Select-BenchmarkLibrary [-Path] <string> [-Include <string[]>] [-Exclude <string[]>] [-AcceptLicense <string[]>] [<CommonParameters>]
```

## DESCRIPTION
Selection is generic: include and exclude lists plus explicit per-library licence acceptance. Excluded
libraries never run; acceptance-required libraries run only when you name them in -AcceptLicense.
Pass Accepted to Import-BenchmarkResult -AcceptLicense so the
evidence records the acceptance.

## EXAMPLES

### EXAMPLE 1
```powershell
$selection = Select-BenchmarkLibrary -Path .\Benchmarks\benchmarks.json -AcceptLicense EPPlus
dotnet run -c Release --project .\Benchmarks -- --libraries ($selection.Selected -join ',')
```


## PARAMETERS

### -AcceptLicense
Library ids whose licence you accept for this run.

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

### -Exclude
Library ids not to run.

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

### -Include
Library ids to run; omit to run every selectable library.

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

- `None`

## OUTPUTS

- `PowerForge.BenchmarkLibrarySelection`

## RELATED LINKS

- None

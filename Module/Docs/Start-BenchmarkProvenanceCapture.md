---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Start-BenchmarkProvenanceCapture
## SYNOPSIS
Captures clean source state before an external benchmark writes into a fresh artifact directory.

## SYNTAX
### __AllParameterSets
```powershell
Start-BenchmarkProvenanceCapture -SourceRoot <string> -ArtifactRoot <string> [-Metadata <hashtable>] [-RunMode <string>] [<CommonParameters>]
```

## DESCRIPTION
Captures clean source state before an external benchmark writes into a fresh artifact directory.

## EXAMPLES

### EXAMPLE 1
```powershell
$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts -Metadata @{ 'benchmark.workload.id' = 'tabular-65k-v1' } -RunMode full
```


## PARAMETERS

### -ArtifactRoot
Fresh directory where the external benchmark will write its artifacts.

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

### -Metadata
Optional workload metadata to bind before measurement. Publishable evidence requires benchmark.workload.id.

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RunMode
Optional for diagnostic captures; publishable evidence requires a run mode bound before measurement.

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

### -SourceRoot
Git repository root whose source is being measured.

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

- `None`

## OUTPUTS

- `PowerForge.BenchmarkProvenanceCaptureSession`

## RELATED LINKS

- None

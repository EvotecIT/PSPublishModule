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
Start-BenchmarkProvenanceCapture -SourceRoot <string> -ArtifactRoot <string> [<CommonParameters>]
```

## DESCRIPTION
Captures clean source state before an external benchmark writes into a fresh artifact directory.

## EXAMPLES

### EXAMPLE 1
```powershell
$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts
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

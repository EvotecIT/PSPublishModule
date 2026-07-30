---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Complete-BenchmarkProvenanceCapture
## SYNOPSIS
Verifies unchanged source after an external benchmark and writes a hash-bound artifact sidecar.

## SYNTAX
### __AllParameterSets
```powershell
Complete-BenchmarkProvenanceCapture -InputObject <BenchmarkProvenanceCaptureSession> [<CommonParameters>]
```

## DESCRIPTION
Verifies unchanged source after an external benchmark and writes a hash-bound artifact sidecar.

## EXAMPLES

### EXAMPLE 1
```powershell
$capture | Complete-BenchmarkProvenanceCapture
```


## PARAMETERS

### -InputObject
Capture session returned before measurement.

```yaml
Type: BenchmarkProvenanceCaptureSession
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `PowerForge.BenchmarkProvenanceCaptureSession`

## OUTPUTS

- `System.String`

## RELATED LINKS

- None

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-BenchmarkCleanup
## SYNOPSIS
Sets the benchmark cleanup mode.

## SYNTAX
### __AllParameterSets
```powershell
Set-BenchmarkCleanup [-Name] <PowerShellBenchmarkCleanupMode> [<CommonParameters>]
```

## DESCRIPTION
Sets the benchmark cleanup mode.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-BenchmarkCleanup -Name 'Value'
```


## PARAMETERS

### -Name
Cleanup mode.

```yaml
Type: PowerShellBenchmarkCleanupMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Always, KeepOnFailure, KeepAlways

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

- `System.Object`

## RELATED LINKS

- None

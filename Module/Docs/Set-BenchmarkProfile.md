---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-BenchmarkProfile
## SYNOPSIS
Sets the benchmark profile mode.

## SYNTAX
### __AllParameterSets
```powershell
Set-BenchmarkProfile [-Name] <PowerShellBenchmarkProfileKind> [-Cleanup <PowerShellBenchmarkCleanupMode>] [<CommonParameters>]
```

## DESCRIPTION
Sets the benchmark profile mode.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-BenchmarkProfile -Name 'Value'
```


## PARAMETERS

### -Cleanup
Cleanup mode used by profile-owned temporary state.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Profile kind.

```yaml
Type: PowerShellBenchmarkProfileKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Current, TemporaryLocalUser

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

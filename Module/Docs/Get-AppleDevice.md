---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-AppleDevice
## SYNOPSIS
Lists Apple devices available through xcrun devicectl.

## SYNTAX
### __AllParameterSets
```powershell
Get-AppleDevice [[-Device] <string>] [-Xcrun <string>] [-IncludeUnavailable] [-TimeoutMinutes <int>] [<CommonParameters>]
```

## DESCRIPTION
Lists Apple devices available through xcrun devicectl.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-AppleDevice -Device 'Value'
```


## PARAMETERS

### -Device
Optional device name, identifier, or model filter.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeUnavailable
Include unavailable devices.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TimeoutMinutes
Maximum command runtime in minutes.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Xcrun
xcrun executable name or path.

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

- `PowerForge.AppleDeviceInfo`

## RELATED LINKS

- None

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-AppleApp
## SYNOPSIS
Installs a built Apple .app bundle on a physical device.

## SYNTAX
### __AllParameterSets
```powershell
Install-AppleApp [-AppPath] <string> [-DeviceIdentifier <string>] [-Device <string>] [-Xcrun <string>] [-TimeoutMinutes <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Installs a built Apple .app bundle on a physical device.

## EXAMPLES

### EXAMPLE 1
```powershell
Install-AppleApp -AppPath 'C:\Path'
```


## PARAMETERS

### -AppPath
Path to the built .app bundle.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path, FullName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Device
Device name, identifier, or model used when DeviceIdentifier is not supplied.

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

### -DeviceIdentifier
Physical device identifier.

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

### -TimeoutMinutes
Maximum install runtime in minutes.

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

- `PowerForge.AppleAppInstallResult`

## RELATED LINKS

- None

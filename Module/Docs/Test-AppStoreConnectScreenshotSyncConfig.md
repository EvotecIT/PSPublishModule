---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-AppStoreConnectScreenshotSyncConfig
## SYNOPSIS
Validates an App Store Connect screenshot sync configuration against local files.

## SYNTAX
### __AllParameterSets
```powershell
Test-AppStoreConnectScreenshotSyncConfig [-ConfigPath] <string> [-PassThru] [-Quiet] [<CommonParameters>]
```

## DESCRIPTION
Validates an App Store Connect screenshot sync configuration against local files.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-AppStoreConnectScreenshotSyncConfig -ConfigPath 'C:\Path'
```


## PARAMETERS

### -ConfigPath
Path to the screenshot sync JSON configuration file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: FullName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### -PassThru
Return the full validation result instead of a Boolean.

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

### -Quiet
Suppress validation warnings.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String`

## OUTPUTS

- `System.Boolean
PowerForge.AppStoreConnectScreenshotSyncValidationResult`

## RELATED LINKS

- None

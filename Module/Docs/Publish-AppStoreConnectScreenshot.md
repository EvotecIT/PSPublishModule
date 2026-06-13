---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Publish-AppStoreConnectScreenshot
## SYNOPSIS
Uploads and commits an App Store Connect screenshot file to an existing screenshot set.

## SYNTAX
### __AllParameterSets
```powershell
Publish-AppStoreConnectScreenshot [-Path] <string> -IssuerId <string> -KeyId <string> -ScreenshotSetId <string> [-PrivateKey <string>] [-PrivateKeyPath <string>] [-TokenLifetimeMinutes <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Uploads and commits an App Store Connect screenshot file to an existing screenshot set.

## EXAMPLES

### EXAMPLE 1
```powershell
Publish-AppStoreConnectScreenshot -IssuerId 'Value' -KeyId 'Value' -ScreenshotSetId 'Value'
```


## PARAMETERS

### -IssuerId
Issuer ID from App Store Connect API keys.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -KeyId
Key ID associated with the private key.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Screenshot file path.

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

### -PrivateKey
Private key text in PEM format.

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

### -PrivateKeyPath
Path to a private key file in PEM format.

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

### -ScreenshotSetId
Existing App Store Connect screenshot set id.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TokenLifetimeMinutes
Token lifetime in minutes, up to 20.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String`

## OUTPUTS

- `PowerForge.AppStoreConnectScreenshotUploadResult`

## RELATED LINKS

- None

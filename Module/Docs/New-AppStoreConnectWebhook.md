---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-AppStoreConnectWebhook
## SYNOPSIS
Creates an App Store Connect webhook.

## SYNTAX
### __AllParameterSets
```powershell
New-AppStoreConnectWebhook -IssuerId <string> -KeyId <string> -AppId <string> -Name <string> -Url <string> -Secret <string> -EventType <string[]> [-PrivateKey <string>] [-PrivateKeyPath <string>] [-TokenLifetimeMinutes <int>] [-Enabled <bool>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Creates an App Store Connect webhook.

## EXAMPLES

### EXAMPLE 1
```powershell
New-AppStoreConnectWebhook -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath './AuthKey.p8' -AppId '6778025328' -Name 'Build status' -Url 'https://releases.example.com/apple/webhook' -Secret 'a-strong-webhook-secret' -EventType 'BUILD_UPLOAD_STATE_UPDATED'
```


## PARAMETERS

### -AppId
App Store Connect app id.

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

### -Enabled
Whether Apple should deliver events.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EventType
App Store Connect event types.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IssuerId
App Store Connect API issuer id.

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
App Store Connect API key id.

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

### -Name
Webhook display name.

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
Private key file path.

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

### -Secret
HMAC signing secret. The cmdlet never writes it to output.

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
JWT token lifetime in minutes.

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

### -Url
Public HTTPS receiver URL.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.AppStoreConnectWebhookInfo`

## RELATED LINKS

- None

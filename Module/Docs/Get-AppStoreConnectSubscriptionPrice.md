---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-AppStoreConnectSubscriptionPrice
## SYNOPSIS
Reads App Store Connect prices configured for an auto-renewable subscription.

## SYNTAX
### __AllParameterSets
```powershell
Get-AppStoreConnectSubscriptionPrice -IssuerId <string> -KeyId <string> -SubscriptionId <string> [-PrivateKey <string>] [-PrivateKeyPath <string>] [-TokenLifetimeMinutes <int>] [-Limit <int>] [<CommonParameters>]
```

## DESCRIPTION
Reads App Store Connect prices configured for an auto-renewable subscription.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-AppStoreConnectSubscriptionPrice -IssuerId 'Value' -KeyId 'Value' -SubscriptionId 'Value'
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

### -Limit
Maximum result count per App Store Connect request.

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

### -SubscriptionId
App Store Connect subscription resource id.

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

- `None`

## OUTPUTS

- `PowerForge.AppStoreConnectSubscriptionPriceInfo`

## RELATED LINKS

- None

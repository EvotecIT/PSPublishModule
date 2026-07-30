---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-AppStoreConnectSubscriptionIntroductoryOffer
## SYNOPSIS
Creates an App Store Connect introductory offer for an auto-renewable subscription.

## SYNTAX
### __AllParameterSets
```powershell
New-AppStoreConnectSubscriptionIntroductoryOffer -IssuerId <string> -KeyId <string> -SubscriptionId <string> -Duration <AppStoreConnectSubscriptionOfferDuration> -OfferMode <AppStoreConnectSubscriptionOfferMode> -TerritoryId <string[]> [-PrivateKey <string>] [-PrivateKeyPath <string>] [-TokenLifetimeMinutes <int>] [-NumberOfPeriods <int>] [-StartDate <datetime>] [-EndDate <datetime>] [-SubscriptionPricePointId <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Creates an App Store Connect introductory offer for an auto-renewable subscription.

## EXAMPLES

### EXAMPLE 1
```powershell
New-AppStoreConnectSubscriptionIntroductoryOffer -IssuerId 'Value' -KeyId 'Value' -SubscriptionId 'Value' -Duration 'Value' -OfferMode 'Value'
```


## PARAMETERS

### -Duration
Introductory-offer duration.

```yaml
Type: AppStoreConnectSubscriptionOfferDuration
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: ThreeDays, OneWeek, TwoWeeks, OneMonth, TwoMonths, ThreeMonths, SixMonths, OneYear

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EndDate
Optional offer end date.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -NumberOfPeriods
Number of offer periods.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OfferMode
Introductory-offer payment mode.

```yaml
Type: AppStoreConnectSubscriptionOfferMode
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: FreeTrial, PayAsYouGo, PayUpFront

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -StartDate
Optional offer start date.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### -SubscriptionPricePointId
Subscription price point resource id for a paid offer. Paid offers accept one territory per invocation
because App Store Connect price points are territory-specific.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TerritoryId
Territory resource ids for the offer. Free trials may target multiple territories; paid offers accept one.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
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
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.AppStoreConnectSubscriptionIntroductoryOfferInfo`

## RELATED LINKS

- None

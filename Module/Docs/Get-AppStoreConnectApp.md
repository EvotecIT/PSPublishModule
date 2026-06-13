---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-AppStoreConnectApp
## SYNOPSIS
Reads app information from App Store Connect.

## SYNTAX
### ById
```powershell
Get-AppStoreConnectApp -IssuerId <string> -KeyId <string> -AppId <string> [-PrivateKey <string>] [-PrivateKeyPath <string>] [-TokenLifetimeMinutes <int>] [<CommonParameters>]
```

### Find
```powershell
Get-AppStoreConnectApp -IssuerId <string> -KeyId <string> [-PrivateKey <string>] [-PrivateKeyPath <string>] [-TokenLifetimeMinutes <int>] [-BundleId <string>] [-Name <string>] [-Platform <ApplePlatform>] [-Limit <int>] [<CommonParameters>]
```

## DESCRIPTION
Reads app information from App Store Connect.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-AppStoreConnectApp -IssuerId 'Value' -KeyId 'Value' -AppId 'Value'
```


### EXAMPLE 2
```powershell
Get-AppStoreConnectApp -IssuerId 'Value' -KeyId 'Value'
```


## PARAMETERS

### -AppId
App Store Connect app id.

```yaml
Type: String
Parameter Sets: ById
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BundleId
Bundle identifier filter.

```yaml
Type: String
Parameter Sets: Find
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IssuerId
Issuer ID from App Store Connect API keys.

```yaml
Type: String
Parameter Sets: ById, Find
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
Parameter Sets: ById, Find
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Limit
Maximum result count for filtered searches.

```yaml
Type: Int32
Parameter Sets: Find
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Name filter.

```yaml
Type: String
Parameter Sets: Find
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Platform
Platform filter.

```yaml
Type: Nullable`1
Parameter Sets: Find
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
Parameter Sets: ById, Find
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
Parameter Sets: ById, Find
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TokenLifetimeMinutes
Token lifetime in minutes, up to 20.

```yaml
Type: Int32
Parameter Sets: ById, Find
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

- `PowerForge.AppStoreConnectAppInfo`

## RELATED LINKS

- None

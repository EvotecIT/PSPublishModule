---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Export-CertificateForNuGet

## SYNOPSIS
Exports a code signing certificate to DER format for NuGet.org registration.

## SYNTAX

### Thumbprint (Default)
```
Export-CertificateForNuGet -CertificateThumbprint <String> [-OutputPath <String>] [-LocalStore <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### Sha256
```
Export-CertificateForNuGet -CertificateSha256 <String> [-OutputPath <String>] [-LocalStore <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
This function finds a code signing certificate by thumbprint or SHA256 hash and exports it
to a .cer file in DER format, which is required for registering the certificate with NuGet.org.
After exporting, you need to manually register this .cer file on NuGet.org under your account
settings in the Certificates section.

## EXAMPLES

### EXAMPLE 1
```
Export-CertificateForNuGet -CertificateThumbprint '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703' -OutputPath 'C:\Temp\MyCodeSigningCert.cer'
```

Exports the certificate to the specified path for NuGet.org registration.

### EXAMPLE 2
```
Export-CertificateForNuGet -CertificateSha256 '769C6B450BE58DC6E15193EE3916282D73BCED16E5E2FF8ACD0850D604DD560C'
```

Exports the certificate using SHA256 hash to the current directory.

## PARAMETERS

### -CertificateThumbprint
The SHA1 thumbprint of the certificate to export.

```yaml
Type: String
Parameter Sets: Thumbprint
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificateSha256
The SHA256 hash of the certificate to export.
Use this instead of thumbprint if you have the SHA256.

```yaml
Type: String
Parameter Sets: Sha256
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputPath
The path where the .cer file will be saved.
If not specified, saves to current directory
with filename based on certificate subject.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LocalStore
Certificate store location.
Defaults to 'CurrentUser'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: CurrentUser
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
After running this function:
1.
Go to https://www.nuget.org
2.
Sign in to your account
3.
Go to Account Settings \> Certificates
4.
Click "Register new"
5.
Upload the generated .cer file
6.
Once registered, all future package uploads must be signed with this certificate

## RELATED LINKS

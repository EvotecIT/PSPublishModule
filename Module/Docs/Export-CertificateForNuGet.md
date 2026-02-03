---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Export-CertificateForNuGet
## SYNOPSIS
Exports a code-signing certificate to DER format for NuGet.org registration.

## SYNTAX
### Thumbprint (Default)
```powershell
Export-CertificateForNuGet -CertificateThumbprint <string> [-OutputPath <string>] [-LocalStore <CertificateStoreLocation>] [<CommonParameters>]
```

### Sha256
```powershell
Export-CertificateForNuGet -CertificateSha256 <string> [-OutputPath <string>] [-LocalStore <CertificateStoreLocation>] [<CommonParameters>]
```

## DESCRIPTION
NuGet.org requires uploading the public certificate (.cer) used for package signing.
This cmdlet exports the selected certificate from the local certificate store.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Export-CertificateForNuGet -CertificateThumbprint '0123456789ABCDEF' -OutputPath 'C:\Temp\NuGetSigning.cer'
```

Exports the certificate in DER format to the given path.

### EXAMPLE 2
```powershell
PS>Export-CertificateForNuGet -CertificateSha256 '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF'
```

Useful when you have the SHA256 fingerprint but not the Windows thumbprint.

## PARAMETERS

### -CertificateSha256
The SHA256 hash of the certificate to export.

```yaml
Type: String
Parameter Sets: Sha256
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CertificateThumbprint
The SHA1 thumbprint of the certificate to export.

```yaml
Type: String
Parameter Sets: Thumbprint
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LocalStore
Certificate store location to use.

```yaml
Type: CertificateStoreLocation
Parameter Sets: Thumbprint, Sha256
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputPath
Output path for the exported .cer file.

```yaml
Type: String
Parameter Sets: Thumbprint, Sha256
Aliases: None

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

- `System.Object`

## RELATED LINKS

- None


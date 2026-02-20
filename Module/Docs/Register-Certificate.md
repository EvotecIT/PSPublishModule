---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Register-Certificate
## SYNOPSIS
Signs files in a path using a code-signing certificate (Windows and PowerShell Core supported).

## SYNTAX
### Store (Default)
```powershell
Register-Certificate -LocalStore <CertificateStoreLocation> -Path <string> [-Thumbprint <string>] [-TimeStampServer <string>] [-IncludeChain <CertificateChainInclude>] [-Include <string[]>] [-ExcludePath <string[]>] [-HashAlgorithm <CertificateHashAlgorithm>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### PFX
```powershell
Register-Certificate -CertificatePFX <string> -Path <string> [-TimeStampServer <string>] [-IncludeChain <CertificateChainInclude>] [-Include <string[]>] [-ExcludePath <string[]>] [-HashAlgorithm <CertificateHashAlgorithm>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Signs PowerShell scripts/manifests (and optionally binaries) using Authenticode.
When running in CI, prefer using a certificate from the Windows certificate store and referencing it by thumbprint.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Register-Certificate -Path 'C:\Git\MyModule\Module' -LocalStore CurrentUser -Thumbprint '0123456789ABCDEF' -WhatIf
```

Previews which files would be signed.

### EXAMPLE 2
```powershell
PS>Register-Certificate -CertificatePFX 'C:\Secrets\codesign.pfx' -Path 'C:\Git\MyModule\Module' -Include '*.ps1','*.psm1','*.psd1'
```

Uses a PFX directly (useful for local testing; store-based is recommended for CI).

## PARAMETERS

### -CertificatePFX
A PFX file to use for signing (mutually exclusive with -LocalStore/-Thumbprint).

```yaml
Type: String
Parameter Sets: PFX
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludePath
One or more path substrings to exclude from signing.

```yaml
Type: String[]
Parameter Sets: Store, PFX
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -HashAlgorithm
Hash algorithm used for the signature. Default: SHA256.

```yaml
Type: CertificateHashAlgorithm
Parameter Sets: Store, PFX
Aliases: None
Possible values: SHA1, SHA256, SHA384, SHA512

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Include
File patterns to include during signing. Default: scripts only.

```yaml
Type: String[]
Parameter Sets: Store, PFX
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeChain
Which portion of the chain to include in the signature. Default: All.

```yaml
Type: CertificateChainInclude
Parameter Sets: Store, PFX
Aliases: None
Possible values: All, NotRoot, Signer

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LocalStore
Certificate store to search when using a certificate from the store.

```yaml
Type: CertificateStoreLocation
Parameter Sets: Store
Aliases: None
Possible values: CurrentUser, LocalMachine

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Root directory containing files to sign.

```yaml
Type: String
Parameter Sets: Store, PFX
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Thumbprint
Certificate thumbprint to select a single certificate from the chosen store.

```yaml
Type: String
Parameter Sets: Store
Aliases: CertificateThumbprint
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TimeStampServer
RFC3161 timestamp server URL. Default: http://timestamp.digicert.com.

```yaml
Type: String
Parameter Sets: Store, PFX
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

- `System.Object`

## RELATED LINKS

- None


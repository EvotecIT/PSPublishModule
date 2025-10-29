---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Register-Certificate

## SYNOPSIS
Signs files in a path using a code-signing certificate (Windows and PowerShell Core supported).

## SYNTAX

### PFX
```
Register-Certificate -CertificatePFX <String> -Path <String> [-TimeStampServer <String>]
 [-IncludeChain <String>] [-Include <String[]>] [-ExcludePath <String[]>] [-HashAlgorithm <String>]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Store
```
Register-Certificate -LocalStore <String> [-Thumbprint <String>] -Path <String> [-TimeStampServer <String>]
 [-IncludeChain <String>] [-Include <String[]>] [-ExcludePath <String[]>] [-HashAlgorithm <String>]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Locates a code-signing certificate (by thumbprint from the Windows cert store or from a PFX)
and applies Authenticode signatures to matching files under -Path.
On Windows, uses Set-AuthenticodeSignature; on non-Windows, uses OpenAuthenticode module if available.

## EXAMPLES

### Example 1
```powershell
PS C:\> {{ Add example code here }}
```

{{ Add example description here }}

## PARAMETERS

### -CertificatePFX
A PFX file to use for signing.
Mutually exclusive with -LocalStore/-Thumbprint.

```yaml
Type: String
Parameter Sets: PFX
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -LocalStore
Certificate store to search ('LocalMachine' or 'CurrentUser') when using a certificate from the store.

```yaml
Type: String
Parameter Sets: Store
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Thumbprint
Certificate thumbprint to select a single certificate from the chosen -LocalStore.

```yaml
Type: String
Parameter Sets: Store
Aliases: CertificateThumbprint

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Root directory containing files to sign.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TimeStampServer
RFC3161 timestamp server URL.
Default: http://timestamp.digicert.com

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Http://timestamp.digicert.com
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeChain
Which portion of the chain to include in the signature: All, NotRoot, or Signer.
Default: All.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: All
Accept pipeline input: False
Accept wildcard characters: False
```

### -Include
File patterns to include during signing.
Defaults to scripts only: '*.ps1','*.psd1','*.psm1'.
You may pass additional patterns if needed (e.g., '*.dll').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: @('*.ps1', '*.psd1', '*.psm1')
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludePath
One or more path substrings to exclude from signing.
Useful for skipping folders like 'Internals' unless opted-in.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -HashAlgorithm
Hash algorithm for the signature.
Default: SHA256.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: SHA256
Accept pipeline input: False
Accept wildcard characters: False
```

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
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

## RELATED LINKS

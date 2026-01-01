---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-DotNetReleaseBuild
## SYNOPSIS
Builds a .NET project in Release configuration and prepares release artefacts.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-DotNetReleaseBuild -ProjectPath <string[]> [-CertificateThumbprint <string>] [-LocalStore <CertificateStoreLocation>] [-TimeStampServer <string>] [-PackDependencies] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Builds a .NET project in Release configuration and prepares release artefacts.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-DotNetReleaseBuild -ProjectPath '.\MyLibrary\MyLibrary.csproj' -PackDependencies
```

### EXAMPLE 2
```powershell
Invoke-DotNetReleaseBuild -ProjectPath '.\MyLibrary\MyLibrary.csproj' -CertificateThumbprint '0123456789ABCDEF' -LocalStore CurrentUser
```

## PARAMETERS

### -CertificateThumbprint
Optional certificate thumbprint used to sign assemblies and packages. When omitted, no signing is performed.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -LocalStore
Certificate store location used when searching for the signing certificate. Default: CurrentUser.

```yaml
Type: CertificateStoreLocation
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PackDependencies
When enabled, also packs all project dependencies that have their own .csproj files.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectPath
Path to the folder containing the project (*.csproj) file (or the csproj file itself).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TimeStampServer
Timestamp server URL used while signing. Default: http://timestamp.digicert.com.

```yaml
Type: String
Parameter Sets: __AllParameterSets
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

- `PowerForge.DotNetReleaseBuildResult`

## RELATED LINKS

- None


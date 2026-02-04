---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-DotNetRepositoryRelease
## SYNOPSIS
Repository-wide .NET package release workflow (discover, version, pack, publish).

## SYNTAX
### __AllParameterSets
```powershell
Invoke-DotNetRepositoryRelease [-Path <string>] [-ExpectedVersion <string>] [-ExpectedVersionMap <IDictionary>] [-ExpectedVersionMapAsInclude] [-ExpectedVersionMapUseWildcards] [-IncludeProject <string[]>] [-ExcludeProject <string[]>] [-ExcludeDirectories <string[]>] [-NugetSource <string[]>] [-IncludePrerelease] [-NugetCredentialUserName <string>] [-NugetCredentialSecret <string>] [-NugetCredentialSecretFilePath <string>] [-NugetCredentialSecretEnvName <string>] [-Configuration <string>] [-OutputPath <string>] [-CertificateThumbprint <string>] [-CertificateStore <CertificateStoreLocation>] [-TimeStampServer <string>] [-SkipPack] [-Publish] [-PublishSource <string>] [-PublishApiKey <string>] [-PublishApiKeyFilePath <string>] [-PublishApiKeyEnvName <string>] [-SkipDuplicate] [-PublishFailFast] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Discovers packable projects, resolves a repository-wide version (supports X-pattern),
updates csproj versions, packs, and optionally publishes packages.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-DotNetRepositoryRelease -Path . -ExpectedVersion '1.2.X' -Publish -PublishApiKey $env:NUGET_API_KEY
```

### EXAMPLE 2
```powershell
Invoke-DotNetRepositoryRelease -Path . -ExpectedVersion '2.0.X' -ExcludeProject 'OfficeIMO.Visio' -NugetSource 'C:\Packages' -Publish -PublishApiKey $env:NUGET_API_KEY
```

## PARAMETERS

### -CertificateStore
Certificate store location used when searching for the signing certificate.

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

### -CertificateThumbprint
Certificate thumbprint used for signing packages.

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

### -Configuration
Build configuration (Release/Debug).

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

### -ExcludeDirectories
Directory names to exclude from discovery.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeProject
Project names to exclude (csproj file name without extension).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedVersion
Expected version (exact or X-pattern, e.g. 1.2.X).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Version

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedVersionMap
Per-project expected versions (hashtable: ProjectName = Version).

```yaml
Type: IDictionary
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedVersionMapAsInclude
When set, only projects listed in ExpectedVersionMap are processed.

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

### -ExpectedVersionMapUseWildcards
Allow wildcards (*, ?) in ExpectedVersionMap keys.

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

### -IncludePrerelease
Include prerelease versions when resolving versions.

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

### -IncludeProject
Project names to include (csproj file name without extension).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NugetCredentialSecret
Credential secret/token for private NuGet sources.

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

### -NugetCredentialSecretEnvName
Name of environment variable containing the credential secret/token.

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

### -NugetCredentialSecretFilePath
Path to a file containing the credential secret/token.

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

### -NugetCredentialUserName
Credential username for private NuGet sources.

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

### -NugetSource
NuGet sources (v3 index or local path) used for version resolution.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputPath
Optional output path for packages.

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

### -Path
Root repository path.

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

### -Publish
Publish packages to the feed.

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

### -PublishApiKey
API key used for publishing packages.

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

### -PublishApiKeyEnvName
Name of environment variable containing the publish API key.

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

### -PublishApiKeyFilePath
Path to a file containing the publish API key.

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

### -PublishFailFast
Stop on the first publish/signing failure.

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

### -PublishSource
NuGet feed source for publishing.

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

### -SkipDuplicate
Skip duplicates when pushing packages.

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

### -SkipPack
Skip dotnet pack step.

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

### -TimeStampServer
Timestamp server URL used while signing packages.

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

- `PowerForge.DotNetRepositoryReleaseResult`

## RELATED LINKS

- None


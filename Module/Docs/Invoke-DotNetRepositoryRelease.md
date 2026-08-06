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
Invoke-DotNetRepositoryRelease [-Path <string>] [-ExpectedVersion <string>] [-ExpectedVersionMap <IDictionary>] [-ExpectedVersionMapAsInclude] [-ExpectedVersionMapUseWildcards] [-AlignPackageVersions] [-IncludeProject <string[]>] [-ExcludeProject <string[]>] [-ExcludeDirectories <string[]>] [-NugetSource <string[]>] [-IncludePrerelease] [-NugetCredentialUserName <string>] [-NugetCredentialSecret <string>] [-NugetCredentialSecretFilePath <string>] [-NugetCredentialSecretEnvName <string>] [-Configuration <string>] [-OutputPath <string>] [-CertificateThumbprint <string>] [-CertificateStore <CertificateStoreLocation>] [-TimeStampServer <string>] [-SkipAssemblySigning] [-SignDependencyAssemblies] [-OverwriteSignedAssemblies] [-SkipPackageSigning] [-OverwriteSignedPackages] [-SkipPack] [-IncludeSymbols] [-Publish] [-PublishSource <string>] [-PublishApiKey <string>] [-PublishApiKeyFilePath <string>] [-PublishApiKeyEnvName <string>] [-SkipDuplicate] [-PublishFailFast] [-WhatIf] [-Confirm] [<CommonParameters>]
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

### -AlignPackageVersions
Align projects sharing an X-pattern to one next version based on the highest current package version in that group.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificateStore
Certificate store location used when searching for the signing certificate.

```yaml
Type: CertificateStoreLocation
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: CurrentUser, LocalMachine

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CertificateThumbprint
Certificate thumbprint used for signing packages.

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

### -Configuration
Build configuration (Release/Debug).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Release, Debug

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeDirectories
Directory names to exclude from discovery.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeProject
Project names to exclude (csproj file name without extension).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExpectedVersion
Expected version (exact or X-pattern, e.g. 1.2.X).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Version
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExpectedVersionMap
Per-project expected versions (hashtable: ProjectName = Version).

```yaml
Type: IDictionary
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExpectedVersionMapAsInclude
When set, only projects listed in ExpectedVersionMap are processed.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExpectedVersionMapUseWildcards
Allow wildcards (*, ?) in ExpectedVersionMap keys.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludePrerelease
Include prerelease versions when resolving versions.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeProject
Project names to include (csproj file name without extension).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeSymbols
Create portable .snupkg symbol packages alongside primary packages.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NugetCredentialSecret
Credential secret/token for private NuGet sources.

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

### -NugetCredentialSecretEnvName
Name of environment variable containing the credential secret/token.

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

### -NugetCredentialSecretFilePath
Path to a file containing the credential secret/token.

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

### -NugetCredentialUserName
Credential username for private NuGet sources.

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

### -NugetSource
NuGet sources (v3 index or local path) used for version resolution.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputPath
Optional output path for packages.

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

### -OverwriteSignedAssemblies
Explicitly replaces existing Authenticode assembly signatures.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OverwriteSignedPackages
Explicitly replaces existing NuGet package signatures.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Root repository path.

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

### -Publish
Publish packages to the feed.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PublishApiKey
API key used for publishing packages.

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

### -PublishApiKeyEnvName
Name of environment variable containing the publish API key.

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

### -PublishApiKeyFilePath
Path to a file containing the publish API key.

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

### -PublishFailFast
Stop on the first publish/signing failure.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PublishSource
NuGet feed source for publishing.

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

### -SignDependencyAssemblies
Also sign copied dependency assemblies from build output folders.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipAssemblySigning
Skip Authenticode signing of build outputs before packages are created.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipDuplicate
Skip duplicates when pushing packages.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipPack
Skip dotnet pack step.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipPackageSigning
Skip signing generated NuGet packages.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TimeStampServer
Timestamp server URL used while signing packages.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetRepositoryReleaseResult`

## RELATED LINKS

- None

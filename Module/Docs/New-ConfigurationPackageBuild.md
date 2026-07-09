---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationPackageBuild
## SYNOPSIS
Creates inline .NET/NuGet package build configuration from the module-build DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationPackageBuild [-Name <string>] [-Enabled] [-BuildBeforeModule] [-UseAsReleaseVersionSource] [-ProvideLocalNuGetFeed] [-RootPath <string>] [-ExpectedVersion <string>] [-ExpectedVersionMap <IDictionary>] [-VersionTracks <IDictionary>] [-ExpectedVersionMapAsInclude] [-ExpectedVersionMapUseWildcards] [-IncludeProjects <string[]>] [-ExcludeProjects <string[]>] [-ExcludeDirectories <string[]>] [-NugetSource <string[]>] [-IncludePrerelease] [-Configuration <string>] [-OutputPath <string>] [-ReleaseZipOutputPath <string>] [-StagingPath <string>] [-CleanStaging] [-PlanOnly] [-PlanOutputPath <string>] [-UpdateVersions] [-Build] [-PackStrategy <string>] [-PublishNuget] [-PublishGitHub] [-CreateReleaseZip] [-UseGitHubPackages] [-GitHubPackagesOwner <string>] [-PublishSource <string>] [-PublishApiKey <string>] [-PublishApiKeyFilePath <string>] [-PublishApiKeyEnvName <string>] [-SkipDuplicate] [-PublishFailFast] [-CertificateThumbprint <string>] [-CertificateStore <string>] [-TimeStampServer <string>] [-SignAssemblies] [-SignDependencyAssemblies] [-SignPackages] [-NugetCredentialUserName <string>] [-NugetCredentialSecret <string>] [-NugetCredentialSecretFilePath <string>] [-NugetCredentialSecretEnvName <string>] [-GitHubAccessToken <string>] [-GitHubAccessTokenFilePath <string>] [-GitHubAccessTokenEnvName <string>] [-GitHubUsername <string>] [-GitHubRepositoryName <string>] [-GitHubIsPreRelease] [-GitHubIncludeProjectNameInTag] [-GitHubGenerateReleaseNotes] [-GitHubReleaseName <string>] [-GitHubTagName <string>] [-GitHubTagTemplate <string>] [-GitHubReleaseMode <string>] [-GitHubPrimaryProject <string>] [-GitHubTagConflictPolicy <string>] [-Options <IDictionary>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet mirrors the repository package build settings normally authored in project.build.json, allowing
Build-Module { } to remain the primary authoring surface for combined module and package publishing.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationPackageBuild -GitHubAccessTokenFilePath 'C:\Path'
```


## PARAMETERS

### -Build
Whether package projects should be built/packed.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BuildBeforeModule
Whether package outputs must be produced before the module lane runs.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CertificateStore
Certificate store location for package signing.

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

### -CertificateThumbprint
Code signing certificate thumbprint for package signing.

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

### -CleanStaging
Whether to clean staging before the package build runs.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Configuration
Build configuration, usually Release or Debug.

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

### -CreateReleaseZip
Whether release ZIPs should be created for package projects.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enabled
Whether this package build lane is enabled. Defaults to true.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeDirectories
Directory names to exclude from project discovery.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeProjects
Project names to exclude.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedVersion
Global expected package version or X-pattern.

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

### -ExpectedVersionMap
Per-project expected package version map.

```yaml
Type: IDictionary
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedVersionMapAsInclude
When true, ExpectedVersionMap acts as an include list.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExpectedVersionMapUseWildcards
When true, ExpectedVersionMap keys support wildcard matching.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GitHubAccessToken
Inline GitHub access token. Prefer file or environment forms for automation.

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

### -GitHubAccessTokenEnvName
Environment variable containing the GitHub access token.

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

### -GitHubAccessTokenFilePath
Path to a file containing the GitHub access token.

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

### -GitHubGenerateReleaseNotes
Whether GitHub should generate release notes.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GitHubIncludeProjectNameInTag
Whether project name should be included in generated package GitHub tags. Defaults to true.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GitHubIsPreRelease
Whether GitHub releases should be marked prerelease.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GitHubPackagesOwner
GitHub user or organization that owns the GitHub Packages NuGet feed.

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

### -GitHubPrimaryProject
Primary project used for single-release version resolution.

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

### -GitHubReleaseMode
GitHub release mode, for example Single or PerProject.

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

### -GitHubReleaseName
GitHub release name template or override.

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

### -GitHubRepositoryName
GitHub repository name.

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

### -GitHubTagConflictPolicy
GitHub tag conflict policy.

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

### -GitHubTagName
GitHub tag name override.

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

### -GitHubTagTemplate
GitHub tag template.

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

### -GitHubUsername
GitHub owner/user name.

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

### -IncludePrerelease
Whether prerelease versions can be considered during version lookup.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeProjects
Project names to include.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Optional friendly name for this package build lane.

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

### -NugetCredentialSecret
NuGet version lookup credential secret.

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

### -NugetCredentialSecretEnvName
Environment variable containing the NuGet version lookup credential secret.

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

### -NugetCredentialSecretFilePath
Path to a file containing the NuGet version lookup credential secret.

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

### -NugetCredentialUserName
NuGet version lookup credential user name.

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

### -NugetSource
NuGet sources used for version lookup.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Options
Additional project-build options for fields not yet modeled as first-class parameters.

```yaml
Type: IDictionary
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OutputPath
Package output path override.

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

### -PackStrategy
Pack strategy, for example PerProject or MSBuild.

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

### -PlanOnly
Whether to produce a plan without executing package build steps.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PlanOutputPath
Plan output path.

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

### -ProvideLocalNuGetFeed
Whether package outputs should be exposed as a temporary local NuGet feed for the module lane.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PublishApiKey
Inline NuGet publish API key. Prefer file or environment forms for automation.

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

### -PublishApiKeyEnvName
Environment variable containing the NuGet publish API key.

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

### -PublishApiKeyFilePath
Path to a file containing the NuGet publish API key.

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

### -PublishFailFast
Whether package publishing should stop on first failure.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PublishGitHub
Whether package GitHub release publishing should be enabled.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PublishNuget
Whether NuGet packages should be published.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PublishSource
NuGet publish source.

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

### -ReleaseZipOutputPath
Release ZIP output path override.

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

### -RootPath
Root path used for project discovery.

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

### -SignAssemblies
Whether assemblies should be signed before packages are created.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignDependencyAssemblies
Whether copied dependency assemblies should also be signed.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SignPackages
Whether generated NuGet packages should be signed.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipDuplicate
Whether duplicate NuGet packages should be skipped during push.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -StagingPath
Staging root for project-build outputs.

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

### -TimeStampServer
Timestamp server URL for package signing.

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

### -UpdateVersions
Whether project/package versions should be updated.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UseAsReleaseVersionSource
Whether the resolved package version should be used as the unified release version source.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UseGitHubPackages
Whether GitHub Packages should be used as the NuGet version lookup and publish feed.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -VersionTracks
Shared version tracks keyed by track name.

```yaml
Type: IDictionary
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

- `PowerForge.ConfigurationPackageBuildSegment`

## RELATED LINKS

- None

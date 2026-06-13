---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationPublish
## SYNOPSIS
Provides a way to configure publishing to PowerShell Gallery, GitHub, JFrog Artifactory, or other private PowerShell module repositories.

## SYNTAX
### ApiFromFile (Default)
```powershell
New-ConfigurationPublish -Type <PublishDestination> -FilePath <string> [-UserName <string>] [-RepositoryName <string>] [-Tool <PublishTool>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-JFrogBaseUri <string>] [-JFrogRepository <string>] [-RepositoryTrusted <bool>] [-RepositoryPriority <int>] [-RepositoryApiVersion <RepositoryApiVersion>] [-EnsureRepositoryRegistered <bool>] [-UnregisterRepositoryAfterPublish] [-RepositoryCredentialUserName <string>] [-RepositoryCredentialSecret <string>] [-RepositoryCredentialSecretFilePath <string>] [-RepositoryCredentialSecretEnvironmentVariable <string>] [-Enabled] [-OverwriteTagName <string>] [-Force] [-ID <string>] [-DoNotMarkAsPreRelease] [-GenerateReleaseNotes] [-UseAsDependencyVersionSource] [<CommonParameters>]
```

### ApiKey
```powershell
New-ConfigurationPublish -Type <PublishDestination> -ApiKey <string> [-UserName <string>] [-RepositoryName <string>] [-Tool <PublishTool>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-JFrogBaseUri <string>] [-JFrogRepository <string>] [-RepositoryTrusted <bool>] [-RepositoryPriority <int>] [-RepositoryApiVersion <RepositoryApiVersion>] [-EnsureRepositoryRegistered <bool>] [-UnregisterRepositoryAfterPublish] [-RepositoryCredentialUserName <string>] [-RepositoryCredentialSecret <string>] [-RepositoryCredentialSecretFilePath <string>] [-RepositoryCredentialSecretEnvironmentVariable <string>] [-Enabled] [-OverwriteTagName <string>] [-Force] [-ID <string>] [-DoNotMarkAsPreRelease] [-GenerateReleaseNotes] [-UseAsDependencyVersionSource] [<CommonParameters>]
```

### AzureArtifacts
```powershell
New-ConfigurationPublish -AzureDevOpsOrganization <string> -AzureArtifactsFeed <string> [-AzureDevOpsProject <string>] [-RepositoryName <string>] [-Tool <PublishTool>] [-RepositoryTrusted <bool>] [-RepositoryPriority <int>] [-RepositoryApiVersion <RepositoryApiVersion>] [-EnsureRepositoryRegistered <bool>] [-UnregisterRepositoryAfterPublish] [-RepositoryCredentialUserName <string>] [-RepositoryCredentialSecret <string>] [-RepositoryCredentialSecretFilePath <string>] [-RepositoryCredentialSecretEnvironmentVariable <string>] [-Enabled] [-Force] [-ID <string>] [-UseAsDependencyVersionSource] [<CommonParameters>]
```

### Profile
```powershell
New-ConfigurationPublish -ProfileName <string> [-FilePath <string>] [-ApiKey <string>] [-RepositoryCredentialUserName <string>] [-RepositoryCredentialSecret <string>] [-RepositoryCredentialSecretFilePath <string>] [-RepositoryCredentialSecretEnvironmentVariable <string>] [-Enabled] [-Force] [-ID <string>] [<CommonParameters>]
```

### JFrog
```powershell
New-ConfigurationPublish -JFrogBaseUri <string> -JFrogRepository <string> [-FilePath <string>] [-ApiKey <string>] [-RepositoryName <string>] [-Tool <PublishTool>] [-RepositoryTrusted <bool>] [-RepositoryPriority <int>] [-RepositoryApiVersion <RepositoryApiVersion>] [-EnsureRepositoryRegistered <bool>] [-UnregisterRepositoryAfterPublish] [-RepositoryCredentialUserName <string>] [-RepositoryCredentialSecret <string>] [-RepositoryCredentialSecretFilePath <string>] [-RepositoryCredentialSecretEnvironmentVariable <string>] [-JFrogPlatformUri <string>] [-JFrogOidcProvider <string>] [-JFrogOidcTokenId <string>] [-JFrogOidcTokenIdEnvironmentVariable <string>] [-JFrogOidcProviderType <JFrogOidcProviderType>] [-Enabled] [-Force] [-ID <string>] [-UseAsDependencyVersionSource] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet emits publish configuration consumed by Invoke-ModuleBuild / Build-Module.
Use -Type to choose a destination. For repository publishing, -Tool selects the provider
(PowerShellGet/PSResourceGet/Auto).

For private repositories (for example Azure DevOps Artifacts, JFrog Artifactory, GitHub Packages, or private NuGet v3 feeds), provide repository URIs
and (optionally) credentials, or use provider-specific preset parameters to resolve those URIs automatically.
To avoid secrets in source control, pass API keys/tokens via -FilePath or environment-specific tooling.

JFrog Artifactory can be configured directly with -JFrogBaseUri and -JFrogRepository.
For PAT/basic-auth feeds, use repository credentials only. Add -FilePath or -ApiKey only when the feed requires a separate NuGet API key for package push.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationPublish -Type PowerShellGallery -FilePath "$env:USERPROFILE\.secrets\psgallery.key" -Enabled
```


### EXAMPLE 2
```powershell
New-ConfigurationPublish -Type GitHub -FilePath "$env:USERPROFILE\.secrets\github.token" -UserName 'EvotecIT' -RepositoryName 'MyModule' -Enabled
```


### EXAMPLE 3
```powershell
New-ConfigurationPublish -ProfileName 'Company' -Enabled
```


### EXAMPLE 4
```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretFilePath "$env:USERPROFILE\.secrets\jfrog-pat.txt" -Enabled
```


### EXAMPLE 5
```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -FilePath "$env:USERPROFILE\.secrets\jfrog-nuget-api-key.txt" -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretFilePath "$env:USERPROFILE\.secrets\jfrog-pat.txt" -Enabled
```


### EXAMPLE 6
```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -RepositoryCredentialUserName 'name@company.com' -RepositoryCredentialSecretEnvironmentVariable 'JFROG_ACCESS_TOKEN' -Enabled
```


### EXAMPLE 7
```powershell
New-ConfigurationPublish -JFrogBaseUri 'https://company.jfrog.io/artifactory' -JFrogRepository 'powershell-virtual' -RepositoryName 'JFrogPS' -Tool PSResourceGet -JFrogOidcProvider 'azure-oidc' -JFrogOidcProviderType Azure -JFrogOidcTokenIdEnvironmentVariable 'JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID' -Enabled
```


## PARAMETERS

### -ApiKey
API key to be used for publishing in clear text. For JFrog, use this only when the feed requires a separate NuGet API key.

```yaml
Type: String
Parameter Sets: ApiKey, Profile, JFrog
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AzureArtifactsFeed
Azure Artifacts feed name for the private gallery preset.

```yaml
Type: String
Parameter Sets: AzureArtifacts
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AzureDevOpsOrganization
Azure DevOps organization name for the Azure Artifacts preset.

```yaml
Type: String
Parameter Sets: AzureArtifacts
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AzureDevOpsProject
Optional Azure DevOps project name for project-scoped feeds.

```yaml
Type: String
Parameter Sets: AzureArtifacts
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DoNotMarkAsPreRelease
Publish GitHub release as a release even if module prerelease is set.

```yaml
Type: SwitchParameter
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enabled
Enable publishing to the chosen destination.

```yaml
Type: SwitchParameter
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, Profile, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnsureRepositoryRegistered
When true, registers/updates the repository before publishing. Default: true.

```yaml
Type: Boolean
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FilePath
API key to be used for publishing in clear text in a file. For JFrog, use this only when the feed requires a separate NuGet API key.

```yaml
Type: String
Parameter Sets: ApiFromFile, Profile, JFrog
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Allow publishing lower version of a module on a PowerShell repository.

```yaml
Type: SwitchParameter
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, Profile, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GenerateReleaseNotes
When set, asks GitHub to generate release notes automatically.

```yaml
Type: SwitchParameter
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ID
Optional ID of the artefact used for publishing.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, Profile, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogBaseUri
JFrog Artifactory base URI, for example https://company.jfrog.io/artifactory. PowerShellGet and PSResourceGet URLs are derived automatically.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, JFrog
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogOidcProvider
JFrog OIDC provider name configured in Artifactory. Enables runtime token exchange through JFrog CLI.

```yaml
Type: String
Parameter Sets: JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogOidcProviderType
JFrog OIDC provider implementation passed to JFrog CLI. Use Azure for Azure DevOps or Entra-backed OIDC mappings.

```yaml
Type: JFrogOidcProviderType
Parameter Sets: JFrog
Aliases: None
Possible values: GitHub, Azure, GenericOidc

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogOidcTokenId
CI-issued OIDC token value used by JFrog CLI token exchange. Prefer JFrogOidcTokenIdEnvironmentVariable in CI.

```yaml
Type: String
Parameter Sets: JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogOidcTokenIdEnvironmentVariable
Environment variable containing the CI-issued OIDC token value used by JFrog CLI token exchange.

```yaml
Type: String
Parameter Sets: JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogPlatformUri
JFrog Platform URL used for JFrog CLI OIDC token exchange. Defaults from JFrogBaseUri when omitted.

```yaml
Type: String
Parameter Sets: JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogRepository
JFrog NuGet repository key used to derive PowerShellGet and PSResourceGet endpoints, for example powershell-virtual.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, JFrog
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OverwriteTagName
Override tag name used for GitHub publishing.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved private gallery profile name for Azure Artifacts publishing.

```yaml
Type: String
Parameter Sets: Profile
Aliases: Profile
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryApiVersion
Repository API version for PSResourceGet registration (v2/v3).

```yaml
Type: RepositoryApiVersion
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values: Auto, V2, V3, ContainerRegistry

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryCredentialSecret
Repository credential secret (password/token) in clear text. For JFrog PAT/basic-auth flows, this is the PAT or access token.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, Profile, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryCredentialSecretEnvironmentVariable
Environment variable containing the repository credential secret (password/token). For JFrog PAT/access-token flows, this can be JFROG_ACCESS_TOKEN or a CI secret variable.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, Profile, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryCredentialSecretFilePath
Repository credential secret (password/token) in a clear-text file. For JFrog PAT/basic-auth flows, prefer this over inline token values.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, Profile, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryCredentialUserName
Repository credential username (basic auth). For JFrog PAT/basic-auth flows, this is the JFrog user name or email.

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, Profile, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryName
Repository name override (GitHub or PowerShell repository name).

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryPriority
Repository priority for PSResourceGet (lower is higher priority).

```yaml
Type: Nullable`1
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryPublishUri
Repository publish URI (PowerShellGet PublishLocation).

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositorySourceUri
Repository source URI (PowerShellGet SourceLocation).

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryTrusted
Whether to mark the repository as trusted (avoids prompts). Default: true.

```yaml
Type: Boolean
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryUri
Repository base URI (used for both source and publish unless overridden).

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Tool
Publishing tool/provider used for repository publishing. Ignored for GitHub publishing.

```yaml
Type: PublishTool
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values: Auto, PSResourceGet, PowerShellGet

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Type
Choose between PowerShellGallery and GitHub.

```yaml
Type: PublishDestination
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values: PowerShellGallery, GitHub

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UnregisterRepositoryAfterPublish
When set, unregisters the repository after publish if it was created by this run.

```yaml
Type: SwitchParameter
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UseAsDependencyVersionSource
Use this PowerShell repository as the source for resolving Auto/Latest dependency versions.

```yaml
Type: SwitchParameter
Parameter Sets: ApiFromFile, ApiKey, AzureArtifacts, JFrog
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UserName
GitHub username (required for GitHub publishing).

```yaml
Type: String
Parameter Sets: ApiFromFile, ApiKey
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

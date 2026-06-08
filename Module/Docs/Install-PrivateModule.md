---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Install-PrivateModule
## SYNOPSIS
Installs one or more modules from a private repository, optionally bootstrapping Azure Artifacts registration first.

## SYNTAX
### Repository (Default)
```powershell
Install-PrivateModule [-Name] <string[]> -Repository <string> [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-Prerelease] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### AzureArtifacts
```powershell
Install-PrivateModule [-Name] <string[]> [-Repository <string>] [-Provider <PrivateGalleryProvider>] [-AzureDevOpsOrganization <string>] [-AzureDevOpsProject <string>] [-AzureArtifactsFeed <string>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-JFrogBaseUri <string>] [-JFrogRepository <string>] [-RepositoryName <string>] [-Tool <RepositoryRegistrationTool>] [-BootstrapMode <PrivateGalleryBootstrapMode>] [-Trusted <bool>] [-Priority <int>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-Prerelease] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Profile
```powershell
Install-PrivateModule [-Name] <string[]> -ProfileName <string> [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-Prerelease] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### MicrosoftArtifactRegistry
```powershell
Install-PrivateModule [-Name] <string[]> -MicrosoftArtifactRegistry [-RepositoryName <string>] [-Tool <RepositoryRegistrationTool>] [-Trusted <bool>] [-Priority <int>] [-InstallPrerequisites] [-Prerelease] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet provides the simplified end-user flow for private gallery onboarding. You can point it at an existing
repository name or provide Azure Artifacts details and let the cmdlet register the repository before installing
the requested modules.

## EXAMPLES

### EXAMPLE 1
```powershell
Install-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'
```


### EXAMPLE 2
```powershell
Install-PrivateModule -Name 'ModuleA', 'ModuleB' -ProfileName 'Company' -InstallPrerequisites
```


## PARAMETERS

### -AzureArtifactsFeed
Azure Artifacts feed name.

```yaml
Type: String
Parameter Sets: AzureArtifacts
Aliases: Feed
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -AzureDevOpsOrganization
Azure DevOps organization name.

```yaml
Type: String
Parameter Sets: AzureArtifacts
Aliases: Organization
Possible values:

Required: False
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
Aliases: Project
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BootstrapMode
Bootstrap/authentication mode used when Azure Artifacts details are supplied. Auto prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.

```yaml
Type: PrivateGalleryBootstrapMode
Parameter Sets: AzureArtifacts
Aliases: Mode
Possible values: Auto, ExistingSession, CredentialPrompt, JFrogCli

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialSecret
Optional repository credential secret.

```yaml
Type: String
Parameter Sets: Repository, AzureArtifacts, Profile
Aliases: Password, Token
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialSecretFilePath
Optional path to a file containing the repository credential secret.

```yaml
Type: String
Parameter Sets: Repository, AzureArtifacts, Profile
Aliases: CredentialPath, TokenPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialUserName
Optional repository credential username.

```yaml
Type: String
Parameter Sets: Repository, AzureArtifacts, Profile
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Forces reinstall even when a matching version is already present.

```yaml
Type: SwitchParameter
Parameter Sets: Repository, AzureArtifacts, Profile, MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallPrerequisites
Installs missing private-gallery prerequisites before automatic registration, including PSResourceGet requirements and, for Azure Artifacts, the credential provider.

```yaml
Type: SwitchParameter
Parameter Sets: AzureArtifacts, Profile, MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -JFrogBaseUri
JFrog Artifactory base URI, for example https://company.jfrog.io/artifactory.

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

### -JFrogRepository
JFrog NuGet repository key. Defaults from Repository when omitted.

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

### -MicrosoftArtifactRegistry
Installs Microsoft-owned packages from Microsoft Artifact Registry, registering MAR first when needed.

```yaml
Type: SwitchParameter
Parameter Sets: MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Module names to install.

```yaml
Type: String[]
Parameter Sets: Repository, AzureArtifacts, Profile, MicrosoftArtifactRegistry
Aliases: ModuleName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Prerelease
Includes prerelease versions when supported by the selected installer.

```yaml
Type: SwitchParameter
Parameter Sets: Repository, AzureArtifacts, Profile, MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Priority
Optional PSResourceGet repository priority used during automatic registration.

```yaml
Type: Nullable`1
Parameter Sets: AzureArtifacts, MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved repository profile name.

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

### -PromptForCredential
Prompts interactively for repository credentials.

```yaml
Type: SwitchParameter
Parameter Sets: Repository, AzureArtifacts, Profile
Aliases: Interactive
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Provider
Private gallery provider used for automatic repository registration.

```yaml
Type: PrivateGalleryProvider
Parameter Sets: AzureArtifacts
Aliases: None
Possible values: AzureArtifacts, Azure, JFrog, NuGet, GitHubPackages, GitHub

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repository
Name of an already registered repository, or provider repository/feed id when a private-gallery provider is selected.

```yaml
Type: String
Parameter Sets: Repository, AzureArtifacts
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryName
Optional repository name override when Azure Artifacts details are supplied.

```yaml
Type: String
Parameter Sets: AzureArtifacts, MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryPublishUri
PowerShellGet publish URI for generic/JFrog feeds.

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

### -RepositorySourceUri
PowerShellGet source URI for generic/JFrog feeds.

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

### -RepositoryUri
PSResourceGet v3 repository URI for generic/JFrog feeds.

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

### -Tool
Registration strategy used when Azure Artifacts details are supplied. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.

```yaml
Type: RepositoryRegistrationTool
Parameter Sets: AzureArtifacts, MicrosoftArtifactRegistry
Aliases: None
Possible values: Auto, PSResourceGet, PowerShellGet, Both

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Trusted
When true, marks the repository as trusted during automatic registration.

```yaml
Type: Boolean
Parameter Sets: AzureArtifacts, MicrosoftArtifactRegistry
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

- `PowerForge.ModuleDependencyInstallResult`

## RELATED LINKS

- None

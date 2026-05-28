---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Connect-ModuleRepository
## SYNOPSIS
Registers an Azure Artifacts repository if needed and validates authenticated access for the selected bootstrap mode.

## SYNTAX
### AzureArtifacts (Default)
```powershell
Connect-ModuleRepository [-Provider <PrivateGalleryProvider>] [-AzureDevOpsOrganization <string>] [-AzureDevOpsProject <string>] [-AzureArtifactsFeed <string>] [-Name <string>] [-Repository <string>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-JFrogBaseUri <string>] [-JFrogRepository <string>] [-Tool <RepositoryRegistrationTool>] [-BootstrapMode <PrivateGalleryBootstrapMode>] [-Trusted <bool>] [-Priority <int>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Profile
```powershell
Connect-ModuleRepository -ProfileName <string> [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### MicrosoftArtifactRegistry
```powershell
Connect-ModuleRepository -MicrosoftArtifactRegistry [-Name <string>] [-Repository <string>] [-Tool <RepositoryRegistrationTool>] [-Trusted <bool>] [-Priority <int>] [-InstallPrerequisites] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the explicit "connect/login" companion to Register-ModuleRepository. It ensures the
repository registration exists and then performs a lightweight authenticated probe so callers know whether
the chosen bootstrap path can actually access the private feed.

## EXAMPLES

### EXAMPLE 1
```powershell
Connect-ModuleRepository -ProfileName 'Company' -InstallPrerequisites
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
Bootstrap/authentication mode. Auto uses supplied or prompted credentials when requested; otherwise it prefers ExistingSession when Azure Artifacts prerequisites are ready and falls back to CredentialPrompt when they are not.

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
Parameter Sets: AzureArtifacts, Profile
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
Parameter Sets: AzureArtifacts, Profile
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
Parameter Sets: AzureArtifacts, Profile
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallPrerequisites
Installs missing private-gallery prerequisites before connecting, including PSResourceGet requirements and, for Azure Artifacts, the credential provider.

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
Registers and probes Microsoft Artifact Registry as a PSResourceGet repository for Microsoft-owned packages.

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
Optional repository name override. Defaults to the feed name.

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

### -Priority
Optional PSResourceGet repository priority.

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
Parameter Sets: AzureArtifacts, Profile
Aliases: Interactive
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Provider
Private gallery provider.

```yaml
Type: PrivateGalleryProvider
Parameter Sets: AzureArtifacts
Aliases: None
Possible values: AzureArtifacts, Azure, JFrog, NuGet

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repository
Provider repository/feed id. For Azure this is the feed when AzureArtifactsFeed is omitted; for JFrog this is the Artifactory NuGet repository key.

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
Registration strategy. Auto prefers PSResourceGet and falls back to PowerShellGet when needed.

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
When true, marks the repository as trusted.

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

- `PSPublishModule.ModuleRepositoryRegistrationResult` — Result returned when registering or refreshing a private module repository.

## RELATED LINKS

- None

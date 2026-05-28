---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Initialize-ModuleRepository
## SYNOPSIS
Performs one-command enterprise onboarding for a private module repository profile.

## SYNTAX
### Profile (Default)
```powershell
Initialize-ModuleRepository [-ProfileName] <string> [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-SkipConnect] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Import
```powershell
Initialize-ModuleRepository [-Path] <string> [-ProfileName <string>] [-Overwrite] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-SkipConnect] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### AzureArtifacts
```powershell
Initialize-ModuleRepository [-ProfileName] <string> [-Provider <PrivateGalleryProvider>] [-AzureDevOpsOrganization <string>] [-AzureDevOpsProject <string>] [-AzureArtifactsFeed <string>] [-Repository <string>] [-RepositoryName <string>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-JFrogBaseUri <string>] [-JFrogRepository <string>] [-Tool <RepositoryRegistrationTool>] [-BootstrapMode <PrivateGalleryBootstrapMode>] [-Trusted <bool>] [-Priority <int>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-SkipConnect] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the managed-workstation entry point for private gallery onboarding. It can use an existing saved
profile, import a non-secret profile JSON file, or create an Azure Artifacts profile from feed details. Unless
-SkipConnect is used, it then installs requested prerequisites, registers the repository, and validates
authenticated access through the selected bootstrap mode.

## EXAMPLES

### EXAMPLE 1
```powershell
Initialize-ModuleRepository -Path .\Company.profile.json -ProfileName Company -Overwrite -InstallPrerequisites
```

Imports the non-secret profile, installs/refreshes prerequisites, registers the repository, and triggers the Azure Artifacts credential-provider login flow when needed.

### EXAMPLE 2
```powershell
Initialize-ModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -InstallPrerequisites
```

Saves an Entra-first profile and connects the workstation in one command.

### EXAMPLE 3
```powershell
Initialize-ModuleRepository -ProfileName Company -SkipConnect
```

Returns profile and local prerequisite readiness without registering or probing the repository.

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
Bootstrap/authentication mode saved in a new profile. Defaults to ExistingSession for Azure Artifacts Credential Provider login.

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
Optional repository credential secret for credential-prompt fallback environments.

```yaml
Type: String
Parameter Sets: Profile, Import, AzureArtifacts
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
Parameter Sets: Profile, Import, AzureArtifacts
Aliases: CredentialPath, TokenPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CredentialUserName
Optional repository credential username for credential-prompt fallback environments.

```yaml
Type: String
Parameter Sets: Profile, Import, AzureArtifacts
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallPrerequisites
Installs missing private-gallery prerequisites before connecting, including the PSResourceGet version required by the selected bootstrap mode and the Azure Artifacts credential provider.

```yaml
Type: SwitchParameter
Parameter Sets: Profile, Import, AzureArtifacts
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

### -Overwrite
Replace saved profiles with matching names when importing from Path.

```yaml
Type: SwitchParameter
Parameter Sets: Import
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Source JSON profile file exported with Export-ModuleRepositoryProfile.

```yaml
Type: String
Parameter Sets: Import
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Priority
Optional PSResourceGet repository priority.

```yaml
Type: Nullable`1
Parameter Sets: AzureArtifacts
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved repository profile name. When used with Path, selects one imported profile from the file. When used with Azure Artifacts feed details, creates that profile name.

```yaml
Type: String
Parameter Sets: Profile, Import, AzureArtifacts
Aliases: Name, Profile
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PromptForCredential
Prompts interactively for repository credentials in credential-prompt fallback environments.

```yaml
Type: SwitchParameter
Parameter Sets: Profile, Import, AzureArtifacts
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
Parameter Sets: AzureArtifacts
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryName
Optional local repository name override. Defaults to the profile name for new Azure Artifacts profiles.

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

### -Scope
Profile store scope. Existing profiles default to user-then-machine lookup; profile creation/import defaults to the current user's store unless Machine is specified.

```yaml
Type: ModuleRepositoryProfileScope
Parameter Sets: Profile, Import, AzureArtifacts
Aliases: None
Possible values: User, Machine, All

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SkipConnect
Save/import/test the profile but do not register, connect, or probe the repository.

```yaml
Type: SwitchParameter
Parameter Sets: Profile, Import, AzureArtifacts
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Tool
Registration strategy saved in a new profile. Defaults to PSResourceGet for Entra-first Azure Artifacts use.

```yaml
Type: RepositoryRegistrationTool
Parameter Sets: AzureArtifacts
Aliases: None
Possible values: Auto, PSResourceGet, PowerShellGet, Both

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Trusted
When true, marks the repository as trusted during registration.

```yaml
Type: Boolean
Parameter Sets: AzureArtifacts
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

- `PSPublishModule.ModuleRepositoryOnboardingResult` — Result returned by Initialize-ModuleRepository for enterprise private-gallery onboarding.

## RELATED LINKS

- None

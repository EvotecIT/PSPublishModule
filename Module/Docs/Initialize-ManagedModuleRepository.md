---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Initialize-ManagedModuleRepository
## SYNOPSIS
Performs one-command onboarding for managed module repository profiles.

## SYNTAX
### Profile (Default)
```powershell
Initialize-ManagedModuleRepository [-ProfileName] <string> [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-BootstrapPath <string>] [-BootstrapScriptName <string>] [-BootstrapProfileFileName <string>] [-InstallModule <string[]>] [-BootstrapForce] [-SkipConnect] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Import
```powershell
Initialize-ManagedModuleRepository [-Path] <string> [-ProfileName <string>] [-Overwrite] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-BootstrapPath <string>] [-BootstrapScriptName <string>] [-BootstrapProfileFileName <string>] [-InstallModule <string[]>] [-BootstrapForce] [-SkipConnect] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Repository
```powershell
Initialize-ManagedModuleRepository [-ProfileName] <string> [-Provider <PrivateGalleryProvider>] [-AzureDevOpsOrganization <string>] [-AzureDevOpsProject <string>] [-AzureArtifactsFeed <string>] [-Repository <string>] [-RepositoryName <string>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-JFrogBaseUri <string>] [-JFrogRepository <string>] [-GitHubOwner <string>] [-Tool <RepositoryRegistrationTool>] [-BootstrapMode <PrivateGalleryBootstrapMode>] [-Trusted <bool>] [-Priority <int>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-BootstrapPath <string>] [-BootstrapScriptName <string>] [-BootstrapProfileFileName <string>] [-InstallModule <string[]>] [-BootstrapForce] [-SkipConnect] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### MicrosoftArtifactRegistry
```powershell
Initialize-ManagedModuleRepository -MicrosoftArtifactRegistry [-RepositoryName <string>] [-Tool <RepositoryRegistrationTool>] [-Trusted <bool>] [-Priority <int>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-BootstrapPath <string>] [-BootstrapScriptName <string>] [-BootstrapProfileFileName <string>] [-InstallModule <string[]>] [-BootstrapForce] [-SkipConnect] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the workstation or build-agent onboarding entry point for managed module repositories. It can use an
existing saved profile, import a non-secret profile JSON file, or create a profile from feed details. Unless
-SkipConnect is used, it then installs requested prerequisites, registers/probes native provider state where
needed, and validates authenticated access through the selected bootstrap mode. It can also write a distributable
non-secret bootstrap package for other machines.

## EXAMPLES

### EXAMPLE 1
```powershell
Initialize-ManagedModuleRepository -Path .\Company.repository.json -ProfileName Company -Overwrite -InstallPrerequisites
```

Imports the non-secret profile, installs/refreshes prerequisites, registers the repository, and triggers the Azure Artifacts credential-provider login flow when needed.

### EXAMPLE 2
```powershell
Initialize-ManagedModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -InstallPrerequisites
```

Saves an Entra-first profile and connects the workstation in one command.

### EXAMPLE 3
```powershell
Initialize-ManagedModuleRepository -ProfileName Company -SkipConnect
```

Returns profile and local prerequisite readiness without registering or probing the repository.

## PARAMETERS

### -AzureArtifactsFeed
Azure Artifacts feed name.

```yaml
Type: String
Parameter Sets: Repository
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
Parameter Sets: Repository
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
Parameter Sets: Repository
Aliases: Project
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BootstrapForce
Overwrite existing bootstrap files when BootstrapPath is used.

```yaml
Type: SwitchParameter
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
Aliases: None
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
Parameter Sets: Repository
Aliases: Mode
Possible values: Auto, ExistingSession, CredentialPrompt, JFrogCli

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BootstrapPath
Optional output directory for a non-secret onboarding package that imports the selected profiles and can install starter modules.

```yaml
Type: String
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BootstrapProfileFileName
Generated profile JSON file name when BootstrapPath is used.

```yaml
Type: String
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -BootstrapScriptName
Generated bootstrap script file name when BootstrapPath is used.

```yaml
Type: String
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
Aliases: None
Possible values:

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
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
Aliases: UserName
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GitHubOwner
GitHub user or organization namespace for GitHub Packages. Defaults from Repository when omitted.

```yaml
Type: String
Parameter Sets: Repository
Aliases: Owner, Namespace
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallModule
Optional module names written into the bootstrap script as starter managed installs.

```yaml
Type: String[]
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
Aliases: ModuleName
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
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Repository
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
Parameter Sets: Repository
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MicrosoftArtifactRegistry
Initializes the public Microsoft Artifact Registry PowerShell repository.

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
Source JSON profile file exported with Get-ManagedModuleRepository -ExportPath.

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
Parameter Sets: Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Profile, Import, Repository
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
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Repository
Aliases: None
Possible values: AzureArtifacts, Azure, JFrog, NuGet, GitHubPackages, GitHub

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
Parameter Sets: Repository
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
Parameter Sets: Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Repository
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
Parameter Sets: Repository
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
Parameter Sets: Repository
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
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Profile, Import, Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Repository, MicrosoftArtifactRegistry
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
Parameter Sets: Repository, MicrosoftArtifactRegistry
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

- `PSPublishModule.ModuleRepositoryOnboardingResult
PSPublishModule.ModuleRepositoryRegistrationResult` — Result returned when registering or refreshing a private module repository.

## RELATED LINKS

- None

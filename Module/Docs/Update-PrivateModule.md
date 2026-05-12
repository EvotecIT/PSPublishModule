---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Update-PrivateModule
## SYNOPSIS
Updates one or more modules from a private repository, optionally refreshing Azure Artifacts registration first.

## SYNTAX
### Repository (Default)
```powershell
Update-PrivateModule [-Name] <string[]> -Repository <string> [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-Prerelease] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### AzureArtifacts
```powershell
Update-PrivateModule [-Name] <string[]> -AzureDevOpsOrganization <string> -AzureArtifactsFeed <string> [-Provider <PrivateGalleryProvider>] [-AzureDevOpsProject <string>] [-RepositoryName <string>] [-Tool <RepositoryRegistrationTool>] [-BootstrapMode <PrivateGalleryBootstrapMode>] [-Trusted <bool>] [-Priority <int>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-Prerelease] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the day-to-day maintenance companion to Install-PrivateModule. When Azure Artifacts details
are provided, the repository registration is refreshed before the update is attempted.

## EXAMPLES

### EXAMPLE 1
```powershell
Update-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'
```


### EXAMPLE 2
```powershell
Update-PrivateModule -Name 'ModuleA', 'ModuleB' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -PromptForCredential
```


## PARAMETERS

### -AzureArtifactsFeed
Azure Artifacts feed name.

```yaml
Type: String
Parameter Sets: AzureArtifacts
Aliases: Feed
Possible values: 

Required: True
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
Possible values: Auto, ExistingSession, CredentialPrompt

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
Parameter Sets: Repository, AzureArtifacts
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
Parameter Sets: Repository, AzureArtifacts
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
Parameter Sets: Repository, AzureArtifacts
Aliases: UserName
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallPrerequisites
Installs missing private-gallery prerequisites such as PSResourceGet and the Azure Artifacts credential provider before automatic registration refresh.

```yaml
Type: SwitchParameter
Parameter Sets: AzureArtifacts
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Name
Module names to update.

```yaml
Type: String[]
Parameter Sets: Repository, AzureArtifacts
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
Parameter Sets: Repository, AzureArtifacts
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Priority
Optional PSResourceGet repository priority used during automatic registration refresh.

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

### -PromptForCredential
Prompts interactively for repository credentials.

```yaml
Type: SwitchParameter
Parameter Sets: Repository, AzureArtifacts
Aliases: Interactive
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Provider
Private gallery provider. Currently only AzureArtifacts is supported.

```yaml
Type: PrivateGalleryProvider
Parameter Sets: AzureArtifacts
Aliases: None
Possible values: AzureArtifacts

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repository
Name of an already registered repository.

```yaml
Type: String
Parameter Sets: Repository
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
When true, marks the repository as trusted during automatic registration refresh.

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

- `PowerForge.ModuleDependencyInstallResult`

## RELATED LINKS

- None


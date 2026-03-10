---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Register-ModuleRepository
## SYNOPSIS
Registers an Azure Artifacts feed as a private PowerShell module repository for PowerShellGet and/or PSResourceGet.

## SYNTAX
### __AllParameterSets
```powershell
Register-ModuleRepository -AzureDevOpsOrganization <string> -AzureArtifactsFeed <string> [-Provider <PrivateGalleryProvider>] [-AzureDevOpsProject <string>] [-Name <string>] [-Tool <RepositoryRegistrationTool>] [-BootstrapMode <PrivateGalleryBootstrapMode>] [-Trusted <bool>] [-Priority <int>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-PromptForCredential] [-InstallPrerequisites] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet simplifies Azure Artifacts setup on end-user machines by resolving the correct v2/v3 endpoints
and registering the repository for the selected client tools.

For PowerShellGet, supplied credentials are forwarded to Register-PSRepository so later
Install-Module calls can reuse the registered source. For PSResourceGet, the repository is registered
with the Azure Artifacts v3 endpoint so Install-PSResource can use the Azure Artifacts credential provider.

The output object indicates which native install paths are ready after registration, so callers can see whether
Install-PSResource, Install-Module, or both are available for the configured repository.

## EXAMPLES

### EXAMPLE 1
```powershell
Register-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -PromptForCredential -Trusted
```

## PARAMETERS

### -AzureArtifactsFeed
Azure Artifacts feed name.

```yaml
Type: String
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
Aliases: UserName
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -InstallPrerequisites
Installs missing private-gallery prerequisites such as PSResourceGet and the Azure Artifacts credential provider before registration.

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

### -Name
Optional repository name override. Defaults to the feed name.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Repository
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
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
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
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: AzureArtifacts

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
Parameter Sets: __AllParameterSets
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

- `PSPublishModule.ModuleRepositoryRegistrationResult`

## RELATED LINKS

- None


---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-ModuleRepositoryProfile
## SYNOPSIS
Creates or updates a saved private module repository profile.

## SYNTAX
### __AllParameterSets
```powershell
Set-ModuleRepositoryProfile [-Name] <string> -AzureDevOpsOrganization <string> -AzureArtifactsFeed <string> [-Provider <PrivateGalleryProvider>] [-AzureDevOpsProject <string>] [-RepositoryName <string>] [-Tool <RepositoryRegistrationTool>] [-BootstrapMode <PrivateGalleryBootstrapMode>] [-Trusted <bool>] [-Priority <int>] [-Scope <ModuleRepositoryProfileScope>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Profiles store the non-secret Azure Artifacts feed settings used by Connect-ModuleRepository,
Install-PrivateModule, Update-PrivateModule, New-ConfigurationPublish, and
Publish-NugetPackage. Azure Artifacts profiles default to PSResourceGet with the Azure Artifacts
Credential Provider so Entra ID/MFA is handled by the provider instead of storing PATs in PSPublishModule.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-ModuleRepositoryProfile -Name Company -AzureDevOpsOrganization contoso -AzureDevOpsProject Platform -AzureArtifactsFeed Modules
```

Saves a user-local profile that later commands can reference with -ProfileName Company.

### EXAMPLE 2
```powershell
Set-ModuleRepositoryProfile -Name Finance -AzureDevOpsOrganization contoso -AzureDevOpsProject Platform -AzureArtifactsFeed InternalModules -RepositoryName CompanyModules -Priority 20
```

Stores the Azure Artifacts feed identity while registering it locally as CompanyModules.

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
Bootstrap/authentication mode saved in the profile. Defaults to ExistingSession for Azure Artifacts Credential Provider login.

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

### -Name
Profile name used by connect, install, update, and publish commands.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ProfileName
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
Parameter Sets: __AllParameterSets
Aliases: None
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

### -RepositoryName
Optional local repository name override. Defaults to the feed name.

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

### -Scope
Profile store scope to write. Use Machine from an elevated/admin deployment to share non-secret feed settings with all users.

```yaml
Type: ModuleRepositoryProfileScope
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: User, Machine, All

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Tool
Registration strategy saved in the profile. Defaults to PSResourceGet for Entra-first Azure Artifacts use.

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
When true, marks the repository as trusted during registration.

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

- `PSPublishModule.ModuleRepositoryProfileResult` — User-facing private module repository profile saved by PSPublishModule.

## RELATED LINKS

- None

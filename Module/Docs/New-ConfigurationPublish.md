---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationPublish
## SYNOPSIS
Provides a way to configure publishing to PowerShell Gallery or GitHub.

## SYNTAX
### ApiFromFile (Default)
```powershell
New-ConfigurationPublish -Type <PublishDestination> -FilePath <string> [-UserName <string>] [-RepositoryName <string>] [-Tool <PublishTool>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-RepositoryTrusted <bool>] [-RepositoryPriority <int>] [-RepositoryApiVersion <RepositoryApiVersion>] [-EnsureRepositoryRegistered <bool>] [-UnregisterRepositoryAfterPublish] [-RepositoryCredentialUserName <string>] [-RepositoryCredentialSecret <string>] [-RepositoryCredentialSecretFilePath <string>] [-Enabled] [-OverwriteTagName <string>] [-Force] [-ID <string>] [-DoNotMarkAsPreRelease] [-GenerateReleaseNotes] [<CommonParameters>]
```

### ApiKey
```powershell
New-ConfigurationPublish -Type <PublishDestination> -ApiKey <string> [-UserName <string>] [-RepositoryName <string>] [-Tool <PublishTool>] [-RepositoryUri <string>] [-RepositorySourceUri <string>] [-RepositoryPublishUri <string>] [-RepositoryTrusted <bool>] [-RepositoryPriority <int>] [-RepositoryApiVersion <RepositoryApiVersion>] [-EnsureRepositoryRegistered <bool>] [-UnregisterRepositoryAfterPublish] [-RepositoryCredentialUserName <string>] [-RepositoryCredentialSecret <string>] [-RepositoryCredentialSecretFilePath <string>] [-Enabled] [-OverwriteTagName <string>] [-Force] [-ID <string>] [-DoNotMarkAsPreRelease] [-GenerateReleaseNotes] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet emits publish configuration consumed by Invoke-ModuleBuild / Build-Module.
Use -Type to choose a destination. For repository publishing, -Tool selects the provider (PowerShellGet/PSResourceGet/Auto).

For private repositories (for example Azure DevOps Artifacts / private NuGet v3 feeds), provide repository URIs and (optionally) credentials.
To avoid secrets in source control, pass API keys/tokens via -FilePath or environment-specific tooling.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationPublish -Type PowerShellGallery -FilePath "$env:USERPROFILE\.secrets\psgallery.key" -Enabled
```

### EXAMPLE 2
```powershell
New-ConfigurationPublish -Type GitHub -FilePath "$env:USERPROFILE\.secrets\github.token" -UserName 'EvotecIT' -RepositoryName 'MyModule' -Enabled
```

## PARAMETERS

### -ApiKey
API key to be used for publishing in clear text.

```yaml
Type: String
Parameter Sets: ApiKey
Aliases: None
Possible values: 

Required: True
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
Parameter Sets: ApiFromFile, ApiKey
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
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FilePath
API key to be used for publishing in clear text in a file.

```yaml
Type: String
Parameter Sets: ApiFromFile
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
Parameter Sets: ApiFromFile, ApiKey
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
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values: 

Required: False
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

### -RepositoryApiVersion
Repository API version for PSResourceGet registration (v2/v3).

```yaml
Type: RepositoryApiVersion
Parameter Sets: ApiFromFile, ApiKey
Aliases: None
Possible values: Auto, V2, V3

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryCredentialSecret
Repository credential secret (password/token) in clear text.

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

### -RepositoryCredentialSecretFilePath
Repository credential secret (password/token) in a clear-text file.

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

### -RepositoryCredentialUserName
Repository credential username (basic auth).

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

### -RepositoryName
Repository name override (GitHub or PowerShell repository name).

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

### -RepositoryPriority
Repository priority for PSResourceGet (lower is higher priority).

```yaml
Type: Nullable`1
Parameter Sets: ApiFromFile, ApiKey
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
Parameter Sets: ApiFromFile, ApiKey
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
Parameter Sets: ApiFromFile, ApiKey
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
Parameter Sets: ApiFromFile, ApiKey
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


---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Find-ManagedModule
## SYNOPSIS
Finds module versions from a managed module repository.

## SYNTAX
### __AllParameterSets
```powershell
Find-ManagedModule [[-Name] <string[]>] [[-Repository] <string>] [-RepositoryName <string>] [-ProfileName <string>] [-Version <string>] [-AllVersions] [-First <int>] [-Tag <string[]>] [-ResourceType <string[]>] [-IncludeDependencies] [-Prerelease] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [<CommonParameters>]
```

## DESCRIPTION
This command is the module-package equivalent of Find-PSResource. It queries NuGet-compatible or
local-folder repositories through the managed C# repository client and returns typed module version objects
that can be piped to Install-ManagedModule or Save-ManagedModule.

Name, exact/range version, prerelease, tag, and dependency searches are supported for module packages.
Command-name and DSC-resource-name searches inspect package contents and remain outside this fast metadata path.

## EXAMPLES

### EXAMPLE 1
```powershell
Find-ManagedModule -Name Company.Tools
```


### EXAMPLE 2
```powershell
Find-ManagedModule -Name Company.* -Repository C:\Packages
```


### EXAMPLE 3
```powershell
Find-ManagedModule -Name Company.Tools -Repository C:\Packages -AllVersions -AllowPrerelease
```


### EXAMPLE 4
```powershell
Find-ManagedModule -Name Company.Tools -Version '[1.2.0,2.0.0)' -ProfileName CompanyModules | Save-ManagedModule -Path C:\OfflineModules
```


## PARAMETERS

### -AllVersions
Return all matching versions instead of only the latest selected version.

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

### -Credential
Optional repository credential.

```yaml
Type: PSCredential
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

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

### -First
Maximum search results returned for wildcard name queries.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeDependencies
Include dependency resources exposed by repository metadata.

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
Module names or wildcard patterns to find. When omitted, all module package names are considered.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ModuleName
Possible values:

Required: False
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### -Prerelease
Include prerelease versions.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: AllowPrerelease
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved module repository profile to use instead of Repository.

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

### -Proxy
Optional HTTP proxy used for repository requests.

```yaml
Type: Uri
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProxyCredential
Optional proxy credential used with Proxy.

```yaml
Type: PSCredential
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Repository
Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Source, RepositoryUri
Possible values:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RepositoryName
Friendly repository name used in output.

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

### -ResourceType
Resource kind to find. Find-ManagedModule currently returns module resources.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Type
Possible values: Module

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Tag
Filter results by package tag metadata.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Tags
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Version
Exact version, wildcard version, or NuGet-style version range to return.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String[]`

## OUTPUTS

- `PowerForge.ManagedModuleVersionInfo`

## RELATED LINKS

- None

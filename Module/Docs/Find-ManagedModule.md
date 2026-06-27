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
Find-ManagedModule [-Name] <string[]> [[-Repository] <string>] [-RepositoryName <string>] [-ProfileName <string>] [-AllVersions] [-First <int>] [-Prerelease] [-Credential <pscredential>] [-CredentialUserName <string>] [-CredentialSecret <string>] [-CredentialSecretFilePath <string>] [<CommonParameters>]
```

## DESCRIPTION
This command queries NuGet v3 or local-folder repositories through the managed C# repository client.

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

### -Name
Module names to find.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: ModuleName
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.String[]`

## OUTPUTS

- `PowerForge.ManagedModuleVersionInfo`

## RELATED LINKS

- None

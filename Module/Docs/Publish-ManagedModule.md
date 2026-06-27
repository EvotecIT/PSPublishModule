---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Publish-ManagedModule
## SYNOPSIS
Publishes a PowerShell module package through the managed C# module engine.

## SYNTAX
### __AllParameterSets
```powershell
Publish-ManagedModule [-Path] <string> [[-Repository] <string>] [-RepositoryName <string>] [-ProfileName <string>] [-OutputDirectory <string>] [-ManifestPath <string>] [-Name <string>] [-Version <string>] [-Authors <string>] [-Description <string>] [-ProjectUrl <string>] [-Tags <string[]>] [-Credential <pscredential>] [-ApiKey <string>] [-ApiKeyFilePath <string>] [-Force] [-SkipDependenciesCheck] [-SkipModuleManifestValidate] [-ShowSummary] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This managed publish surface creates a NuGet package from a module folder and publishes it to a local folder feed
or NuGet-compatible package publish endpoint.

## EXAMPLES

### EXAMPLE 1
```powershell
Publish-ManagedModule -Path C:\Source\Company.Tools -Repository C:\Packages
```


## PARAMETERS

### -ApiKey
API key used by NuGet-compatible package publish endpoints.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: NuGetApiKey
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ApiKeyFilePath
Optional path to a file containing the API key.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ApiKeyPath, NuGetApiKeyPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Authors
Optional authors override.

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

### -Description
Optional description override.

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

### -Force
Overwrite an existing package.

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

### -ManifestPath
Optional explicit module manifest path.

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

### -Name
Optional package id override.

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

### -OutputDirectory
Output directory used when Repository is omitted.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: DestinationPath, OutputPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Module folder to package.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ModulePath
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProfileName
Saved module repository profile to use instead of Repository or OutputDirectory.

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

### -ProjectUrl
Optional project URL override.

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
Repository URL, NuGet v3 service index, publish endpoint, or local folder feed.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: RepositoryPath, RepositoryUri, Source
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

### -ShowSummary
Write a compact Spectre.Console summary for the publish result.

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

### -SkipDependenciesCheck
Skip checking RequiredModules against the target repository.

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

### -SkipModuleManifestValidate
Skip managed manifest metadata validation before packaging.

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

### -Tags
Optional package tags override.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Version
Optional package version override.

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

- `None`

## OUTPUTS

- `PowerForge.ManagedModulePublishResult`

## RELATED LINKS

- None

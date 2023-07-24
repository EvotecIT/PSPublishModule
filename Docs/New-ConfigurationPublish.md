---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationPublish

## SYNOPSIS
Provide a way to configure publishing to PowerShell Gallery or GitHub

## SYNTAX

### ApiFromFile
```
New-ConfigurationPublish -Type <String> -FilePath <String> [-UserName <String>] [-RepositoryName <String>]
 [-Enabled] [-OverwriteTagName <String>] [-Force] [-ID <String>] [-DoNotMarkAsPreRelease] [<CommonParameters>]
```

### ApiKey
```
New-ConfigurationPublish -Type <String> -ApiKey <String> [-UserName <String>] [-RepositoryName <String>]
 [-Enabled] [-OverwriteTagName <String>] [-Force] [-ID <String>] [-DoNotMarkAsPreRelease] [<CommonParameters>]
```

## DESCRIPTION
Provide a way to configure publishing to PowerShell Gallery or GitHub
You can configure publishing to both at the same time
You can publish to multiple PowerShellGalleries at the same time as well
You can have multiple GitHub configurations at the same time as well

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationPublish -Type PowerShellGallery -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' -Enabled:$true
```

### EXAMPLE 2
```
New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHub'
```

## PARAMETERS

### -Type
Choose between PowerShellGallery and GitHub

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FilePath
API Key to be used for publishing to GitHub or PowerShell Gallery in clear text in file

```yaml
Type: String
Parameter Sets: ApiFromFile
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ApiKey
API Key to be used for publishing to GitHub or PowerShell Gallery in clear text

```yaml
Type: String
Parameter Sets: ApiKey
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UserName
When used for GitHub this parameter is required to know to which repository to publish.
This parameter is not used for PSGallery publishing

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RepositoryName
When used for PowerShellGallery publishing this parameter provides a way to overwrite default PowerShellGallery and publish to a different repository
When not used, the default PSGallery will be used.
When used for GitHub publishing this parameter provides a way to overwrite default repository name and publish to a different repository
When not used, the default repository name will be used, that matches the module name

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Enabled
Enable publishing to GitHub or PowerShell Gallery

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -OverwriteTagName
Allow to overwrite tag name when publishing to GitHub.
By default "v\<ModuleVersion\>" will be used i.e v1.0.0

You can use following variables that will be replaced with actual values:
- \<ModuleName\> / {ModuleName} - the name of the module i.e PSPublishModule
- \<ModuleVersion\> / {ModuleVersion} - the version of the module i.e 1.0.0
- \<ModuleVersionWithPreRelease\> / {ModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e 1.0.0-Preview1
- \<TagModuleVersionWithPreRelease\> / {TagModuleVersionWithPreRelease} - the version of the module with pre-release tag i.e v1.0.0-Preview1
- \<TagName\> / {TagName} - the name of the tag - i.e.
v1.0.0

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Allow to publish lower version of module on PowerShell Gallery.
By default it will fail if module with higher version already exists.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -ID
Optional ID of the artefact.
If not specified, the default packed artefact will be used.
If no packed artefact is specified, the first packed artefact will be used (if enabled)
If no packed artefact is enabled, the publishing will fail

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DoNotMarkAsPreRelease
Allow to publish to GitHub as release even if pre-release tag is set on the module version.
By default it will be published as pre-release if pre-release tag is set.
This setting prevents it.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
General notes

## RELATED LINKS

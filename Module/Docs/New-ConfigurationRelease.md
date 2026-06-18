---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationRelease
## SYNOPSIS
Creates repo-level release coordination settings for a module and package build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationRelease [-StageRoot <string>] [-VersionSource <ReleaseVersionSource>] [-Version <string>] [-PrimaryProject <string>] [-BuildOrder <string[]>] [-PublishOrder <string[]>] [<CommonParameters>]
```

## DESCRIPTION
Creates repo-level release coordination settings for a module and package build.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationRelease -BuildOrder @('Value')
```


## PARAMETERS

### -BuildOrder
Preferred build order for high-level release lanes.

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

### -PrimaryProject
Primary package/project used when the version source is package/project build.

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

### -PublishOrder
Preferred publish order for destinations such as NuGet, PowerShellGallery, and GitHub.

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

### -StageRoot
Staged release root where upload-ready assets should be copied.

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

### -Version
Explicit release version used when VersionSource is Manual.

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

### -VersionSource
Source used to resolve the coordinated release version.

```yaml
Type: ReleaseVersionSource
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Module, ProjectBuild, PackageBuild, Manual

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

- `PowerForge.ConfigurationReleaseSegment`

## RELATED LINKS

- None

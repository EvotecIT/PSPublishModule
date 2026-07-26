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
New-ConfigurationRelease [-StageRoot <string>] [-VersionSource <ReleaseVersionSource>] [-Version <string>] [-PrimaryProject <string>] [-SynchronizeModuleVersion] [-BuildOrder <string[]>] [-PublishOrder <string[]>] [<CommonParameters>]
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
Primary package/project used when the version source is a package/project build.
Required by SynchronizeModuleVersion to identify the package coordinated with module history.

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

### -SynchronizeModuleVersion
Coordinates the module and selected primary package on one version.
The next available module version becomes a floor for the primary package; a higher numeric package version still wins.
At the same numeric version, a stable X-pattern candidate does not erase the configured module prerelease;
explicit prerelease versions retain normal semantic-version ordering.
PrimaryProject is required, and exactly one selected lane must run before the module and use
UseAsReleaseVersionSource. Package groups using AlignPackageVersions are raised together.
Publish runs persist a credential-free checkpoint so a partial release resumes the exact versions
and skips destinations that already completed.

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

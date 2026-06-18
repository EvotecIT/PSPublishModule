---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProjectBuild
## SYNOPSIS
References an existing project.build.json package build from the module-build DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProjectBuild -ConfigPath <string> [-Name <string>] [-Enabled] [-BuildBeforeModule] [-UseAsReleaseVersionSource] [-ProvideLocalNuGetFeed] [<CommonParameters>]
```

## DESCRIPTION
Use this cmdlet inside Build-Module { } when package build details already live in a JSON file and the
module build should coordinate with that package lane.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProjectBuild -ConfigPath 'C:\Path'
```


## PARAMETERS

### -BuildBeforeModule
Whether package outputs must be produced before the module lane runs.

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

### -ConfigPath
Path to an existing project.build.json file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Enabled
Whether this project build lane is enabled. Defaults to true.

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
Optional friendly name for this package build lane.

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

### -ProvideLocalNuGetFeed
Whether package outputs should be exposed as a temporary local NuGet feed for the module lane.

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

### -UseAsReleaseVersionSource
Whether the resolved package version should be used as the unified release version source.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ConfigurationProjectBuildSegment`

## RELATED LINKS

- None

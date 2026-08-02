---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-PowerForgeReleaseConfig
## SYNOPSIS
Scaffolds a starter unified release.json configuration file.

## SYNTAX
### __AllParameterSets
```powershell
New-PowerForgeReleaseConfig [-ProjectRoot <string>] [-PackagesConfigPath <string>] [-DotNetPublishConfigPath <string>] [-OutputPath <string>] [-Configuration <string>] [-Force] [-NoSchema] [-SkipPackages] [-SkipTools] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Use this cmdlet to assemble a repository-level release config from the build configs the repo
already has, typically Build/project.build.json and
Build/powerforge.dotnetpublish.json.

## EXAMPLES

### EXAMPLE 1
```powershell
New-PowerForgeReleaseConfig -ProjectRoot '.' -PassThru
```


### EXAMPLE 2
```powershell
New-PowerForgeReleaseConfig -PackagesConfigPath '.\Build\project.build.json' -DotNetPublishConfigPath '.\Build\powerforge.dotnetpublish.json' -Force
```


## PARAMETERS

### -Configuration
Release configuration value written into the tool section.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Release, Debug

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DotNetPublishConfigPath
Optional path to an existing DotNet publish config file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Overwrite an existing config file.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: Overwrite
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NoSchema
Omits the $schema property from generated JSON.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputPath
Output config path (default: Build\release.json).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path, ConfigPath
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PackagesConfigPath
Optional path to an existing project-build config file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PassThru
Returns detailed scaffold metadata instead of only the config path.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProjectRoot
Project root used to resolve relative paths.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipPackages
Skips package config discovery.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipTools
Skips tool/app config discovery.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.String`
- `PowerForge.PowerForgeReleaseConfigScaffoldResult`

## RELATED LINKS

- None

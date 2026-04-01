---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ProjectReleaseConfig
## SYNOPSIS
Scaffolds a starter project release configuration file for PowerShell-authored project builds.

## SYNTAX
### __AllParameterSets
```powershell
New-ProjectReleaseConfig [-ProjectRoot <string>] [-ProjectPath <string>] [-Name <string>] [-TargetName <string>] [-Framework <string>] [-Runtimes <string[]>] [-Configuration <string>] [-OutputPath <string>] [-Force] [-Portable] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Scaffolds a starter project release configuration file for PowerShell-authored project builds.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ProjectReleaseConfig -ProjectRoot '.' -PassThru
```

## PARAMETERS

### -Configuration
Release configuration value written into the starter config.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Release, Debug

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
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
Accept wildcard characters: True
```

### -Framework
Optional framework override.

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
Optional release name override.

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

### -OutputPath
Output config path (default: Build\project.release.json).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path, ConfigPath
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
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
Accept wildcard characters: True
```

### -Portable
Configure the starter file to request a portable bundle by default.

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

### -ProjectPath
Optional path to a specific project file.

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
Accept wildcard characters: True
```

### -Runtimes
Optional runtime identifiers override.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Runtime
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TargetName
Optional target name override.

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

- `System.String
PowerForge.PowerForgeProjectConfigurationScaffoldResult`

## RELATED LINKS

- None


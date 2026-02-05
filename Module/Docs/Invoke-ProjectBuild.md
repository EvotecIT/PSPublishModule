---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-ProjectBuild
## SYNOPSIS
Executes a repository-wide .NET build/release pipeline from a JSON configuration.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-ProjectBuild -ConfigPath <string> [-UpdateVersions] [-Build] [-PublishNuget] [-PublishGitHub] [-Plan] [-PlanPath <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
The configuration file follows Schemas/project.build.schema.json in the PSPublishModule repository.
Use it to define discovery rules, versioning, staging, NuGet publishing, and GitHub release settings.

GitHub tag/release templates support tokens:
{Project}, {Version}, {PrimaryProject}, {PrimaryVersion}, {Repo},
{Repository}, {Date}, {UtcDate}.
When GitHub release mode is Single and multiple project versions are present, {Version} defaults to
the local date (yyyy.MM.dd) unless a primary project version is available.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-ProjectBuild -ConfigPath 'C:\Path'
```

## PARAMETERS

### -Build
Run build/pack step.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ConfigPath
Path to the JSON configuration file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Plan
Generate a plan only (no build/publish actions).

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PlanPath
Optional path to write the plan JSON file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PublishGitHub
Publish artifacts to GitHub.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PublishNuget
Publish packages to NuGet.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UpdateVersions
Run version update step.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

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

- `PSPublishModule.ProjectBuildResult`

## RELATED LINKS

- None


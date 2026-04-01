---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-PowerForgePluginPack
## SYNOPSIS
Packs plugin-related NuGet packages from a reusable PowerForge plugin catalog configuration.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-PowerForgePluginPack [-ConfigPath <string>] [-ProjectRoot <string>] [-Group <string[]>] [-Configuration <string>] [-OutputRoot <string>] [-NoBuild] [-IncludeSymbols] [-PackageVersion <string>] [-VersionSuffix <string>] [-Push] [-Source <string>] [-ApiKey <string>] [-Plan] [-ExitCode] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the PowerShell entry point for the same generic plugin package workflow used by
powerforge plugin pack. It selects package groups from the shared catalog, runs
dotnet pack, and can optionally push only the packages produced by the current run.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-PowerForgePluginPack -Plan
```

### EXAMPLE 2
```powershell
Invoke-PowerForgePluginPack -ConfigPath '.\Build\powerforge.plugins.json' -Group pack-public -Push -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY -ExitCode
```

## PARAMETERS

### -ApiKey
API key used with Push.

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

### -ConfigPath
Path to the plugin catalog configuration file. When omitted, the cmdlet searches the current
directory and its parents for standard PowerForge plugin config names.

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

### -Configuration
Optional configuration override.

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

### -ExitCode
Sets host exit code: 0 on success, 1 on failure.

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

### -Group
Optional package group filter. When omitted, all catalog entries are selected.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: Groups
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludeSymbols
Includes symbol packages in the pack output.

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

### -NoBuild
Skips the build step when running dotnet pack.

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

### -OutputRoot
Optional output root override for packed NuGet packages.

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

### -PackageVersion
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

### -Plan
Builds and emits the resolved package plan without executing dotnet pack.

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

### -ProjectRoot
Optional project root override for resolving plugin project paths.

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

### -Push
Pushes packages after a successful pack run.

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

### -Source
Package source URL or name used with Push.

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

### -VersionSuffix
Optional package version suffix override.

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

- `System.Object`

## RELATED LINKS

- None


---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-PowerForgePluginExport
## SYNOPSIS
Exports plugin folders from a reusable PowerForge plugin catalog configuration.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-PowerForgePluginExport [-ConfigPath <string>] [-ProjectRoot <string>] [-Group <string[]>] [-PreferredFramework <string>] [-Configuration <string>] [-OutputRoot <string>] [-KeepSymbols] [-Plan] [-ExitCode] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is the PowerShell entry point for the same generic plugin export engine used by
powerforge plugin export. It plans and optionally publishes plugin project outputs into
folder-based plugin bundles without hardcoding IntelligenceX-specific project lists or manifest
formats into the engine.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-PowerForgePluginExport -Plan
```


### EXAMPLE 2
```powershell
Invoke-PowerForgePluginExport -ConfigPath '.\Build\powerforge.plugins.json' -Group public -KeepSymbols -ExitCode
```


## PARAMETERS

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
Optional plugin group filter. When omitted, all catalog entries are selected.

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

### -KeepSymbols
Keeps symbol files in exported plugin folders.

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
Optional output root override for exported plugin folders.

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
Builds and emits the resolved export plan without executing dotnet publish.

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

### -PreferredFramework
Optional preferred framework override used when a catalog entry targets multiple frameworks.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Framework
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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None


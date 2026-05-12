---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProjectRelease
## SYNOPSIS
Creates release-level defaults for a PowerShell-authored project build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProjectRelease [-Configuration <string>] [-PublishToolGitHub] [-SkipRestore] [-SkipBuild] [-ToolOutput <ConfigurationProjectReleaseOutputType[]>] [-SkipToolOutput <ConfigurationProjectReleaseOutputType[]>] [<CommonParameters>]
```

## DESCRIPTION
Creates release-level defaults for a PowerShell-authored project build.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProjectRelease -Configuration 'Value'
```


## PARAMETERS

### -Configuration
Build configuration used by the generated release object.

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

### -PublishToolGitHub
Enables tool/app GitHub release publishing by default for this release object.

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

### -SkipBuild
Skips build operations by default for DotNetPublish-backed tool/app flows.

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

### -SkipRestore
Skips restore operations by default for DotNetPublish-backed tool/app flows.

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

### -SkipToolOutput
Optional default release output exclusion.

```yaml
Type: ConfigurationProjectReleaseOutputType[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Tool, Portable, Installer, Store

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ToolOutput
Optional default release output selection.

```yaml
Type: ConfigurationProjectReleaseOutputType[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Tool, Portable, Installer, Store

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

- `PowerForge.ConfigurationProjectRelease`

## RELATED LINKS

- None


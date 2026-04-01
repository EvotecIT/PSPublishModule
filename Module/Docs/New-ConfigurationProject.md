---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProject
## SYNOPSIS
Creates a PowerShell-first project/release object for the unified PowerForge release engine.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProject -Name <string> -Target <ConfigurationProjectTarget[]> [-ProjectRoot <string>] [-Release <ConfigurationProjectRelease>] [-Workspace <ConfigurationProjectWorkspace>] [-Signing <ConfigurationProjectSigning>] [-Output <ConfigurationProjectOutput>] [-Installer <ConfigurationProjectInstaller[]>] [<CommonParameters>]
```

## DESCRIPTION
Creates a PowerShell-first project/release object for the unified PowerForge release engine.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProject -Name 'Name' -Target @('Value')
```

## PARAMETERS

### -Installer
Optional installers.

```yaml
Type: ConfigurationProjectInstaller[]
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
Friendly project name.

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

### -Output
Optional output defaults.

```yaml
Type: ConfigurationProjectOutput
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
Optional project root used for resolving relative paths.

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

### -Release
Optional release-level defaults.

```yaml
Type: ConfigurationProjectRelease
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Signing
Optional signing defaults.

```yaml
Type: ConfigurationProjectSigning
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Target
Project targets.

```yaml
Type: ConfigurationProjectTarget[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Workspace
Optional workspace defaults.

```yaml
Type: ConfigurationProjectWorkspace
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

- `PowerForge.ConfigurationProject`

## RELATED LINKS

- None


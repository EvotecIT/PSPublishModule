---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Export-ConfigurationProject
## SYNOPSIS
Exports a PowerShell-authored project release object to JSON.

## SYNTAX
### __AllParameterSets
```powershell
Export-ConfigurationProject -Project <ConfigurationProject> -OutputPath <string> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Exports a PowerShell-authored project release object to JSON.

## EXAMPLES

### EXAMPLE 1
```powershell
Export-ConfigurationProject -Project $project -OutputPath '.\Build\project.release.json'
```

## PARAMETERS

### -Force
Overwrites an existing file.

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

### -OutputPath
Output JSON path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: Path
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Project
Project configuration object to export.

```yaml
Type: ConfigurationProject
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: True
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

- `System.String`

## RELATED LINKS

- None


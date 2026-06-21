---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationGate
## SYNOPSIS
Sets the high-level module pipeline mode for an F5-friendly build DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationGate [-Mode] <ConfigurationGateMode> [<CommonParameters>]
```

## DESCRIPTION
Sets the high-level module pipeline mode for an F5-friendly build DSL.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationGate -Mode Manifest
```


### EXAMPLE 2
```powershell
New-ConfigurationGate -Mode Build
```


## PARAMETERS

### -Mode
High-level run mode. Use Manifest for PSD1 refresh, Build for local build/package work, and Publish for release publishing.

```yaml
Type: ConfigurationGateMode
Parameter Sets: __AllParameterSets
Aliases: Type
Possible values: Manifest, Build, Publish

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.ConfigurationGateSegment`

## RELATED LINKS

- None

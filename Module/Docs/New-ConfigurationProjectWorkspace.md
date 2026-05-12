---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationProjectWorkspace
## SYNOPSIS
Creates workspace-validation defaults for a PowerShell-authored project build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationProjectWorkspace [-ConfigPath <string>] [-Profile <string>] [-EnableFeature <string[]>] [-DisableFeature <string[]>] [-SkipValidation] [<CommonParameters>]
```

## DESCRIPTION
Creates workspace-validation defaults for a PowerShell-authored project build.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationProjectWorkspace -ConfigPath 'C:\Path'
```


## PARAMETERS

### -ConfigPath
Optional workspace validation config path.

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

### -DisableFeature
Optional features to disable.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnableFeature
Optional features to enable.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Profile
Optional workspace validation profile.

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

### -SkipValidation
When set, disables workspace validation by default for this object.

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

- `PowerForge.ConfigurationProjectWorkspace`

## RELATED LINKS

- None


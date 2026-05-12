---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetMatrixRule
## SYNOPSIS
Creates a matrix include/exclude rule for DotNet publish DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetMatrixRule [-Targets <string[]>] [-Runtime <string>] [-Framework <string>] [-Style <string>] [<CommonParameters>]
```

## DESCRIPTION
Creates a matrix include/exclude rule for DotNet publish DSL.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetMatrixRule -Targets 'Service*' -Runtime 'win-*' -Framework 'net10.0*' -Style 'Portable*'
```


## PARAMETERS

### -Framework
Optional framework wildcard pattern.

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

### -Runtime
Optional runtime wildcard pattern.

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

### -Style
Optional style wildcard pattern.

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

### -Targets
Optional target name patterns.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishMatrixRule`

## RELATED LINKS

- None


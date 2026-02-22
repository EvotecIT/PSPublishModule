---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetMatrix
## SYNOPSIS
Creates matrix defaults and include/exclude filters for DotNet publish DSL.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetMatrix [-Runtimes <string[]>] [-Frameworks <string[]>] [-Styles <DotNetPublishStyle[]>] [-Include <DotNetPublishMatrixRule[]>] [-Exclude <DotNetPublishMatrixRule[]>] [<CommonParameters>]
```

## DESCRIPTION
Creates matrix defaults and include/exclude filters for DotNet publish DSL.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetMatrix -Runtimes 'win-x64','win-arm64' -Frameworks 'net10.0','net10.0-windows' -Styles PortableCompat,AotSpeed
```

## PARAMETERS

### -Exclude
Exclude rules.

```yaml
Type: DotNetPublishMatrixRule[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Frameworks
Default frameworks.

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

### -Include
Include rules.

```yaml
Type: DotNetPublishMatrixRule[]
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
Default runtimes.

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

### -Styles
Default styles.

```yaml
Type: DotNetPublishStyle[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Portable, PortableCompat, PortableSize, AotSpeed, AotSize

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

- `PowerForge.DotNetPublishMatrix`

## RELATED LINKS

- None


---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationDotNetStateRule
## SYNOPSIS
Creates a preserve/restore rule for DotNet publish state handling.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationDotNetStateRule -SourcePath <string> [-DestinationPath <string>] [-Overwrite <bool>] [<CommonParameters>]
```

## DESCRIPTION
Creates a preserve/restore rule for DotNet publish state handling.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationDotNetStateRule -SourcePath 'appsettings.json' -DestinationPath 'appsettings.json' -Overwrite
```


## PARAMETERS

### -DestinationPath
Destination path relative to publish output.

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

### -Overwrite
Overwrite destination during restore.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SourcePath
Source path relative to publish output.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishStateRule`

## RELATED LINKS

- None


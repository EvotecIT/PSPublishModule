---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/StartAutomating/Pipeworks
Import-PSData
schema: 2.0.0
---

# Export-PSData

## SYNOPSIS
Exports property bags into a data file

## SYNTAX

```
Export-PSData -InputObject <PSObject[]> [-DataFile] <String> [-Sort] [<CommonParameters>]
```

## DESCRIPTION
Exports property bags and the first level of any other object into a ps data file (.psd1)

## EXAMPLES

### EXAMPLE 1
```
Get-Web -Url http://www.youtube.com/watch?v=xPRC3EDR_GU -AsMicrodata -ItemType http://schema.org/VideoObject |
```

Export-PSData .\PipeworksQuickstart.video.psd1

## PARAMETERS

### -InputObject
The data that will be exported

```yaml
Type: PSObject[]
Parameter Sets: (All)
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -DataFile
The path to the data file

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Sort
{{ Fill Sort Description }}

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### System.IO.FileInfo
## NOTES

## RELATED LINKS

[https://github.com/StartAutomating/Pipeworks
Import-PSData](https://github.com/StartAutomating/Pipeworks
Import-PSData)


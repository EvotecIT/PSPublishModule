---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Import-ConfigurationProject
## SYNOPSIS
Imports a PowerShell-authored project release object from JSON.

## SYNTAX
### __AllParameterSets
```powershell
Import-ConfigurationProject -Path <string> [<CommonParameters>]
```

## DESCRIPTION
Imports a PowerShell-authored project release object from JSON.

## EXAMPLES

### EXAMPLE 1
```powershell
Import-ConfigurationProject -Path '.\Build\project.release.json'
```


## PARAMETERS

### -Path
Path to the JSON configuration file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ConfigPath, FilePath, JsonPath
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

- `PowerForge.ConfigurationProject`

## RELATED LINKS

- None


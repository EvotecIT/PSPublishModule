---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Import-ConfigurationDotNetPublish
## SYNOPSIS
Imports a typed .NET publish configuration for use in the publish DSL.

## SYNTAX
### __AllParameterSets
```powershell
Import-ConfigurationDotNetPublish [-Path] <string> [<CommonParameters>]
```

## DESCRIPTION
Relative project roots are anchored to the defining configuration file, so the resulting specification can be used from another working directory.

## EXAMPLES

### EXAMPLE 1
```powershell
$spec = Import-ConfigurationDotNetPublish -Path './Build/publish.json'; $spec.DotNet.Configuration = 'Debug'; Invoke-DotNetPublish -Settings { $spec }
```


## PARAMETERS

### -Path
Publish JSON or unified release JSON containing the tools configuration.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: ConfigPath
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `PowerForge.DotNetPublishSpec`

## RELATED LINKS

- None

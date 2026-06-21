---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ConfigurationBoolean
## SYNOPSIS
Resolves a boolean configuration value from an environment variable with a script-defined default.

## SYNTAX
### __AllParameterSets
```powershell
Get-ConfigurationBoolean [-Name] <string> [-Default <bool>] [<CommonParameters>]
```

## DESCRIPTION
This helper is intended for build DSL scripts that need environment overrides without repeating
[bool]::Parse boilerplate in every repository.

## EXAMPLES

### EXAMPLE 1
```powershell
New-ConfigurationBuild -RefreshPSD1Only:(Get-ConfigurationBoolean RefreshPSD1Only -Default $true)
```


## PARAMETERS

### -Default
Value returned when the environment variable is missing or blank.

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

### -Name
Environment variable name to read.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: VariableName
Possible values:

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

- `System.Boolean`

## RELATED LINKS

- None

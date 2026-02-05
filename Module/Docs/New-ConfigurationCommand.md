---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationCommand
## SYNOPSIS
Defines a command import configuration for the build pipeline.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationCommand [-ModuleName <string>] [-CommandName <string[]>] [<CommonParameters>]
```

## DESCRIPTION
Used by the build pipeline to declare which commands should be imported from an external module at build time.
This helps make build scripts deterministic and explicit about their dependencies.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationCommand -ModuleName 'Pester' -CommandName 'Invoke-Pester'
```

Declares a dependency on Invoke-Pester from the Pester module.

### EXAMPLE 2
```powershell
PS>New-ConfigurationCommand -ModuleName 'PSWriteColor' -CommandName 'Write-Color','Write-Text'
```

Declares multiple command references from the same module.

## PARAMETERS

### -CommandName
One or more command names to reference from the module.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ModuleName
Name of the module that contains the commands.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

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

- `System.Object`

## RELATED LINKS

- None


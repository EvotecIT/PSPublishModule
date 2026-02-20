---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationImportModule
## SYNOPSIS
Creates a configuration for importing PowerShell modules.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationImportModule [-ImportSelf] [-ImportRequiredModules] [<CommonParameters>]
```

## DESCRIPTION
Controls which modules are imported during a pipeline run (the module under build itself and/or its RequiredModules).
This is primarily used by test and documentation steps that execute PowerShell code.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationImportModule -ImportSelf -ImportRequiredModules
```

Ensures the pipeline imports the module and required dependencies before running tests or generating docs.

### EXAMPLE 2
```powershell
PS>New-ConfigurationImportModule -ImportSelf
```

## PARAMETERS

### -ImportRequiredModules
Indicates whether to import required modules from the manifest.

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

### -ImportSelf
Indicates whether to import the current module itself.

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

- `System.Object`

## RELATED LINKS

- None


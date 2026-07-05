---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Test-ManagedScriptFileInfo
## SYNOPSIS
Tests whether a script file contains readable PSResourceGet-compatible PSScriptInfo metadata.

## SYNTAX
### __AllParameterSets
```powershell
Test-ManagedScriptFileInfo [-Path] <string> [<CommonParameters>]
```

## DESCRIPTION
Tests whether a script file contains readable PSResourceGet-compatible PSScriptInfo metadata.

## EXAMPLES

### EXAMPLE 1
```powershell
Test-ManagedScriptFileInfo -Path 'C:\Path'
```


## PARAMETERS

### -Path
Path to the script file.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
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

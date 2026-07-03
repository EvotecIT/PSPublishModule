---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Add-BenchmarkEngine
## SYNOPSIS
Adds a benchmark engine.

## SYNTAX
### __AllParameterSets
```powershell
Add-BenchmarkEngine [-Name] <string> [-ScriptBlock] <scriptblock> [<CommonParameters>]
```

## DESCRIPTION
Adds a benchmark engine.

## EXAMPLES

### EXAMPLE 1
```powershell
Add-BenchmarkEngine -Name 'Name'
```


## PARAMETERS

### -Name
Engine name.

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

### -ScriptBlock
Engine declaration body.

```yaml
Type: ScriptBlock
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: 1
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

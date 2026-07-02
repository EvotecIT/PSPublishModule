---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Add-BenchmarkSkipRule
## SYNOPSIS
Adds a benchmark skip rule.

## SYNTAX
### __AllParameterSets
```powershell
Add-BenchmarkSkipRule [-ScriptBlock] <scriptblock> [<CommonParameters>]
```

## DESCRIPTION
Adds a benchmark skip rule.

## EXAMPLES

### EXAMPLE 1
```powershell
Add-BenchmarkSkipRule -ScriptBlock { }
```


## PARAMETERS

### -ScriptBlock
Skip rule block.

```yaml
Type: ScriptBlock
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

- `System.Object`

## RELATED LINKS

- None

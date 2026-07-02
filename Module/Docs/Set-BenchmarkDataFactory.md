---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Set-BenchmarkDataFactory
## SYNOPSIS
Sets the suite data factory block.

## SYNTAX
### __AllParameterSets
```powershell
Set-BenchmarkDataFactory [-ScriptBlock] <scriptblock> [<CommonParameters>]
```

## DESCRIPTION
Sets the suite data factory block.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-BenchmarkDataFactory -ScriptBlock { }
```


## PARAMETERS

### -ScriptBlock
Data factory block.

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

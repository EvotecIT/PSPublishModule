---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Add-BenchmarkCase
## SYNOPSIS
Adds one benchmark case.

## SYNTAX
### __AllParameterSets
```powershell
Add-BenchmarkCase [-Name] <string> [[-Values] <hashtable>] [<CommonParameters>]
```

## DESCRIPTION
Adds one benchmark case.

## EXAMPLES

### EXAMPLE 1
```powershell
Add-BenchmarkCase -Name 'Name'
```


## PARAMETERS

### -Name
Case name.

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

### -Values
Case values.

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
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

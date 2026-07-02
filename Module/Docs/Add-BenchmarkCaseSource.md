---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Add-BenchmarkCaseSource
## SYNOPSIS
Adds benchmark cases from a script block or evaluated objects.

## SYNTAX
### InputObject (Default)
```powershell
Add-BenchmarkCaseSource [-InputObject] <Object[]> [<CommonParameters>]
```

### ScriptBlock
```powershell
Add-BenchmarkCaseSource [-ScriptBlock] <scriptblock> [<CommonParameters>]
```

## DESCRIPTION
Adds benchmark cases from a script block or evaluated objects.

## EXAMPLES

### EXAMPLE 1
```powershell
Add-BenchmarkCaseSource -InputObject @('Value')
```


## PARAMETERS

### -InputObject
Already evaluated case objects.

```yaml
Type: Object[]
Parameter Sets: InputObject
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptBlock
Script block that emits case objects.

```yaml
Type: ScriptBlock
Parameter Sets: ScriptBlock
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

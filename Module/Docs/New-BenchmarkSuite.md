---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-BenchmarkSuite
## SYNOPSIS
Declares a PowerShell benchmark suite.

## SYNTAX
### __AllParameterSets
```powershell
New-BenchmarkSuite [-Name] <string> [-ScriptBlock] <scriptblock> [-OutputRoot <string>] [<CommonParameters>]
```

## DESCRIPTION
Declares a PowerShell benchmark suite.

## EXAMPLES

### EXAMPLE 1
```powershell
New-BenchmarkSuite -Name 'Name'
```


## PARAMETERS

### -Name
Suite name.

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

### -OutputRoot
Output root for benchmark artifacts.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: out
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ScriptBlock
Suite declaration body.

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

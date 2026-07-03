---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Assert-BenchmarkValue
## SYNOPSIS
Asserts a benchmark value condition.

## SYNTAX
### Equals (Default)
```powershell
Assert-BenchmarkValue [-Actual] <Object> [-Expected] <Object> [-Message <string>] [-PassThru] [<CommonParameters>]
```

### NotNull
```powershell
Assert-BenchmarkValue [-Actual] <Object> -NotNull [-Message <string>] [-PassThru] [<CommonParameters>]
```

## DESCRIPTION
Asserts a benchmark value condition.

## EXAMPLES

### EXAMPLE 1
```powershell
Assert-BenchmarkValue -Actual 'Value'
```


### EXAMPLE 2
```powershell
Assert-BenchmarkValue -NotNull
```


## PARAMETERS

### -Actual
Actual value.

```yaml
Type: Object
Parameter Sets: Equals, NotNull
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Expected
Expected value.

```yaml
Type: Object
Parameter Sets: Equals
Aliases: None
Possible values:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Message
Optional assertion message.

```yaml
Type: String
Parameter Sets: Equals, NotNull
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NotNull
Assert that the value is not null.

```yaml
Type: SwitchParameter
Parameter Sets: NotNull
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Emit the actual value when the assertion passes.

```yaml
Type: SwitchParameter
Parameter Sets: Equals, NotNull
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

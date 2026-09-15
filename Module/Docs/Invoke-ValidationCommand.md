---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-ValidationCommand
## SYNOPSIS
Runs a bounded validation process and returns its captured output.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-ValidationCommand -Command <ReleaseCommandValidation> [-ProjectRoot <string>] [-Variables <hashtable>] [<CommonParameters>]
```

## DESCRIPTION
Use this for product-specific probes that must inspect structured process output. PowerForge owns timeout, cancellation, argument quoting, and expected-exit checks.

## EXAMPLES

### EXAMPLE 1
```powershell
$result = Invoke-ValidationCommand -Command @{ FileName = './tool.exe'; Arguments = @('diagnostics', '--json'); OutputJsonKind = 'Object' }; $result.StdOut | ConvertFrom-Json
```


## PARAMETERS

### -Command
Executable, arguments, timeout, environment, and output expectations.

```yaml
Type: ReleaseCommandValidation
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -ProjectRoot
Root for relative paths. Defaults to the current PowerShell filesystem location.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Variables
Named substitutions for command arguments, environment, and paths.

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `PowerForge.ReleaseCommandValidation`

## OUTPUTS

- `PowerForge.ProcessRunResult`

## RELATED LINKS

- None

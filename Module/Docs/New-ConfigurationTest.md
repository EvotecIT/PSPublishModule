---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationTest
## SYNOPSIS
Configures running Pester tests as part of the build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationTest -TestsPath <string> [-Enable] [-Force] [<CommonParameters>]
```

## DESCRIPTION
Emits a test configuration segment that instructs the pipeline to run Pester tests after the module is merged/built.
Use this when you want builds to fail fast on test failures.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationTest -Enable -TestsPath 'Tests'
```

Runs tests from the Tests folder after the build/merge step.

### EXAMPLE 2
```powershell
PS>New-ConfigurationTest -Enable -TestsPath 'Tests' -Force
```

Useful in CI when you always want a fresh test run.

## PARAMETERS

### -Enable
Enable test execution in the build.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force running tests even if caching would skip them.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestsPath
Path to the folder containing Pester tests.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
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


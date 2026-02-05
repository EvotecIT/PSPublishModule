---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationModuleSkip
## SYNOPSIS
Provides a way to ignore certain commands or modules during build process and continue module building on errors.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationModuleSkip [-IgnoreModuleName <string[]>] [-IgnoreFunctionName <string[]>] [-Force] [-FailOnMissingCommands] [<CommonParameters>]
```

## DESCRIPTION
Useful for optional dependencies where you want builds to succeed even if a tool module is not available
(e.g. optional analyzers, formatters, helpers).

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationModuleSkip -IgnoreModuleName 'PSScriptAnalyzer' -Force
```

Prevents build failure when the module is not installed in the environment.

### EXAMPLE 2
```powershell
PS>New-ConfigurationModuleSkip -IgnoreModuleName 'Microsoft.PowerShell.Security' -IgnoreFunctionName 'Get-AuthenticodeSignature','Set-AuthenticodeSignature' -Force
```

## PARAMETERS

### -FailOnMissingCommands
Fail build when unresolved commands are detected during merge.

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
Force build process to continue even if the module or command is not available.

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

### -IgnoreFunctionName
Ignore function name(s). If the function is not available it will be ignored.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IgnoreModuleName
Ignore module name(s). If the module is not available it will be ignored.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

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


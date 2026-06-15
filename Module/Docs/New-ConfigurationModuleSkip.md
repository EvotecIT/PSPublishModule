---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationModuleSkip
## SYNOPSIS
Provides a way to ignore certain commands or modules during build-time dependency validation.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationModuleSkip [-IgnoreModuleName <string[]>] [-IgnoreFunctionName <string[]>] [-Force] [-FailOnMissingCommands] [<CommonParameters>]
```

## DESCRIPTION
Missing module-backed commands fail the build by default. Use this command for optional dependencies where you
want selected missing modules or commands to be ignored. -Force keeps the legacy broad opt-out behavior.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> New-ConfigurationModuleSkip -IgnoreModuleName 'PSScriptAnalyzer' -Force
```

Prevents build failure when the module is not installed in the environment.

### EXAMPLE 2
```powershell
PS> New-ConfigurationModuleSkip -IgnoreModuleName 'Microsoft.PowerShell.Security' -IgnoreFunctionName 'Get-AuthenticodeSignature','Set-AuthenticodeSignature' -Force
```


## PARAMETERS

### -FailOnMissingCommands
Fail build when unresolved commands are detected during merge. This is the default and is retained for compatibility.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force the build process to continue even if modules or commands are not available.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IgnoreFunctionName
Ignore command/function name(s) during missing-command validation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IgnoreModuleName
Ignore module name(s) during missing-command validation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
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

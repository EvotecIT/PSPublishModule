---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationModuleSkip

## SYNOPSIS
Provides a way to ignore certain commands or modules during build process and continue module building on errors.

## SYNTAX

```
New-ConfigurationModuleSkip [[-IgnoreModuleName] <String[]>] [[-IgnoreFunctionName] <String[]>] [-Force]
 [<CommonParameters>]
```

## DESCRIPTION
Provides a way to ignore certain commands or modules during build process and continue module building on errors.
During build if a build module can't find require module or command it will fail the build process to prevent incomplete module from being created.
This option allows to skip certain modules or commands and continue building the module.
This is useful for commands we know are not available on all systems, or we get them different way.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationModuleSkip -IgnoreFunctionName 'Invoke-Formatter', 'Find-Module' -IgnoreModuleName 'platyPS'
```

## PARAMETERS

### -IgnoreModuleName
Ignore module name or names.
If the module is not available on the system it will be ignored and build process will continue.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IgnoreFunctionName
Ignore function name or names.
If the function is not available in the module it will be ignored and build process will continue.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
This switch will force build process to continue even if the module or command is not available (aka you know what you are doing)

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
General notes

## RELATED LINKS

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationImportModule

## SYNOPSIS
Creates a configuration for importing PowerShell modules.

## SYNTAX

```
New-ConfigurationImportModule [-ImportSelf] [-ImportRequiredModules] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
This function generates a configuration object for importing PowerShell modules.
It allows specifying whether to import the current module itself and/or any required modules.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationImportModule -ImportSelf -ImportRequiredModules
```

## PARAMETERS

### -ImportSelf
Indicates whether to import the current module itself.

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

### -ImportRequiredModules
Indicates whether to import any required modules specified in the module manifest.

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

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
This function helps in creating a standardized import configuration for PowerShell modules.

## RELATED LINKS

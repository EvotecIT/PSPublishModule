---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationPlaceHolder
## SYNOPSIS
Helps define custom placeholders replacing content within a script or module during the build process.

## SYNTAX
### FindAndReplace (Default)
```powershell
New-ConfigurationPlaceHolder -Find <string> -Replace <string> [<CommonParameters>]
```

### CustomReplacement
```powershell
New-ConfigurationPlaceHolder -CustomReplacement <PlaceHolderReplacement[]> [<CommonParameters>]
```

## DESCRIPTION
Placeholders are applied during merge/packaging so you can inject build-time values (versions, build IDs, timestamps)
without hardcoding them into source files.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationPlaceHolder -Find '{{ModuleVersion}}' -Replace '1.2.3'
```

Replaces all occurrences of {{ModuleVersion}} in merged content.

### EXAMPLE 2
```powershell
PS>New-ConfigurationPlaceHolder -CustomReplacement @{ Find='{{Company}}'; Replace='Evotec' }, @{ Find='{{Year}}'; Replace='2025' }
```

Emits multiple placeholder replacement segments in one call.

## PARAMETERS

### -CustomReplacement
Custom placeholder replacements. Accepts legacy hashtable array (@{ Find='..'; Replace='..' }) or T:PowerForge.PlaceHolderReplacement[].

```yaml
Type: PlaceHolderReplacement[]
Parameter Sets: CustomReplacement
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Find
The string to find in the script or module content.

```yaml
Type: String
Parameter Sets: FindAndReplace
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Replace
The string to replace the Find string in the script or module content.

```yaml
Type: String
Parameter Sets: FindAndReplace
Aliases: None
Possible values: 

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


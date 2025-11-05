---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationPlaceHolder

## SYNOPSIS
Command helping define custom placeholders replacing content within a script or module during the build process.

## SYNTAX

### FindAndReplace (Default)
```
New-ConfigurationPlaceHolder -Find <String> -Replace <String> [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### CustomReplacement
```
New-ConfigurationPlaceHolder -CustomReplacement <IDictionary[]> [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Command helping define custom placeholders replacing content within a script or module during the build process.
It modifies only the content of the script or module (PSM1) and does not modify the sources.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationPlaceHolder -Find '{CustomName}' -Replace 'SpecialCase'
```

### EXAMPLE 2
```
New-ConfigurationPlaceHolder -CustomReplacement @(
    @{ Find = '{CustomName}'; Replace = 'SpecialCase' }
    @{ Find = '{CustomVersion}'; Replace = '1.0.0' }
)
```

## PARAMETERS

### -CustomReplacement
Hashtable array with custom placeholders to replace.
Each hashtable must contain two keys: Find and Replace.

```yaml
Type: IDictionary[]
Parameter Sets: CustomReplacement
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Find
The string to find in the script or module content.

```yaml
Type: String
Parameter Sets: FindAndReplace
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Replace
The string to replace the Find string in the script or module content.

```yaml
Type: String
Parameter Sets: FindAndReplace
Aliases:

Required: True
Position: Named
Default value: None
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
General notes

## RELATED LINKS

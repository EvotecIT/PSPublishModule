---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationDocumentation

## SYNOPSIS
Enables or disables creation of documentation from the module using PlatyPS

## SYNTAX

```
New-ConfigurationDocumentation [-Enable] [-StartClean] [-UpdateWhenNew] [-Path] <String> [-PathReadme] <String>
 [<CommonParameters>]
```

## DESCRIPTION
Enables or disables creation of documentation from the module using PlatyPS

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationDocumentation -Enable:$false -StartClean -UpdateWhenNew -PathReadme 'Docs\Readme.md' -Path 'Docs'
```

### EXAMPLE 2
```
New-ConfigurationDocumentation -Enable -PathReadme 'Docs\Readme.md' -Path 'Docs'
```

## PARAMETERS

### -Enable
Enables creation of documentation from the module.
If not specified, the documentation will not be created.

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

### -StartClean
Removes all files from the documentation folder before creating new documentation.
Otherwise the \`Update-MarkdownHelpModule\` will be used to update the documentation.

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

### -UpdateWhenNew
Updates the documentation right after running \`New-MarkdownHelp\` due to platyPS bugs.

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

### -Path
Path to the folder where documentation will be created.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PathReadme
Path to the readme file that will be used for the documentation.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 2
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

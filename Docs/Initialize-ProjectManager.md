---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Initialize-ProjectManager

## SYNOPSIS
Builds VSCode Project manager config from filesystem

## SYNTAX

```
Initialize-ProjectManager [-Path] <String> [-DisableSorting] [<CommonParameters>]
```

## DESCRIPTION
Builds VSCode Project manager config from filesystem

## EXAMPLES

### EXAMPLE 1
```
Initialize-ProjectManager -Path "C:\Support\GitHub"
```

### EXAMPLE 2
```
Initialize-ProjectManager -Path "C:\Support\GitHub" -DisableSorting
```

## PARAMETERS

### -Path
Path to where the projects are located

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

### -DisableSorting
Disables sorting of the projects by last modified date

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

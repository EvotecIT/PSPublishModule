---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Initialize-PortableScript

## SYNOPSIS
Produces a self-contained script by inlining missing helper function definitions.

## SYNTAX

```
Initialize-PortableScript [[-FilePath] <String>] [[-OutputPath] <String>] [[-ApprovedModules] <Array>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Analyzes the input script for function calls not present in the script itself, pulls helper
definitions from approved modules, and writes a combined output file that begins with those
helper definitions followed by the original script content.
Useful for portable delivery.

## EXAMPLES

### EXAMPLE 1
```
Initialize-PortableScript -FilePath .\Scripts\Do-Work.ps1 -OutputPath .\Artefacts\Do-Work.Portable.ps1 -ApprovedModules PSSharedGoods
Generates a portable script with helper functions inlined at the top.
```

## PARAMETERS

### -FilePath
Path to the source script to analyze and convert.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputPath
Destination path for the generated self-contained script.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ApprovedModules
Module names that are permitted sources for inlined helper functions.

```yaml
Type: Array
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
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
Output encoding is UTF8BOM on PS 7+, UTF8 on PS 5.1 for compatibility.

## RELATED LINKS

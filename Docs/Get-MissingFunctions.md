---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-MissingFunctions

## SYNOPSIS
Analyzes a script or scriptblock and reports functions it calls that are not present.

## SYNTAX

```
Get-MissingFunctions [[-FilePath] <String>] [[-Code] <ScriptBlock>] [[-Functions] <String[]>] [-Summary]
 [-SummaryWithCommands] [[-ApprovedModules] <Array>] [[-IgnoreFunctions] <Array>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Scans the provided file path or scriptblock, detects referenced commands, filters them down to
function calls, and returns a summary or the raw helper function definitions that can be inlined.
When -ApprovedModules is specified, helper definitions are only taken from those modules; otherwise
only the list is returned.
Use this to build self-contained scripts by discovering dependencies.

## EXAMPLES

### EXAMPLE 1
```
Get-MissingFunctions -FilePath .\Build\Manage-Module.ps1 -Summary
Returns a list of functions used by the script.
```

### EXAMPLE 2
```
$sb = { Invoke-ModuleBuild -ModuleName 'MyModule' }
Get-MissingFunctions -Code $sb -SummaryWithCommands -ApprovedModules 'PSSharedGoods','PSPublishModule'
Returns a hashtable with a summary and inlineable helper definitions sourced from approved modules.
```

## PARAMETERS

### -FilePath
Path to a script file to analyze for missing function dependencies.
Alias: Path.

```yaml
Type: String
Parameter Sets: (All)
Aliases: Path

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Code
ScriptBlock to analyze instead of a file.
Alias: ScriptBlock.

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases: ScriptBlock

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Functions
Known function names to treat as already available (exclude from missing list).

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Summary
Return only a flattened summary list of functions used (objects with Name/Source), not inlined definitions.

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

### -SummaryWithCommands
Return a hashtable with Summary (names), SummaryFiltered (objects), and Functions (inlineable text).

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

### -ApprovedModules
Module names that are allowed sources for pulling inline helper function definitions.

```yaml
Type: Array
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IgnoreFunctions
Function names to ignore when computing the missing set.

```yaml
Type: Array
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
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
Use with Initialize-PortableScript to emit a self-contained version of a script.

## RELATED LINKS

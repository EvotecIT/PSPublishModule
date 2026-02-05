---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-MissingFunctions
## SYNOPSIS
Analyzes a script or scriptblock and reports functions/commands it calls that are not present.

## SYNTAX
### File (Default)
```powershell
Get-MissingFunctions [-FilePath <string>] [-Functions <string[]>] [-Summary] [-SummaryWithCommands] [-ApprovedModules <string[]>] [-IgnoreFunctions <string[]>] [<CommonParameters>]
```

### Code
```powershell
Get-MissingFunctions [-Code <scriptblock>] [-Functions <string[]>] [-Summary] [-SummaryWithCommands] [-ApprovedModules <string[]>] [-IgnoreFunctions <string[]>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet parses PowerShell code and returns a list of referenced commands that look like missing local helpers.
It is useful when building “portable” scripts/modules where you want to detect (and optionally inline) helper functions.

When -ApprovedModules is specified, helper definitions are only accepted from those modules.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-MissingFunctions -FilePath '.\Build\Build-Module.ps1' -Summary
```

Returns a list of functions referenced by the script that are not part of the script itself.

### EXAMPLE 2
```powershell
PS>$sb = { Invoke-ModuleBuild -ModuleName 'MyModule' }; Get-MissingFunctions -Code $sb -SummaryWithCommands -ApprovedModules 'PSSharedGoods','PSPublishModule'
```

Returns a structured report that can include helper function bodies sourced from approved modules.

## PARAMETERS

### -ApprovedModules
Module names that are allowed sources for pulling inline helper function definitions.

```yaml
Type: String[]
Parameter Sets: File, Code
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Code
ScriptBlock to analyze instead of a file. Alias: ScriptBlock.

```yaml
Type: ScriptBlock
Parameter Sets: Code
Aliases: ScriptBlock

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -FilePath
Path to a script file to analyze for missing function dependencies. Alias: Path.

```yaml
Type: String
Parameter Sets: File
Aliases: Path

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Functions
Known function names to treat as already available (exclude from missing list).

```yaml
Type: String[]
Parameter Sets: File, Code
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IgnoreFunctions
Function names to ignore when computing the missing set.

```yaml
Type: String[]
Parameter Sets: File, Code
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Summary
Return only a flattened summary list of functions used (objects with Name/Source), not inlined definitions.

```yaml
Type: SwitchParameter
Parameter Sets: File, Code
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SummaryWithCommands
Return a typed report with Summary, SummaryFiltered, and Functions.

```yaml
Type: SwitchParameter
Parameter Sets: File, Code
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


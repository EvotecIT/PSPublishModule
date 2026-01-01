---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ProjectLineEnding
## SYNOPSIS
Analyzes line ending consistency across all files in a project directory.

## SYNTAX
### __AllParameterSets
```powershell
Get-ProjectLineEnding -Path <string> [-ProjectType <string>] [-CustomExtensions <string[]>] [-ExcludeDirectories <string[]>] [-GroupByLineEnding] [-ShowFiles] [-CheckMixed] [-ExportPath <string>] [-Internal] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is read-only: it reports CRLF/LF usage and can optionally detect mixed line endings.

To normalize line endings after analysis, use Convert-ProjectLineEnding.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType PowerShell
```

Returns a summary of line endings used across PowerShell-related files.

### EXAMPLE 2
```powershell
PS>Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType Mixed -CheckMixed -ShowFiles
```

Use this before enforcing a repository-wide LF/CRLF policy.

### EXAMPLE 3
```powershell
PS>Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType All -ExportPath 'C:\Reports\lineending-report.csv'
```

Creates a CSV report that can be shared or used in CI artifacts.

## PARAMETERS

### -CheckMixed
Additionally check for files with mixed line endings.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CustomExtensions
Custom file extensions to analyze when ProjectType is Custom (e.g., *.ps1, *.cs).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeDirectories
Directory names to exclude from analysis (e.g., .git, bin, obj).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExportPath
Export the detailed report to a CSV file at the specified path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -GroupByLineEnding
Group results by line ending type.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Internal
Internal mode: avoid host output; use verbose messages instead.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Path to the project directory to analyze.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectType
Type of project to analyze. Determines which file extensions are included.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowFiles
Include individual file details in the report.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
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


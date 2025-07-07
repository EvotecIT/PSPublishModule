---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-ProjectLineEnding

## SYNOPSIS
Analyzes line ending consistency across all files in a project directory.

## SYNTAX

```
Get-ProjectLineEnding [-Path] <String> [[-ProjectType] <String>] [[-CustomExtensions] <String[]>]
 [[-ExcludeDirectories] <String[]>] [-GroupByLineEnding] [-ShowFiles] [-CheckMixed] [[-ExportPath] <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Scans all relevant files in a project directory and provides a comprehensive report on line endings.
Identifies inconsistencies between CRLF (Windows), LF (Unix/Linux), and mixed line endings.
Helps ensure consistency across development environments and prevent Git issues.

## EXAMPLES

### EXAMPLE 1
```
Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType PowerShell
Analyze line ending consistency in a PowerShell project.
```

### EXAMPLE 2
```
Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType Mixed -CheckMixed -ShowFiles
Get detailed line ending report including mixed line ending detection.
```

### EXAMPLE 3
```
Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType All -ExportPath 'C:\Reports\lineending-report.csv'
Analyze all file types and export detailed report to CSV.
```

## PARAMETERS

### -Path
Path to the project directory to analyze.

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

### -ProjectType
Type of project to analyze.
Determines which file extensions are included.
Valid values: 'PowerShell', 'CSharp', 'Mixed', 'All', 'Custom'

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: Mixed
Accept pipeline input: False
Accept wildcard characters: False
```

### -CustomExtensions
Custom file extensions to analyze when ProjectType is 'Custom'.
Example: @('*.ps1', '*.psm1', '*.cs', '*.vb')

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

### -ExcludeDirectories
Directory names to exclude from analysis (e.g., '.git', 'bin', 'obj').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode')
Accept pipeline input: False
Accept wildcard characters: False
```

### -GroupByLineEnding
Group results by line ending type for easier analysis.

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

### -ShowFiles
Include individual file details in the report.

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

### -CheckMixed
Additionally check for files with mixed line endings (both CRLF and LF in same file).

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

### -ExportPath
Export the detailed report to a CSV file at the specified path.

```yaml
Type: String
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
Line ending types:
- CRLF: Windows style (\\\\r\\\\n)
- LF: Unix/Linux style (\\\\n)
- CR: Classic Mac style (\\\\r) - rarely used
- Mixed: File contains multiple line ending types
- None: Empty file or single line without line ending

## RELATED LINKS

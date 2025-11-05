---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-ProjectConsistency

## SYNOPSIS
Provides comprehensive analysis of encoding and line ending consistency across a project.

## SYNTAX

```
Get-ProjectConsistency [-Path] <String> [[-ProjectType] <String>] [[-CustomExtensions] <String[]>]
 [[-ExcludeDirectories] <String[]>] [[-RecommendedEncoding] <String>] [[-RecommendedLineEnding] <String>]
 [-ShowDetails] [[-ExportPath] <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Combines encoding and line ending analysis to provide a complete picture of file consistency
across a project.
Identifies issues and provides recommendations for standardization.
This is the main analysis function that should be run before any bulk conversions.

## EXAMPLES

### EXAMPLE 1
```
Get-ProjectConsistencyReport -Path 'C:\MyProject' -ProjectType PowerShell
Analyze consistency in a PowerShell project with UTF8BOM encoding (PS 5.1 compatible).
```

### EXAMPLE 2
```
Get-ProjectConsistencyReport -Path 'C:\MyProject' -ProjectType Mixed -RecommendedEncoding UTF8BOM -RecommendedLineEnding LF -ShowDetails
Analyze a mixed project with specific recommendations and detailed output.
```

### EXAMPLE 3
```
Get-ProjectConsistencyReport -Path 'C:\MyProject' -ProjectType CSharp -RecommendedEncoding UTF8 -ExportPath 'C:\Reports\consistency-report.csv'
Analyze a C# project (UTF8 without BOM is fine) with CSV export.
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

### -RecommendedEncoding
The encoding standard you want to achieve.
Default is 'UTF8BOM' for PowerShell projects (PS 5.1 compatibility), 'UTF8' for others.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: $(
            if ($ProjectType -eq 'PowerShell') { 'UTF8BOM' }
            elseif ($ProjectType -eq 'Mixed') { 'UTF8BOM' }  # Default to PowerShell-safe for mixed projects
            else { 'UTF8' }
        )
Accept pipeline input: False
Accept wildcard characters: False
```

### -RecommendedLineEnding
The line ending standard you want to achieve.
Default is 'CRLF' on Windows, 'LF' on Unix.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
Default value: $(if ($IsWindows) { 'CRLF' } else { 'LF' })
Accept pipeline input: False
Accept wildcard characters: False
```

### -ShowDetails
Include detailed file-by-file analysis in the output.

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
Position: 7
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
This function combines the analysis from Get-ProjectEncoding and Get-ProjectLineEnding
to provide a unified view of project file consistency.
Use this before running conversion functions.

Encoding Recommendations:
- PowerShell: UTF8BOM (required for PS 5.1 compatibility with special characters)
- C#: UTF8 (BOM not needed, Visual Studio handles UTF8 correctly)
- Mixed: UTF8BOM (safest for cross-platform PowerShell compatibility)

PowerShell 5.1 Compatibility:
UTF8 without BOM can cause PowerShell 5.1 to misinterpret files as ASCII, leading to:
- Broken special characters and accented letters
- Module import failures
- Incorrect string processing
UTF8BOM ensures proper encoding detection across all PowerShell versions.

## RELATED LINKS

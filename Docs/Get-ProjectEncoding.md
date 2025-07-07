---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-ProjectEncoding

## SYNOPSIS
Analyzes encoding consistency across all files in a project directory.

## SYNTAX

```
Get-ProjectEncoding [-Path] <String> [[-ProjectType] <String>] [[-CustomExtensions] <String[]>]
 [[-ExcludeDirectories] <String[]>] [-GroupByEncoding] [-ShowFiles] [[-ExportPath] <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Scans all relevant files in a project directory and provides a comprehensive report on file encodings.
Identifies inconsistencies, potential issues, and provides recommendations for standardization.
Useful for auditing projects before performing encoding conversions.

## EXAMPLES

### EXAMPLE 1
```
Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType PowerShell
Analyze encoding consistency in a PowerShell project.
```

### EXAMPLE 2
```
Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType Mixed -GroupByEncoding -ShowFiles
Get detailed encoding report grouped by encoding type with individual file listings.
```

### EXAMPLE 3
```
Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType All -ExportPath 'C:\Reports\encoding-report.csv'
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

### -GroupByEncoding
Group results by encoding type for easier analysis.

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
This function is read-only and does not modify any files.
Use Convert-ProjectEncoding to standardize encodings.

## RELATED LINKS

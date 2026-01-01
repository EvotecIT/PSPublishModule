---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ProjectEncoding
## SYNOPSIS
Analyzes encoding consistency across all files in a project directory.

## SYNTAX
### __AllParameterSets
```powershell
Get-ProjectEncoding -Path <string> [-ProjectType <string>] [-CustomExtensions <string[]>] [-ExcludeDirectories <string[]>] [-GroupByEncoding] [-ShowFiles] [-ExportPath <string>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is read-only: it does not modify files. Use it to audit a repository before converting encodings.

To standardize file encodings after analysis, use Convert-ProjectEncoding.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType PowerShell
```

Returns a summary of the encodings used in PowerShell-related files.

### EXAMPLE 2
```powershell
PS>Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType Mixed -GroupByEncoding -ShowFiles
```

Useful when you need to identify which files are outliers (e.g. ASCII vs UTF-8 BOM).

### EXAMPLE 3
```powershell
PS>Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType All -ExportPath 'C:\Reports\encoding-report.csv'
```

Creates a CSV report that can be shared or used in CI artifacts.

## PARAMETERS

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

### -GroupByEncoding
Group results by encoding type.

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


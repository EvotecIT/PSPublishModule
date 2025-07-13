---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-PowerShellCompatibility

## SYNOPSIS
Analyzes PowerShell files and folders to determine compatibility with PowerShell 5.1 and PowerShell 7.

## SYNTAX

```
Get-PowerShellCompatibility [-Path] <String> [-Recurse] [[-ExcludeDirectories] <String[]>] [-ShowDetails]
 [[-ExportPath] <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Scans PowerShell files to detect features, cmdlets, and patterns that are specific to PowerShell 5.1 (Windows PowerShell)
or PowerShell 7 (PowerShell Core).
Identifies potential compatibility issues and provides recommendations for cross-version support.

## EXAMPLES

### EXAMPLE 1
```
Get-PowerShellCompatibility -Path 'C:\MyModule'
Analyzes all PowerShell files in the specified directory for compatibility issues.
```

### EXAMPLE 2
```
Get-PowerShellCompatibility -Path 'C:\MyModule' -Recurse -ShowDetails
Recursively analyzes all PowerShell files with detailed compatibility information.
```

### EXAMPLE 3
```
Get-PowerShellCompatibility -Path 'C:\MyModule\MyScript.ps1' -ShowDetails
Analyzes a specific PowerShell file for compatibility issues.
```

### EXAMPLE 4
```
Get-PowerShellCompatibility -Path 'C:\MyModule' -ExportPath 'C:\Reports\compatibility.csv'
Analyzes compatibility and exports detailed results to a CSV file.
```

## PARAMETERS

### -Path
Path to the file or directory to analyze for PowerShell compatibility.

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

### -Recurse
When analyzing a directory, recursively analyze all subdirectories.

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

### -ExcludeDirectories
Directory names to exclude from analysis (e.g., '.git', 'bin', 'obj', 'Artefacts').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode', 'Artefacts', 'Ignore')
Accept pipeline input: False
Accept wildcard characters: False
```

### -ShowDetails
Include detailed analysis of each file with specific compatibility issues found.

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
This function identifies:
- PowerShell 5.1 specific features (Windows PowerShell Desktop edition)
- PowerShell 7 specific features (PowerShell Core)
- Encoding issues that affect cross-version compatibility
- .NET Framework vs .NET Core dependencies
- Edition-specific cmdlets and parameters
- Cross-platform compatibility concerns

PowerShell 5.1 typically requires:
- UTF8BOM encoding for proper character handling
- Windows-specific cmdlets and .NET Framework
- Desktop edition specific features

PowerShell 7 supports:
- UTF8 encoding (BOM optional)
- Cross-platform cmdlets and .NET Core/.NET 5+
- Enhanced language features and performance

## RELATED LINKS

[Get-ProjectConsistency](Get-ProjectConsistency.md)
[Get-ProjectEncoding](Get-ProjectEncoding.md)
[Convert-ProjectEncoding](Convert-ProjectEncoding.md)
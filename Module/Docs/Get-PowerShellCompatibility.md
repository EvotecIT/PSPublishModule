---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-PowerShellCompatibility
## SYNOPSIS
Analyzes PowerShell files and folders to determine compatibility with Windows PowerShell 5.1 and PowerShell 7+.

## SYNTAX
### __AllParameterSets
```powershell
Get-PowerShellCompatibility -Path <string> [-Recurse] [-ExcludeDirectories <string[]>] [-ShowDetails] [-ExportPath <string>] [-Internal] [<CommonParameters>]
```

## DESCRIPTION
Scans PowerShell files to detect patterns and constructs that can cause cross-version issues
(Windows PowerShell 5.1 vs PowerShell 7+), and outputs a compatibility report.

Use this as part of CI to keep modules compatible across editions, and pair it with encoding/line-ending checks
when supporting Windows PowerShell 5.1.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-PowerShellCompatibility -Path 'C:\MyModule'
```

Analyzes PowerShell files in the folder and returns a compatibility report.

### EXAMPLE 2
```powershell
PS>Get-PowerShellCompatibility -Path 'C:\MyModule' -Recurse -ShowDetails
```

Useful when investigating why a module behaves differently in PS 5.1 vs PS 7+.

### EXAMPLE 3
```powershell
PS>Get-PowerShellCompatibility -Path 'C:\MyModule' -ExportPath 'C:\Reports\compatibility.csv'
```

Creates a report that can be attached to CI artifacts.

## PARAMETERS

### -ExcludeDirectories
Directory names to exclude from analysis.

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

### -Internal
Internal mode used by build pipelines to suppress host output.

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
Path to the file or directory to analyze for PowerShell compatibility.

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

### -Recurse
When analyzing a directory, recursively analyze all subdirectories.

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

### -ShowDetails
Include detailed analysis of each file with specific compatibility issues found.

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


---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ProjectConsistency
## SYNOPSIS
Provides comprehensive analysis of encoding and line ending consistency across a project.

## SYNTAX
### __AllParameterSets
```powershell
Get-ProjectConsistency -Path <string> [-ProjectType <string>] [-CustomExtensions <string[]>] [-ExcludeDirectories <string[]>] [-RecommendedEncoding <TextEncodingKind>] [-RecommendedLineEnding <FileConsistencyLineEnding>] [-ShowDetails] [-ExportPath <string>] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet combines encoding and line-ending analysis to provide a single “consistency” view of your repository.
It is intended to be run before bulk conversions (encoding/line endings) and before packaging a module for release.

For fixing issues during builds, use New-ConfigurationFileConsistency with -AutoFix enabled.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-ProjectConsistency -Path 'C:\MyProject' -ProjectType PowerShell
```

Reports encoding and line ending consistency using PowerShell-friendly defaults.

### EXAMPLE 2
```powershell
PS>Get-ProjectConsistency -Path 'C:\MyProject' -ProjectType Mixed -RecommendedEncoding UTF8BOM -RecommendedLineEnding LF -ShowDetails
```

Useful when you want to enforce a policy (e.g. UTF-8 BOM for PS 5.1 compatibility and LF for cross-platform repos).

### EXAMPLE 3
```powershell
PS>Get-ProjectConsistency -Path 'C:\MyProject' -ProjectType CSharp -RecommendedEncoding UTF8 -ExportPath 'C:\Reports\consistency-report.csv'
```

Exports the per-file details so you can review issues outside the console.

## PARAMETERS

### -CustomExtensions
Custom file extensions to analyze when ProjectType is Custom (e.g., *.ps1, *.cs).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

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
Possible values: 

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
Possible values: 

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
Possible values: 

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
Possible values: PowerShell, CSharp, Mixed, All, Custom

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RecommendedEncoding
The encoding standard you want to achieve (optional).

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RecommendedLineEnding
The line ending standard you want to achieve (optional).

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowDetails
Include detailed file-by-file analysis in the output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: 

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


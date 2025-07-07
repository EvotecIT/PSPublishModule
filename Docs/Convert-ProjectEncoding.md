---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Convert-ProjectEncoding

## SYNOPSIS
Converts encoding for all source files in a project directory with comprehensive safety features.

## SYNTAX

### ProjectType (Default)
```
Convert-ProjectEncoding -Path <String> [-ProjectType <String>] [-SourceEncoding <String>]
 [-TargetEncoding <String>] [-ExcludeDirectories <String[]>] [-CreateBackups] [-BackupDirectory <String>]
 [-Force] [-NoRollbackOnMismatch] [-PassThru] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### Custom
```
Convert-ProjectEncoding -Path <String> -CustomExtensions <String[]> [-SourceEncoding <String>]
 [-TargetEncoding <String>] [-ExcludeDirectories <String[]>] [-CreateBackups] [-BackupDirectory <String>]
 [-Force] [-NoRollbackOnMismatch] [-PassThru] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## DESCRIPTION
Recursively converts encoding for PowerShell, C#, and other source code files in a project directory.
Includes comprehensive safety features: WhatIf support, automatic backups, rollback protection,
and detailed reporting.
Designed specifically for development projects with intelligent file type detection.

## EXAMPLES

### EXAMPLE 1
```
Convert-ProjectEncoding -Path 'C:\MyProject' -ProjectType PowerShell -WhatIf
Preview encoding conversion for a PowerShell project (will convert from ANY encoding to UTF8BOM by default).
```

### EXAMPLE 2
```
Convert-ProjectEncoding -Path 'C:\MyProject' -ProjectType PowerShell -TargetEncoding UTF8BOM
Convert ALL files in a PowerShell project to UTF8BOM regardless of their current encoding.
```

### EXAMPLE 3
```
Convert-ProjectEncoding -Path 'C:\MyProject' -ProjectType Mixed -SourceEncoding ASCII -TargetEncoding UTF8BOM -CreateBackups
Convert ONLY ASCII files in a mixed project to UTF8BOM with backups.
```

### EXAMPLE 4
```
Convert-ProjectEncoding -Path 'C:\MyProject' -ProjectType CSharp -TargetEncoding UTF8 -PassThru
Convert ALL files in a C# project to UTF8 without BOM and return detailed results.
```

## PARAMETERS

### -Path
Path to the project directory to process.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProjectType
Type of project to process.
Determines which file extensions are included.
Valid values: 'PowerShell', 'CSharp', 'Mixed', 'All', 'Custom'

```yaml
Type: String
Parameter Sets: ProjectType
Aliases:

Required: False
Position: Named
Default value: Mixed
Accept pipeline input: False
Accept wildcard characters: False
```

### -CustomExtensions
Custom file extensions to process when ProjectType is 'Custom'.
Example: @('*.ps1', '*.psm1', '*.cs', '*.vb')

```yaml
Type: String[]
Parameter Sets: Custom
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SourceEncoding
Expected source encoding of files.
When specified, only files with this encoding will be converted.
When not specified (or set to 'Any'), files with any encoding except the target encoding will be converted.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Any
Accept pipeline input: False
Accept wildcard characters: False
```

### -TargetEncoding
Target encoding for conversion.
Default is 'UTF8BOM' for PowerShell projects (PS 5.1 compatibility), 'UTF8' for others.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeDirectories
Directory names to exclude from processing (e.g., '.git', 'bin', 'obj').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode')
Accept pipeline input: False
Accept wildcard characters: False
```

### -CreateBackups
Create backup files before conversion for additional safety.

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

### -BackupDirectory
Directory to store backup files.
If not specified, backups are created alongside original files.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Convert files even when their detected encoding doesn't match SourceEncoding.

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

### -NoRollbackOnMismatch
Skip rolling back changes when content verification fails.

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

### -PassThru
Return detailed results for each processed file.

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

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
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
File type mappings:
- PowerShell: *.ps1, *.psm1, *.psd1, *.ps1xml
- CSharp: *.cs, *.csx, *.csproj, *.sln, *.config, *.json, *.xml
- Mixed: Combination of PowerShell and CSharp
- All: Common source code extensions including JS, Python, etc.

PowerShell Encoding Recommendations:
- UTF8BOM is recommended for PowerShell files to ensure PS 5.1 compatibility
- UTF8 without BOM can cause PS 5.1 to misinterpret files as ASCII
- This can lead to broken special characters and module loading issues
- UTF8BOM ensures proper encoding detection across all PowerShell versions

## RELATED LINKS

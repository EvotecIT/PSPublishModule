---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Convert-ProjectLineEnding

## SYNOPSIS
Converts line endings for all source files in a project directory with comprehensive safety features.

## SYNTAX

```
Convert-ProjectLineEnding [-Path] <String> [[-ProjectType] <String>] [[-CustomExtensions] <String[]>]
 [-TargetLineEnding] <String> [[-ExcludeDirectories] <String[]>] [-CreateBackups] [[-BackupDirectory] <String>]
 [-Force] [-OnlyMixed] [-EnsureFinalNewline] [-OnlyMissingNewline] [-PassThru]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Recursively converts line endings for PowerShell, C#, and other source code files in a project directory.
Includes comprehensive safety features: WhatIf support, automatic backups, rollback protection,
and detailed reporting.
Can convert between CRLF (Windows), LF (Unix/Linux), and fix mixed line endings.

## EXAMPLES

### EXAMPLE 1
```
Convert-ProjectLineEnding -Path 'C:\MyProject' -ProjectType PowerShell -TargetLineEnding CRLF -WhatIf
Preview what files would be converted to Windows-style line endings.
```

### EXAMPLE 2
```
Convert-ProjectLineEnding -Path 'C:\MyProject' -ProjectType Mixed -TargetLineEnding LF -CreateBackups
Convert a mixed project to Unix-style line endings with backups.
```

### EXAMPLE 3
```
Convert-ProjectLineEnding -Path 'C:\MyProject' -ProjectType All -OnlyMixed -PassThru
Fix only files with mixed line endings and return detailed results.
```

## PARAMETERS

### -Path
Path to the project directory to process.

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
Type of project to process.
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
Custom file extensions to process when ProjectType is 'Custom'.
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

### -TargetLineEnding
Target line ending style.
Valid values: 'CRLF', 'LF'

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 4
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
Position: 5
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
Position: 6
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Convert all files regardless of current line ending type.

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

### -OnlyMixed
{{ Fill OnlyMixed Description }}

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

### -EnsureFinalNewline
Ensure all files end with a newline character (POSIX compliance).

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

### -OnlyMissingNewline
Only process files that are missing final newlines, leave others unchanged.

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
This function modifies files in place.
Always use -WhatIf first or -CreateBackups for safety.
Line ending types:
- CRLF: Windows style (\\\\r\\\\n)
- LF: Unix/Linux style (\\\\n)

## RELATED LINKS

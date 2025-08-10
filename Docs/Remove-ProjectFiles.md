---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Remove-ProjectFiles

## SYNOPSIS
Removes specific files and folders from a project directory with comprehensive safety features.

## SYNTAX

### ProjectType (Default)
```
Remove-ProjectFiles -ProjectPath <String> [-ProjectType <String>] [-ExcludePatterns <String[]>]
 [-ExcludeDirectories <String[]>] [-DeleteMethod <String>] [-CreateBackups] [-BackupDirectory <String>]
 [-Retries <Int32>] [-Recurse] [-MaxDepth <Int32>] [-ShowProgress] [-PassThru] [-Internal]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Custom
```
Remove-ProjectFiles -ProjectPath <String> -IncludePatterns <String[]> [-ExcludePatterns <String[]>]
 [-ExcludeDirectories <String[]>] [-DeleteMethod <String>] [-CreateBackups] [-BackupDirectory <String>]
 [-Retries <Int32>] [-Recurse] [-MaxDepth <Int32>] [-ShowProgress] [-PassThru] [-Internal]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Recursively removes files and folders matching specified patterns from a project directory.
Includes comprehensive safety features: WhatIf support, automatic backups, detailed reporting,
and multiple deletion methods.
Designed for cleaning up build artifacts, temporary files,
logs, and other unwanted files from development projects.

## EXAMPLES

### EXAMPLE 1
```
Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType Build -WhatIf
Preview what build artifacts would be removed from the project.
```

### EXAMPLE 2
```
Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType Html -DeleteMethod DotNetDelete -ShowProgress
Remove all HTML files using .NET deletion method with progress display.
```

### EXAMPLE 3
```
Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType Custom -IncludePatterns @('*.log', 'temp*', 'bin', 'obj') -CreateBackups
Custom cleanup of log files and build directories with backup creation.
```

### EXAMPLE 4
```
Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType All -ExcludePatterns @('*.config') -DeleteMethod RecycleBin
Remove all cleanup targets except config files, moving them to Recycle Bin.
```

## PARAMETERS

### -ProjectPath
Path to the project directory to clean.

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
Type of project cleanup to perform.
Determines default patterns and behaviors.
Valid values: 'Build', 'Logs', 'Html', 'Temp', 'All', 'Custom'

```yaml
Type: String
Parameter Sets: ProjectType
Aliases:

Required: False
Position: Named
Default value: Build
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludePatterns
File/folder patterns to include for deletion when ProjectType is 'Custom'.
Example: @('*.html', '*.log', 'bin', 'obj', 'temp*')

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

### -ExcludePatterns
File/folder patterns to exclude from deletion.
Example: @('*.config', 'packages.config')

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeDirectories
Directory names to completely exclude from processing (e.g., '.git', '.vs').

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: @('.git', '.svn', '.hg', 'node_modules')
Accept pipeline input: False
Accept wildcard characters: False
```

### -DeleteMethod
Method to use for file deletion.
Valid values: 'RemoveItem', 'DotNetDelete', 'RecycleBin'
- RemoveItem: Standard Remove-Item cmdlet
- DotNetDelete: Uses .NET Delete() method for cloud file issues
- RecycleBin: Moves files to Recycle Bin

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: RemoveItem
Accept pipeline input: False
Accept wildcard characters: False
```

### -CreateBackups
Create backup files before deletion for additional safety.

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
If not specified, backups are created in a temp location.

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

### -Retries
Number of retry attempts for file deletion.
Default is 3.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: 3
Accept pipeline input: False
Accept wildcard characters: False
```

### -Recurse
Process subdirectories recursively.
Default is true for most cleanup types.

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

### -MaxDepth
Maximum recursion depth.
Default is unlimited (-1).

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: -1
Accept pipeline input: False
Accept wildcard characters: False
```

### -ShowProgress
Display progress information during cleanup.

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
Return detailed results for each processed file/folder.

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

### -Internal
Suppress console output and use verbose/warning streams instead.

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
Cleanup type mappings:
- Build: bin, obj, packages, .vs, .vscode, TestResults, BenchmarkDotNet.Artifacts
- Logs: *.log, *.tmp, *.temp, logs folder
- Html: *.html, *.htm (except in Assets, Docs, Examples folders)
- Temp: temp*, tmp*, cache*, *.cache, *.tmp
- All: Combination of all above types

Safety Features:
- WhatIf support for preview
- Backup creation before deletion
- Multiple deletion methods with retry logic
- Comprehensive error handling
- Detailed reporting

## RELATED LINKS

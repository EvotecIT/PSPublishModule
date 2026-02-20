---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Remove-ProjectFiles
## SYNOPSIS
Removes specific files and folders from a project directory with safety features.

## SYNTAX
### ProjectType (Default)
```powershell
Remove-ProjectFiles -ProjectPath <string> [-ProjectType <ProjectCleanupType>] [-ExcludePatterns <string[]>] [-ExcludeDirectories <string[]>] [-DeleteMethod <ProjectDeleteMethod>] [-CreateBackups] [-BackupDirectory <string>] [-Retries <int>] [-Recurse] [-MaxDepth <int>] [-ShowProgress] [-PassThru] [-Internal] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Custom
```powershell
Remove-ProjectFiles -ProjectPath <string> -IncludePatterns <string[]> [-ExcludePatterns <string[]>] [-ExcludeDirectories <string[]>] [-DeleteMethod <ProjectDeleteMethod>] [-CreateBackups] [-BackupDirectory <string>] [-Retries <int>] [-Recurse] [-MaxDepth <int>] [-ShowProgress] [-PassThru] [-Internal] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Designed for build/CI cleanup scenarios where removing generated artifacts (bin/obj, packed outputs, temporary files)
should be predictable and safe. Supports -WhatIf, retries and optional backups.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Remove-ProjectFiles -ProjectPath '.' -ProjectType Build -WhatIf
```

Shows what would be removed for the selected cleanup type.

### EXAMPLE 2
```powershell
PS>Remove-ProjectFiles -ProjectPath '.' -IncludePatterns 'bin','obj','*.nupkg' -CreateBackups -BackupDirectory 'C:\Backups\MyRepo'
```

Creates backups before deletion and stores them under the backup directory.

## PARAMETERS

### -BackupDirectory
Directory where backups should be stored (optional).

```yaml
Type: String
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CreateBackups
Create backup copies of items before deletion.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -DeleteMethod
Method to use for deletion.

```yaml
Type: ProjectDeleteMethod
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: RemoveItem, DotNetDelete, RecycleBin

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeDirectories
Directory names to completely exclude from processing.

```yaml
Type: String[]
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludePatterns
Patterns to exclude from deletion.

```yaml
Type: String[]
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -IncludePatterns
File/folder patterns to include for deletion when using the Custom parameter set.

```yaml
Type: String[]
Parameter Sets: Custom
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Internal
Suppress console output and use verbose/warning streams instead.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaxDepth
Maximum recursion depth. Default is unlimited (-1).

```yaml
Type: Int32
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Return detailed results.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectPath
Path to the project directory to clean.

```yaml
Type: String
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectType
Type of project cleanup to perform.

```yaml
Type: ProjectCleanupType
Parameter Sets: ProjectType
Aliases: None
Possible values: Build, Logs, Html, Temp, All

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Recurse
Process subdirectories recursively. Defaults to true unless explicitly specified.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Retries
Number of retry attempts for each deletion.

```yaml
Type: Int32
Parameter Sets: ProjectType, Custom
Aliases: None
Possible values: 

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowProgress
Display progress information during cleanup.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
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


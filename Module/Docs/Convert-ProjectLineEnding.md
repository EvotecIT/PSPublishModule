---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Convert-ProjectLineEnding
## SYNOPSIS
Converts line endings across a project (CRLF/LF), with options for mixed-only fixes and final newline enforcement.
Thin wrapper over PowerForge.LineEndingConverter.

## SYNTAX
### ProjectType (Default)
```powershell
Convert-ProjectLineEnding -Path <string> -TargetLineEnding <LineEnding> [-ProjectType <ProjectKind>] [-ExcludeDirectories <string[]>] [-CreateBackups] [-BackupDirectory <string>] [-Force] [-OnlyMixed] [-EnsureFinalNewline] [-OnlyMissingNewline] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Custom
```powershell
Convert-ProjectLineEnding -Path <string> -TargetLineEnding <LineEnding> [-ProjectType <ProjectKind>] [-CustomExtensions <string[]>] [-ExcludeDirectories <string[]>] [-CreateBackups] [-BackupDirectory <string>] [-Force] [-OnlyMixed] [-EnsureFinalNewline] [-OnlyMissingNewline] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Use -WhatIf to preview changes without modifying files. When conversion is performed, PowerForge preserves file encoding where possible
and prefers UTF-8 BOM for PowerShell files to maintain Windows PowerShell 5.1 compatibility.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Convert-ProjectLineEnding -Path C:\Repo -ProjectType PowerShell -TargetLineEnding CRLF -WhatIf
```

Shows which files would be normalized to Windows-style line endings.

### EXAMPLE 2
```powershell
PS>Convert-ProjectLineEnding -Path . -ProjectType Mixed -TargetLineEnding LF -OnlyMixed -CreateBackups -BackupDirectory C:\Backups\Repo
```

Converts only files that contain both CRLF and LF, backing up originals first.

### EXAMPLE 3
```powershell
PS>Convert-ProjectLineEnding -Path . -ProjectType All -TargetLineEnding CRLF -OnlyMissingNewline -EnsureFinalNewline
```

Appends a final CRLF to files missing it without changing other files.

## PARAMETERS

### -BackupDirectory
Root folder for mirrored backups; when null, .bak is used next to files.

```yaml
Type: String
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CreateBackups
Create backups prior to conversion.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -CustomExtensions
Custom extension patterns (e.g., *.ps1,*.psm1) when overriding defaults.

```yaml
Type: String[]
Parameter Sets: Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnsureFinalNewline
Ensure a final newline exists at the end of file.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeDirectories
Directory names to exclude from traversal.

```yaml
Type: String[]
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force conversion even if file appears to already match the target style.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OnlyMissingNewline
Only modify files that are missing the final newline.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -OnlyMixed
Convert only files that contain mixed line endings.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Emit per-file results instead of a summary.

```yaml
Type: SwitchParameter
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Path to the project directory to process.

```yaml
Type: String
Parameter Sets: ProjectType, Custom
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectType
Project type used to derive default include patterns.

```yaml
Type: ProjectKind
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TargetLineEnding
Target line ending style to enforce.

```yaml
Type: LineEnding
Parameter Sets: ProjectType, Custom
Aliases: None

Required: True
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


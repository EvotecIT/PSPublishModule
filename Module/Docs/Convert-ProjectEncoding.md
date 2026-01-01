---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Convert-ProjectEncoding
## SYNOPSIS
Converts file encodings across a project. Thin wrapper over PowerForge.EncodingConverter.
Defaults to UTF-8 with BOM for PowerShell file types to ensure PS 5.1 compatibility.

## SYNTAX
### ProjectType (Default)
```powershell
Convert-ProjectEncoding -Path <string> [-ProjectType <ProjectKind>] [-SourceEncoding <TextEncodingKind>] [-TargetEncoding <TextEncodingKind>] [-ExcludeDirectories <string[]>] [-CreateBackups] [-BackupDirectory <string>] [-Force] [-NoRollbackOnMismatch] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### Custom
```powershell
Convert-ProjectEncoding -Path <string> -CustomExtensions <string[]> [-SourceEncoding <TextEncodingKind>] [-TargetEncoding <TextEncodingKind>] [-ExcludeDirectories <string[]>] [-CreateBackups] [-BackupDirectory <string>] [-Force] [-NoRollbackOnMismatch] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Converts file encodings across a project. Thin wrapper over PowerForge.EncodingConverter.
Defaults to UTF-8 with BOM for PowerShell file types to ensure PS 5.1 compatibility.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Convert-ProjectEncoding -Path C:\MyProject -ProjectType PowerShell -WhatIf
```

Shows what would be converted. PowerShell files default to UTF-8 with BOM.

### EXAMPLE 2
```powershell
PS>Convert-ProjectEncoding -Path C:\Repo -ProjectType Mixed -SourceEncoding ASCII -TargetEncoding UTF8BOM -CreateBackups -BackupDirectory C:\Backups\Repo
```

Only files detected as ASCII are converted; backups are mirrored under C:\Backups\Repo.

### EXAMPLE 3
```powershell
PS>Convert-ProjectEncoding -Path . -CustomExtensions *.ps1,*.psm1 -TargetEncoding UTF8BOM -PassThru
```

Processes only PowerShell scripts using custom patterns and outputs a detailed result object.

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

Required: True
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
Force conversion even if detection does not match SourceEncoding.

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

### -NoRollbackOnMismatch
Do not rollback from backup if verification mismatch occurs.

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
Parameter Sets: ProjectType
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -SourceEncoding
Expected source encoding; when Any, any non-target encoding may be converted.

```yaml
Type: TextEncodingKind
Parameter Sets: ProjectType, Custom
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TargetEncoding
Explicit target encoding; when null, defaults are chosen based on file type.

```yaml
Type: Nullable`1
Parameter Sets: ProjectType, Custom
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


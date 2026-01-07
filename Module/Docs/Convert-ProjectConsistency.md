---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Convert-ProjectConsistency
## SYNOPSIS
Converts a project to a consistent encoding/line ending policy and reports the results.

## SYNTAX
### __AllParameterSets
```powershell
Convert-ProjectConsistency -Path <string> [-ProjectType <string>] [-CustomExtensions <string[]>] [-ExcludeDirectories <string[]>] [-ExcludeFiles <string[]>] [-RequiredEncoding <FileConsistencyEncoding>] [-RequiredLineEnding <FileConsistencyLineEnding>] [-SourceEncoding <TextEncodingKind>] [-FixEncoding] [-FixLineEndings] [-EncodingOverrides <IDictionary>] [-LineEndingOverrides <IDictionary>] [-CreateBackups] [-BackupDirectory <string>] [-Force] [-NoRollbackOnMismatch] [-OnlyMixedLineEndings] [-EnsureFinalNewline] [-OnlyMissingFinalNewline] [-ShowDetails] [-ExportPath <string>] [<CommonParameters>]
```

## DESCRIPTION
Convert-ProjectConsistency applies a consistency policy across a project tree by normalizing
encoding and/or line endings to the specified targets. It reports a post-conversion summary
and optionally exports a detailed CSV report for auditing.

For build-time enforcement, use New-ConfigurationFileConsistency with -AutoFix so the pipeline
applies the same rules automatically.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Convert-ProjectConsistency -Path 'C:\MyProject' -ProjectType PowerShell -CreateBackups
```

Ensures PowerShell-friendly encoding (UTF-8 BOM) and CRLF line endings, creating backups before changes.

### EXAMPLE 2
```powershell
PS>Convert-ProjectConsistency -Path 'C:\MyProject' -FixLineEndings -RequiredLineEnding LF -ExcludeDirectories 'Build','Docs'
```

Normalizes line endings to LF only, skipping non-source folders.

### EXAMPLE 3
```powershell
PS>Convert-ProjectConsistency -Path 'C:\MyProject' -FixEncoding -RequiredEncoding UTF8BOM -EncodingOverrides @{ '*.xml' = 'UTF8' } -ExportPath 'C:\Reports\consistency.csv'
```

Uses UTF-8 BOM by default but keeps XML files UTF-8 without BOM, and writes a report to disk.

## PARAMETERS

### -BackupDirectory
Backup root folder (mirrors the project structure).

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

### -CreateBackups
Create backup files before modifying content.

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

### -CustomExtensions
Custom file extensions to include when ProjectType is Custom (e.g., *.ps1, *.cs).

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

### -EncodingOverrides
Per-path encoding overrides (hashtable of pattern => encoding).

```yaml
Type: IDictionary
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -EnsureFinalNewline
Ensure a final newline exists after line ending conversion.

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

### -ExcludeDirectories
Directory names to exclude from conversion (e.g., .git, bin, obj).

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

### -ExcludeFiles
File patterns to exclude from conversion.

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

### -FixEncoding
Convert encoding inconsistencies.

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

### -FixLineEndings
Convert line ending inconsistencies.

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

### -Force
Force conversion even when the file already matches the target.

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

### -LineEndingOverrides
Per-path line ending overrides (hashtable of pattern => line ending).

```yaml
Type: IDictionary
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -NoRollbackOnMismatch
Do not rollback from backup if verification mismatch occurs during encoding conversion.

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

### -OnlyMissingFinalNewline
Only fix files missing the final newline.

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

### -OnlyMixedLineEndings
Only convert files that have mixed line endings.

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
Path to the project directory to convert.

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

### -RequiredEncoding
Target encoding to enforce when fixing encoding consistency.

```yaml
Type: FileConsistencyEncoding
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: UTF8BOM
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredLineEnding
Target line ending style to enforce when fixing line endings.

```yaml
Type: FileConsistencyLineEnding
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: CRLF
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowDetails
Include detailed file-by-file analysis in the output.

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

### -SourceEncoding
Source encoding filter. When Any, any non-target encoding may be converted.

```yaml
Type: TextEncodingKind
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: Any
Accept pipeline input: False
Accept wildcard characters: True
```

## NOTES

## RELATED LINKS

---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationFileConsistency
## SYNOPSIS
Creates configuration for file consistency checking (encoding and line endings) during module build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationFileConsistency [-Enable] [-FailOnInconsistency] [-Severity <ValidationSeverity>] [-RequiredEncoding <FileConsistencyEncoding>] [-RequiredLineEnding <FileConsistencyLineEnding>] [-ProjectKind <ProjectKind>] [-IncludePatterns <string[]>] [-Scope <FileConsistencyScope>] [-AutoFix] [-CreateBackups] [-MaxInconsistencyPercentage <int>] [-ExcludeDirectories <string[]>] [-ExcludeFiles <string[]>] [-EncodingOverrides <hashtable>] [-LineEndingOverrides <hashtable>] [-ExportReport] [-ReportFileName <string>] [-CheckMixedLineEndings] [-CheckMissingFinalNewline] [-UpdateProjectRoot] [<CommonParameters>]
```

## DESCRIPTION
Adds a file-consistency validation step to the pipeline. This can enforce required encoding/line-ending rules
and (optionally) auto-fix issues during a build.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationFileConsistency -Enable -FailOnInconsistency -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF -AutoFix -CreateBackups -ExportReport
```

Enforces consistency and exports a CSV report; backups are created before fixes are applied.

### EXAMPLE 2
```powershell
PS>New-ConfigurationFileConsistency -Enable -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF -ExportReport -Scope StagingAndProject
```

Runs validation on staging and project root, exports a report, and does not apply automatic fixes.

## PARAMETERS

### -AutoFix
Automatically fix encoding and line ending issues during build.

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

### -CheckMissingFinalNewline
Check for files missing final newlines.

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

### -CheckMixedLineEndings
Check for files with mixed line endings.

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

### -CreateBackups
Create backup files before applying automatic fixes.

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

### -Enable
Enable file consistency checking during build.

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

### -EncodingOverrides
Per-path encoding overrides (patterns mapped to encodings).

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ExcludeDirectories
Directory names to exclude from consistency analysis.

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
File patterns to exclude from consistency analysis.

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

### -ExportReport
Export detailed consistency report to the artifacts directory.

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

### -FailOnInconsistency
Fail the build if consistency issues are found.

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

### -IncludePatterns
Custom include patterns (override default project kind patterns).

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

### -LineEndingOverrides
Per-path line ending overrides (patterns mapped to line endings).

```yaml
Type: Hashtable
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -MaxInconsistencyPercentage
Maximum percentage of files that can have consistency issues. Default is 5.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectKind
Project kind used to derive default include patterns.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ReportFileName
Custom filename for the consistency report.

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
Required file encoding.

```yaml
Type: FileConsistencyEncoding
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -RequiredLineEnding
Required line ending style.

```yaml
Type: FileConsistencyLineEnding
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Scope
Scope for file consistency checks (staging/project).

```yaml
Type: FileConsistencyScope
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Severity
Severity for consistency issues (overrides FailOnInconsistency when specified).

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -UpdateProjectRoot
Legacy switch. When set, applies encoding/line-ending consistency fixes to the project root as well as staging output.

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


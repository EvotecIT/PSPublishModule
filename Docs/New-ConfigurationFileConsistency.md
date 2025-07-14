---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationFileConsistency

## SYNOPSIS
Creates configuration for file consistency checking (encoding and line endings) during module build.

## SYNTAX

```
New-ConfigurationFileConsistency [-Enable] [-FailOnInconsistency] [[-RequiredEncoding] <String>]
 [[-RequiredLineEnding] <String>] [-AutoFix] [-CreateBackups] [[-MaxInconsistencyPercentage] <Int32>]
 [[-ExcludeDirectories] <String[]>] [-ExportReport] [[-ReportFileName] <String>] [-CheckMixedLineEndings]
 [-CheckMissingFinalNewline] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Configures file encoding and line ending consistency analysis to be performed during the module build process.
Can enforce consistency requirements and fail the build if issues are found.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationFileConsistency -Enable -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF
Enable consistency checking with specific encoding and line ending requirements.
```

### EXAMPLE 2
```
New-ConfigurationFileConsistency -Enable -AutoFix -CreateBackups
Enable checking with automatic fixing and backup creation.
```

### EXAMPLE 3
```
New-ConfigurationFileConsistency -Enable -FailOnInconsistency -MaxInconsistencyPercentage 10
Enable checking and fail build if more than 10% of files have issues.
```

## PARAMETERS

### -Enable
Enable file consistency checking during build.

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

### -FailOnInconsistency
Fail the build if consistency issues are found.

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

### -RequiredEncoding
Required file encoding.
Build will fail if files don't match this encoding.
Valid values: 'ASCII', 'UTF8', 'UTF8BOM', 'Unicode', etc.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: UTF8BOM
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequiredLineEnding
Required line ending style.
Build will fail if files don't match this style.
Valid values: 'CRLF', 'LF'

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: CRLF
Accept pipeline input: False
Accept wildcard characters: False
```

### -AutoFix
Automatically fix encoding and line ending issues during build.

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

### -CreateBackups
Create backup files before applying automatic fixes.

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

### -MaxInconsistencyPercentage
Maximum percentage of files that can have consistency issues.
Default is 5%.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: 5
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeDirectories
Directory names to exclude from consistency analysis.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: @('Artefacts', 'Ignore', '.git', '.vs', 'bin', 'obj')
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExportReport
Export detailed consistency report to the artifacts directory.

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

### -ReportFileName
Custom filename for the consistency report.
Default is 'FileConsistencyReport.csv'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: FileConsistencyReport.csv
Accept pipeline input: False
Accept wildcard characters: False
```

### -CheckMixedLineEndings
Check for files with mixed line endings (both CRLF and LF in same file).

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

### -CheckMissingFinalNewline
Check for files missing final newlines.

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
This function is part of the PSPublishModule DSL for configuring module builds.
Use within Build-Module script blocks to configure file consistency checking.

## RELATED LINKS

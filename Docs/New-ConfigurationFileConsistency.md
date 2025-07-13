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
 [-CheckMissingFinalNewline] [<CommonParameters>]
```

## DESCRIPTION
Configures file encoding and line ending consistency analysis to be performed during the module build process.
Can enforce consistency requirements and fail the build if issues are found.

This function analyzes project files for encoding and line ending consistency, ensuring proper cross-platform
compatibility and adherence to project standards.

## EXAMPLES

### Example 1: Enable basic consistency checking
```powershell
New-ConfigurationFileConsistency -Enable -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF
```

Enable consistency checking with specific encoding and line ending requirements.

### Example 2: Enable with automatic fixing
```powershell
New-ConfigurationFileConsistency -Enable -AutoFix -CreateBackups
```

Enable checking with automatic fixing and backup creation.

### Example 3: Strict consistency with build failure
```powershell
New-ConfigurationFileConsistency -Enable -FailOnInconsistency -MaxInconsistencyPercentage 10
```

Enable checking and fail build if more than 10% of files have issues.

## PARAMETERS

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

### -ExcludeDirectories
Directory names to exclude from consistency analysis.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
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

### -MaxInconsistencyPercentage
Maximum percentage of files that can have consistency issues. Default is 5%.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: 5
Accept pipeline input: False
Accept wildcard characters: False
```

### -ReportFileName
Custom filename for the consistency report. Default is 'FileConsistencyReport.csv'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: FileConsistencyReport.csv
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequiredEncoding
Required file encoding. Build will fail if files don't match this encoding.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: ASCII, UTF8, UTF8BOM, Unicode, BigEndianUnicode, UTF7, UTF32

Required: False
Position: Named
Default value: UTF8BOM
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequiredLineEnding
Required line ending style. Build will fail if files don't match this style.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: CRLF, LF

Required: False
Position: Named
Default value: CRLF
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

## OUTPUTS

### System.Collections.Specialized.OrderedDictionary

Returns a configuration object that can be used with Build-Module.

## NOTES

This function is part of the PSPublishModule DSL for configuring module builds.
Use within Build-Module script blocks to configure file consistency checking.

The consistency analysis will check for:
- File encoding consistency (ASCII, UTF8, UTF8BOM, etc.)
- Line ending consistency (CRLF vs LF)
- Mixed line endings within individual files
- Missing final newlines
- Cross-platform compatibility issues

## RELATED LINKS

[Get-ProjectEncoding](Get-ProjectEncoding.md)
[Get-ProjectLineEnding](Get-ProjectLineEnding.md)
[Convert-ProjectEncoding](Convert-ProjectEncoding.md)
[Convert-ProjectLineEnding](Convert-ProjectLineEnding.md)
[Invoke-ModuleBuild](Invoke-ModuleBuild.md)
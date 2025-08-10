---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# New-ConfigurationCompatibility

## SYNOPSIS
Creates configuration for PowerShell compatibility checking during module build.

## SYNTAX

```
New-ConfigurationCompatibility [-Enable] [-FailOnIncompatibility] [-RequirePS51Compatibility]
 [-RequirePS7Compatibility] [-RequireCrossCompatibility] [[-MinimumCompatibilityPercentage] <Int32>]
 [[-ExcludeDirectories] <String[]>] [-ExportReport] [[-ReportFileName] <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Configures PowerShell version compatibility analysis to be performed during the module build process.
Can enforce compatibility requirements and fail the build if compatibility issues are found.

## EXAMPLES

### EXAMPLE 1
```
New-ConfigurationCompatibility -Enable -RequireCrossCompatibility
Enable compatibility checking and require all files to be cross-compatible.
```

### EXAMPLE 2
```
New-ConfigurationCompatibility -Enable -MinimumCompatibilityPercentage 90 -ExportReport
Enable checking with 90% minimum compatibility and export detailed report.
```

### EXAMPLE 3
```
New-ConfigurationCompatibility -Enable -RequirePS51Compatibility -FailOnIncompatibility
Require PS 5.1 compatibility and fail build if issues are found.
```

## PARAMETERS

### -Enable
Enable PowerShell compatibility checking during build.

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

### -FailOnIncompatibility
Fail the build if compatibility issues are found.

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

### -RequirePS51Compatibility
Require PowerShell 5.1 compatibility.
Build will fail if any files are incompatible.

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

### -RequirePS7Compatibility
Require PowerShell 7 compatibility.
Build will fail if any files are incompatible.

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

### -RequireCrossCompatibility
Require cross-version compatibility (both PS 5.1 and PS 7).
Build will fail if any files are incompatible.

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

### -MinimumCompatibilityPercentage
Minimum percentage of files that must be cross-compatible.
Default is 95%.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: 95
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExcludeDirectories
Directory names to exclude from compatibility analysis.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: @('Artefacts', 'Ignore', '.git', '.vs', 'bin', 'obj')
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExportReport
Export detailed compatibility report to the artifacts directory.

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
Custom filename for the compatibility report.
Default is 'PowerShellCompatibilityReport.csv'.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: PowerShellCompatibilityReport.csv
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
Use within Build-Module script blocks to configure compatibility checking.

## RELATED LINKS

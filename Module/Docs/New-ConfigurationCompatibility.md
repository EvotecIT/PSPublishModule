---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# New-ConfigurationCompatibility
## SYNOPSIS
Creates configuration for PowerShell compatibility checking during module build.

## SYNTAX
### __AllParameterSets
```powershell
New-ConfigurationCompatibility [-Enable] [-FailOnIncompatibility] [-Severity <ValidationSeverity>] [-RequirePS51Compatibility] [-RequirePS7Compatibility] [-RequireCrossCompatibility] [-MinimumCompatibilityPercentage <int>] [-ExcludeDirectories <string[]>] [-ExportReport] [-ReportFileName <string>] [<CommonParameters>]
```

## DESCRIPTION
Adds a compatibility validation step to the build pipeline. This can be used to enforce that the module source is compatible
with Windows PowerShell 5.1 and/or PowerShell 7+.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>New-ConfigurationCompatibility -Enable -RequireCrossCompatibility -FailOnIncompatibility -MinimumCompatibilityPercentage 95 -ExportReport
```

Enables validation and exports a CSV report when issues are detected.

### EXAMPLE 2
```powershell
PS>New-ConfigurationCompatibility -Enable -RequirePS7Compatibility -FailOnIncompatibility -ExportReport
```

## PARAMETERS

### -Enable
Enable PowerShell compatibility checking during build.

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
Directory names to exclude from compatibility analysis.

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
Export detailed compatibility report to the artifacts directory.

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

### -FailOnIncompatibility
Fail the build if compatibility issues are found.

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

### -MinimumCompatibilityPercentage
Minimum percentage of files that must be cross-compatible. Default is 95.

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

### -ReportFileName
Custom filename for the compatibility report.

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

### -RequireCrossCompatibility
Require cross-version compatibility (both PS 5.1 and PS 7).

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

### -RequirePS51Compatibility
Require PowerShell 5.1 compatibility.

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

### -RequirePS7Compatibility
Require PowerShell 7 compatibility.

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

### -Severity
Severity for compatibility issues (overrides FailOnIncompatibility when specified).

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None


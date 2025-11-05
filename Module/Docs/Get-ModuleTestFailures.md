---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Get-ModuleTestFailures

## SYNOPSIS
Analyzes and summarizes failed Pester tests from various sources.

## SYNTAX

### Path (Default)
```
Get-ModuleTestFailures [-Path <String>] [-ProjectPath <String>] [-OutputFormat <String>] [-ShowSuccessful]
 [-PassThru] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### TestResults
```
Get-ModuleTestFailures -TestResults <Object> [-ProjectPath <String>] [-OutputFormat <String>] [-ShowSuccessful]
 [-PassThru] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Reads Pester test results and provides a concise summary of failing tests.
Supports both NUnit XML result files and Pester result objects directly.
Integrates with the existing PSPublishModule testing framework.

## EXAMPLES

### EXAMPLE 1
```
# Analyze test failures from default location
Get-ModuleTestFailures
```

### EXAMPLE 2
```
# Analyze failures from specific XML file
Get-ModuleTestFailures -Path 'Tests\TestResults.xml'
```

### EXAMPLE 3
```
# Analyze failures from Pester results object
$testResults = Invoke-ModuleTestSuite -PassThru
Get-ModuleTestFailures -TestResults $testResults
```

### EXAMPLE 4
```
# Get detailed failure information
Get-ModuleTestFailures -OutputFormat Detailed
```

### EXAMPLE 5
```
# Get results for further processing
$failures = Get-ModuleTestFailures -PassThru
if ($failures.FailedCount -gt 0) {
    # Process failures...
}
```

## PARAMETERS

### -Path
Path to the NUnit XML test results file.
If not specified, looks for TestResults.xml
in the standard locations relative to the current project.

```yaml
Type: String
Parameter Sets: Path
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestResults
Pester test results object from Invoke-Pester or Invoke-ModuleTestSuite.

```yaml
Type: Object
Parameter Sets: TestResults
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProjectPath
Path to the project directory (defaults to current script root).
Used to locate test results when Path is not specified.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: $PSScriptRoot
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputFormat
Format for displaying test failures.
- Summary: Shows only test names and counts
- Detailed: Shows test names with error messages
- JSON: Returns results as JSON object

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Detailed
Accept pipeline input: False
Accept wildcard characters: False
```

### -ShowSuccessful
Include successful tests in the output (only applies to Summary format).

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
Return the failure analysis object for further processing.

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
This function integrates with the PSPublishModule testing framework and supports
both Pester v4 and v5+ result formats.

## RELATED LINKS

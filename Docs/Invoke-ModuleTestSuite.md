---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Invoke-ModuleTestSuite

## SYNOPSIS
Complete module testing suite that handles dependencies, imports, and test execution

## SYNTAX

```
Invoke-ModuleTestSuite [[-ProjectPath] <String>] [[-AdditionalModules] <String[]>] [[-SkipModules] <String[]>]
 [[-TestPath] <String>] [[-OutputFormat] <String>] [-EnableCodeCoverage] [-Force] [-ExitOnFailure]
 [-SkipDependencies] [-SkipImport] [-PassThru] [-CICD] [-ShowFailureSummary] [[-FailureSummaryFormat] <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
A comprehensive function that combines module information gathering, dependency management,
module importing, and test execution into a single, easy-to-use command.
This is the
primary function users should call to test their modules.

## EXAMPLES

### EXAMPLE 1
```
# Basic usage - test current module
Invoke-ModuleTestSuite
```

### EXAMPLE 2
```
# Test with additional modules and custom settings
Invoke-ModuleTestSuite -AdditionalModules @('Pester', 'PSWriteColor') -SkipModules @('CertNoob') -EnableCodeCoverage
```

### EXAMPLE 3
```
# Test different project with minimal output
Invoke-ModuleTestSuite -ProjectPath "C:\MyModule" -OutputFormat Minimal -Force
```

### EXAMPLE 4
```
# CI/CD optimized testing
Invoke-ModuleTestSuite -CICD -EnableCodeCoverage
```

### EXAMPLE 5
```
# Get test results for further processing
$results = Invoke-ModuleTestSuite -PassThru
Write-Host "Test duration: $($results.Time)"
```

## PARAMETERS

### -ProjectPath
Path to the PowerShell module project directory (defaults to current script root)

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: $PSScriptRoot
Accept pipeline input: False
Accept wildcard characters: False
```

### -AdditionalModules
Additional modules to install beyond those specified in the manifest

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: @('Pester', 'PSWriteColor')
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipModules
Array of module names to skip during installation

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: @()
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestPath
Custom path to test files (defaults to Tests folder in project)

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 4
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputFormat
Test output format (Detailed, Normal, Minimal)

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 5
Default value: Detailed
Accept pipeline input: False
Accept wildcard characters: False
```

### -EnableCodeCoverage
Enable code coverage analysis during tests

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

### -Force
Force reinstall of modules and reimport of the target module

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

### -ExitOnFailure
Exit PowerShell session if tests fail

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

### -SkipDependencies
Skip dependency checking and installation

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

### -SkipImport
Skip module import step

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
Return test results object

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

### -CICD
Enable CI/CD mode with optimized settings and output

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

### -ShowFailureSummary
Display detailed failure analysis when tests fail

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

### -FailureSummaryFormat
Format for failure summary display (Summary, Detailed)

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 6
Default value: Summary
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

## RELATED LINKS

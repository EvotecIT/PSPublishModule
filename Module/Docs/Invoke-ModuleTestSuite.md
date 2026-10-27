---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---
# Invoke-ModuleTestSuite
## SYNOPSIS
Complete module testing suite that handles dependencies, imports, and test execution.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-ModuleTestSuite [-ProjectPath <string>] [-AdditionalModules <string[]>] [-SkipModules <string[]>] [-TestPath <string>] [-PesterVersion <string>] [-ResultsPath <string>] [-JUnitResultsPath <string>] [-SummaryPath <string>] [-WindowsPowerShell] [-FailIfNoTests] [-OutputFormat <ModuleTestSuiteOutputFormat>] [-TimeoutSeconds <int>] [-EnableCodeCoverage] [-Force] [-ExitOnFailure] [-SkipDependencies] [-SkipImport] [-PassThru] [-CICD] [-ShowFailureSummary] [-FailureSummaryFormat <ModuleTestSuiteFailureSummaryFormat>] [<CommonParameters>]
```

## DESCRIPTION
Executes module tests out-of-process, installs required dependencies, and provides a summary that is suitable for both
local development and CI pipelines.

For post-processing failures (e.g. emitting JSON summaries), combine it with Get-ModuleTestFailures.

## EXAMPLES

### EXAMPLE 1
```powershell
PS> Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule'
```

Runs tests under the module project folder, installs dependencies, and prints a summary.

### EXAMPLE 2
```powershell
PS> Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' -CICD -PassThru
```

Optimizes output for CI and returns a structured result object.

### EXAMPLE 3
```powershell
PS> Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' -PassThru | Get-ModuleTestFailures -OutputFormat Summary
```

Produces a concise failure summary that can be used in CI logs.

### EXAMPLE 4
```powershell
Invoke-ModuleTestSuite -PesterVersion 6.1.0 -ResultsPath TestResults/nunit.xml -JUnitResultsPath TestResults/junit.xml -SummaryPath TestResults/summary.md -WindowsPowerShell -FailIfNoTests
```

Saves pinned Pester and runs tests in Windows PowerShell 5.1, retaining reports and a failure summary.

## PARAMETERS

### -AdditionalModules
Additional modules to install beyond those specified in the manifest.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CICD
Enable CI/CD mode with optimized settings and output.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -EnableCodeCoverage
Enable code coverage analysis during tests.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExitOnFailure
Set a non-zero process exit code when tests fail.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FailIfNoTests
Fail when Pester discovers no tests. Defaults to false for existing callers.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FailureSummaryFormat
Format for failure summary display.

```yaml
Type: ModuleTestSuiteFailureSummaryFormat
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Summary, Detailed

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Force reinstall of modules and reimport of the target module.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -JUnitResultsPath
Optional JUnit XML output path, relative to ProjectPath. Requires Pester 5 or newer.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputFormat
Test output format.

```yaml
Type: ModuleTestSuiteOutputFormat
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Detailed, Normal, Minimal

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PassThru
Return the test suite result object.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PesterVersion
Exact Pester version to save and import. Omit to retain installed-version selection.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProjectPath
Path to the PowerShell module project directory.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ResultsPath
Explicit NUnit XML output path, relative to ProjectPath. Specifying it keeps the result.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ShowFailureSummary
Display detailed failure analysis when tests fail.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipDependencies
Skip dependency checking and installation.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipImport
Skip module import step.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipModules
Array of module names to skip during installation.

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SummaryPath
Optional Markdown summary output path, relative to ProjectPath.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TestPath
Custom path to test files (defaults to Tests folder in project).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TimeoutSeconds
Timeout for the out-of-process test execution, in seconds.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WindowsPowerShell
Require Windows PowerShell 5.1 for tests without falling back to PowerShell 7.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `None`

## RELATED LINKS

- None

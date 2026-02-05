---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Invoke-ModuleTestSuite
## SYNOPSIS
Complete module testing suite that handles dependencies, imports, and test execution.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-ModuleTestSuite [-ProjectPath <string>] [-AdditionalModules <string[]>] [-SkipModules <string[]>] [-TestPath <string>] [-OutputFormat <ModuleTestSuiteOutputFormat>] [-TimeoutSeconds <int>] [-EnableCodeCoverage] [-Force] [-ExitOnFailure] [-SkipDependencies] [-SkipImport] [-PassThru] [-CICD] [-ShowFailureSummary] [-FailureSummaryFormat <ModuleTestSuiteFailureSummaryFormat>] [<CommonParameters>]
```

## DESCRIPTION
Executes module tests out-of-process, installs required dependencies, and provides a summary that is suitable for both
local development and CI pipelines.

For post-processing failures (e.g. emitting JSON summaries), combine it with Get-ModuleTestFailures.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule'
```

Runs tests under the module project folder, installs dependencies, and prints a summary.

### EXAMPLE 2
```powershell
PS>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' -CICD -PassThru
```

Optimizes output for CI and returns a structured result object.

### EXAMPLE 3
```powershell
PS>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' -PassThru | Get-ModuleTestFailures -OutputFormat Summary
```

Produces a concise failure summary that can be used in CI logs.

## PARAMETERS

### -AdditionalModules
Additional modules to install beyond those specified in the manifest.

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

### -CICD
Enable CI/CD mode with optimized settings and output.

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

### -EnableCodeCoverage
Enable code coverage analysis during tests.

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

### -ExitOnFailure
Set a non-zero process exit code when tests fail.

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

### -FailureSummaryFormat
Format for failure summary display.

```yaml
Type: ModuleTestSuiteFailureSummaryFormat
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Force
Force reinstall of modules and reimport of the target module.

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

### -OutputFormat
Test output format.

```yaml
Type: ModuleTestSuiteOutputFormat
Parameter Sets: __AllParameterSets
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Return the test suite result object.

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

### -ProjectPath
Path to the PowerShell module project directory.

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

### -ShowFailureSummary
Display detailed failure analysis when tests fail.

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

### -SkipDependencies
Skip dependency checking and installation.

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

### -SkipImport
Skip module import step.

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

### -SkipModules
Array of module names to skip during installation.

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

### -TestPath
Custom path to test files (defaults to Tests folder in project).

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

### -TimeoutSeconds
Timeout for the out-of-process test execution, in seconds.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `None`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None


---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version: https://github.com/EvotecIT/PSPublishModule
schema: 2.0.0
---
# Get-ModuleTestFailures
## SYNOPSIS
Analyzes and summarizes failed Pester tests from either a Pester results object or an NUnit XML result file.

## SYNTAX
### Path (Default)
```powershell
Get-ModuleTestFailures [-Path <string>] [-ProjectPath <string>] [-OutputFormat <ModuleTestFailureOutputFormat>] [-ShowSuccessful] [-PassThru] [<CommonParameters>]
```

### TestResults
```powershell
Get-ModuleTestFailures -TestResults <Object> [-ProjectPath <string>] [-OutputFormat <ModuleTestFailureOutputFormat>] [-ShowSuccessful] [-PassThru] [<CommonParameters>]
```

## DESCRIPTION
This cmdlet is designed to make CI output and local troubleshooting easier by summarizing failures from:

Use -OutputFormat to control whether the cmdlet writes a concise host summary, detailed messages,
or emits JSON to the pipeline.

## EXAMPLES

### EXAMPLE 1
```powershell
PS>Get-ModuleTestFailures
```

Searches for TestResults.xml under the project and prints a detailed failure report.

### EXAMPLE 2
```powershell
PS>Get-ModuleTestFailures -Path 'Tests\TestResults.xml' -OutputFormat Summary
```

Writes a compact summary that is suitable for CI logs.

### EXAMPLE 3
```powershell
PS>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' | Get-ModuleTestFailures -OutputFormat Detailed -PassThru
```

Uses the in-memory results and returns the analysis object for further processing.

## PARAMETERS

### -OutputFormat
Format for displaying test failures.

```yaml
Type: ModuleTestFailureOutputFormat
Parameter Sets: Path, TestResults
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -PassThru
Return the failure analysis object for further processing.

```yaml
Type: SwitchParameter
Parameter Sets: Path, TestResults
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -Path
Path to the NUnit XML test results file. If not specified, searches for TestResults.xml under ProjectPath.

```yaml
Type: String
Parameter Sets: Path
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ProjectPath
Path to the project directory used to locate test results when Path is not specified.

```yaml
Type: String
Parameter Sets: Path, TestResults
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -ShowSuccessful
Include successful tests in the output (only applies to Summary format).

```yaml
Type: SwitchParameter
Parameter Sets: Path, TestResults
Aliases: None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: True
```

### -TestResults
Pester test results object from Invoke-Pester, a T:PowerForge.ModuleTestSuiteResult from Invoke-ModuleTestSuite,
or a precomputed T:PowerForge.ModuleTestFailureAnalysis.

```yaml
Type: Object
Parameter Sets: TestResults
Aliases: None

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: True
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `System.Object`

## OUTPUTS

- `System.Object`

## RELATED LINKS

- None


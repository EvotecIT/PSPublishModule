# PSPublishModule Testing + Failure Analysis (C#)

## Overview
Module test execution and failure analysis are implemented in C# (`PowerForge`) and exposed via thin cmdlets (`PSPublishModule`).

## Cmdlets
- `Invoke-ModuleTestSuite`: installs dependencies (optional), optionally imports the module, then runs Pester **out-of-process** and returns a typed `PowerForge.ModuleTestSuiteResult` when `-PassThru` is used.
- `Get-ModuleTestFailures`: summarizes failed tests from:
  - a `PowerForge.ModuleTestSuiteResult` (recommended),
  - a Pester results object from `Invoke-Pester`,
  - or an NUnit XML results file (`TestResults.xml`).

## Examples
```powershell
# Run tests and inspect failures
$results = Invoke-ModuleTestSuite -PassThru
Get-ModuleTestFailures -TestResults $results -OutputFormat Detailed

# Pipeline usage
Invoke-ModuleTestSuite -PassThru | Get-ModuleTestFailures -OutputFormat Summary

# CI/CD optimized settings
Invoke-ModuleTestSuite -CICD -EnableCodeCoverage
```

## Implementation (source)
- `PowerForge/Services/ModuleTestSuiteService.cs` (out-of-proc Pester runner + typed result)
- `PowerForge/Services/ModuleTestFailureAnalyzer.cs` (Pester/NUnit parsing → `PowerForge.ModuleTestFailureAnalysis`)
- `PSPublishModule/Cmdlets/InvokeModuleTestSuiteCommand.cs` (cmdlet wrapper + host output)
- `PSPublishModule/Cmdlets/GetModuleTestFailuresCommand.cs` (cmdlet wrapper + host/JSON output)

# PSPublishModule Test Failure Analysis (C# Structure)

## Summary
The legacy PowerShell implementation (Public/Private `.ps1` functions) has been replaced with:
- typed C# models in `PowerForge`,
- reusable analyzers/services in `PowerForge`,
- thin cmdlets in `PSPublishModule`.

## Structure
- `PowerForge.ModuleTestFailureAnalyzer`
  - Parses failures from a Pester results object (`Invoke-Pester`) or an NUnit XML file.
  - Produces `PowerForge.ModuleTestFailureAnalysis` containing totals + failed test details.
- `PowerForge.ModuleTestSuiteService`
  - Runs Pester **out-of-process** (VSCode/CLI friendly).
  - Produces `PowerForge.ModuleTestSuiteResult` (counts, duration, coverage percent, optional embedded failure analysis).
- `PSPublishModule` cmdlets
  - `Invoke-ModuleTestSuite` (`PSPublishModule/Cmdlets/InvokeModuleTestSuiteCommand.cs`)
  - `Get-ModuleTestFailures` (`PSPublishModule/Cmdlets/GetModuleTestFailuresCommand.cs`)

## Notes
- `Get-ModuleTestFailures` accepts a `ModuleTestSuiteResult` directly, so you can run:
  ```powershell
  Invoke-ModuleTestSuite -PassThru | Get-ModuleTestFailures -OutputFormat Summary
  ```

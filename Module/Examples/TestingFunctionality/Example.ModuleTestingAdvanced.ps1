# Example: Advanced Module Testing with Custom Configuration
# This example shows advanced usage with custom settings including failure analysis

# Advanced usage - still one command, but with custom options
$invokeModuleTestSuiteSplat = @{
    ProjectPath           = $PSScriptRoot
    AdditionalModules     = @('Pester', 'PSWriteColor', 'PSWriteHTML', 'PSSharedGoods')
    SkipModules          = @('CertNoob', 'SomeOtherModule')
    EnableCodeCoverage   = $true
    OutputFormat         = 'Detailed'
    Force                = $true
    PassThru             = $true
    ShowFailureSummary   = $true  # New parameter for failure analysis
    FailureSummaryFormat = 'Detailed'  # Options: 'Summary', 'Detailed'
}

$testResults = Invoke-ModuleTestSuite @invokeModuleTestSuiteSplat

# Optional: Process the results further if needed
if ($testResults) {
    Write-Host "`nAdditional Test Information:" -ForegroundColor Cyan
    Write-Host "  Execution Time: $($testResults.Time)" -ForegroundColor White
    Write-Host "  Test Cases: $($testResults.TotalCount)" -ForegroundColor White

    if ($testResults.TotalCount -gt 0) {
        $successRate = [math]::Round(($testResults.PassedCount / $testResults.TotalCount) * 100, 2)
        Write-Host "  Success Rate: $successRate%" -ForegroundColor White
    }

    if ($testResults.CodeCoverage) {
        $coveragePercent = [math]::Round(($testResults.CodeCoverage.NumberOfCommandsExecuted / $testResults.CodeCoverage.NumberOfCommandsAnalyzed) * 100, 2)
        Write-Host "  Code Coverage: $coveragePercent%" -ForegroundColor White
    }
}

# Example: Standalone failure analysis
Write-Host "`n=== Standalone Failure Analysis Examples ===" -ForegroundColor Magenta

# 1. Analyze failures from test results object
if ($testResults) {
    Write-Host "`n1. Analyzing failures from test results object:" -ForegroundColor Yellow
    Get-ModuleTestFailures -TestResults $testResults -OutputFormat Summary
}

# 2. Analyze failures from XML file (if available)
$xmlPath = Join-Path -Path $PSScriptRoot -ChildPath 'TestResults.xml'
if (Test-Path -Path $xmlPath) {
    Write-Host "`n2. Analyzing failures from XML file:" -ForegroundColor Yellow
    Get-ModuleTestFailures -Path $xmlPath -OutputFormat Detailed
} else {
    Write-Host "`n2. No XML test results file found at: $xmlPath" -ForegroundColor Gray
}

# 3. Get failure analysis for further processing
Write-Host "`n3. Getting failure analysis for processing:" -ForegroundColor Yellow
$failureAnalysis = Get-ModuleTestFailures -TestResults $testResults -PassThru
if ($failureAnalysis) {
    Write-Host "   Analysis completed at: $($failureAnalysis.Timestamp)" -ForegroundColor Cyan
    Write-Host "   Total failures found: $($failureAnalysis.FailedCount)" -ForegroundColor $(if ($failureAnalysis.FailedCount -gt 0) { 'Red' } else { 'Green' })

    if ($failureAnalysis.FailedCount -gt 0) {
        Write-Host "   Failed test names:" -ForegroundColor Yellow
        foreach ($failure in $failureAnalysis.FailedTests) {
            Write-Host "     • $($failure.Name)" -ForegroundColor Red
        }
    }
}

# 4. JSON output example
Write-Host "`n4. JSON output format:" -ForegroundColor Yellow
$jsonOutput = Get-ModuleTestFailures -TestResults $testResults -OutputFormat JSON
if ($jsonOutput) {
    Write-Host $jsonOutput -ForegroundColor Gray
}

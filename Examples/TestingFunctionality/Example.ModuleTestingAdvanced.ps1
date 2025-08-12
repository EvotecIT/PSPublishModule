# Example: Advanced Module Testing with Custom Configuration
# This example shows advanced usage with custom settings

# Advanced usage - still one command, but with custom options
$invokeModuleTestSuiteSplat = @{
    ProjectPath        = $PSScriptRoot
    AdditionalModules  = @('Pester', 'PSWriteColor', 'PSWriteHTML', 'PSSharedGoods')
    SkipModules        = @('CertNoob', 'SomeOtherModule')
    EnableCodeCoverage = $true
    OutputFormat       = 'Detailed'
    Force              = $true
    PassThru           = $true
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

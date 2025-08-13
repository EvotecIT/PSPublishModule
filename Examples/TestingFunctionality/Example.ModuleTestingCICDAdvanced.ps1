# Example: Advanced CI/CD with Custom Result Processing
# Shows how to use CI/CD mode with custom result handling for advanced scenarios

param(
    [string]$ProjectPath = $PSScriptRoot,
    [string]$ResultsFile = (Join-Path $PSScriptRoot 'test-results.json')
)

# Run tests in CI/CD mode - this handles most CI/CD concerns automatically
$results = Invoke-ModuleTestSuite -ProjectPath $ProjectPath -CICD -EnableCodeCoverage

# Optional: Export detailed results for further processing
if ($results) {
    $moduleInfo = Get-ModuleInformation -Path $ProjectPath
    $summary = @{
        Module      = $moduleInfo.ModuleName
        Success     = $results.FailedCount -eq 0
        TotalTests  = $results.TotalCount
        PassedTests = $results.PassedCount
        FailedTests = $results.FailedCount
        Duration    = $results.Time
        Timestamp   = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    }

    if ($results.CodeCoverage) {
        $summary.CodeCoveragePercent = [math]::Round(($results.CodeCoverage.NumberOfCommandsExecuted / $results.CodeCoverage.NumberOfCommandsAnalyzed) * 100, 2)
    }

    $summary | ConvertTo-Json | Out-File -FilePath $ResultsFile -Encoding UTF8
    Write-Host "Results exported to: $ResultsFile" -ForegroundColor Cyan
}

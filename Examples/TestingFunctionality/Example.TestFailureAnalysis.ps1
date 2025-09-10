# Example: Module Test Failure Analysis
# This example demonstrates the test failure analysis functionality

# Basic test execution with failure summary
Write-Host "=== Basic Testing with Failure Summary ===" -ForegroundColor Cyan

# Run tests with automatic failure analysis
Invoke-ModuleTestSuite -ShowFailureSummary -FailureSummaryFormat Detailed

Write-Host "`n=== Standalone Failure Analysis ===" -ForegroundColor Cyan

# 1. Analyze from test results (most common usage)
Write-Host "`n1. Analyzing from test results object:" -ForegroundColor Yellow
$testResults = Invoke-ModuleTestSuite -PassThru
Get-ModuleTestFailures -TestResults $testResults -OutputFormat Summary

# 2. Analyze from XML file (useful for CI/CD scenarios)
Write-Host "`n2. Analyzing from XML file:" -ForegroundColor Yellow
# Assuming test results are saved to XML
Get-ModuleTestFailures -Path "Tests\TestResults.xml" -OutputFormat Detailed

# 3. Different output formats
Write-Host "`n3. Different output formats:" -ForegroundColor Yellow

Write-Host "`n   Summary format:" -ForegroundColor Gray
Get-ModuleTestFailures -TestResults $testResults -OutputFormat Summary

Write-Host "`n   Detailed format:" -ForegroundColor Gray
Get-ModuleTestFailures -TestResults $testResults -OutputFormat Detailed

Write-Host "`n   JSON format:" -ForegroundColor Gray
$jsonOutput = Get-ModuleTestFailures -TestResults $testResults -OutputFormat JSON
Write-Host $jsonOutput

# 4. Process results programmatically
Write-Host "`n4. Processing results programmatically:" -ForegroundColor Yellow
$failures = Get-ModuleTestFailures -TestResults $testResults -PassThru

if ($failures.FailedCount -gt 0) {
    Write-Host "   Found $($failures.FailedCount) failed tests:" -ForegroundColor Red

    foreach ($failure in $failures.FailedTests) {
        Write-Host "   - Test: $($failure.Name)" -ForegroundColor Yellow
        if ($failure.Duration) {
            Write-Host "     Duration: $($failure.Duration)" -ForegroundColor Gray
        }
        if ($failure.ErrorMessage -ne 'No error message available') {
            $shortError = if ($failure.ErrorMessage.Length -gt 100) {
                $failure.ErrorMessage.Substring(0, 100) + "..."
            } else {
                $failure.ErrorMessage
            }
            Write-Host "     Error: $shortError" -ForegroundColor Red
        }
    }
} else {
    Write-Host "   ✅ No test failures found!" -ForegroundColor Green
}

# 5. CI/CD Integration Example
Write-Host "`n5. CI/CD Integration:" -ForegroundColor Yellow
if ($env:CI -or $env:GITHUB_ACTIONS -or $env:TF_BUILD) {
    Write-Host "   CI/CD environment detected - using optimized settings" -ForegroundColor Cyan

    # CI/CD mode automatically includes failure summary
    $ciResults = Invoke-ModuleTestSuite -CICD -PassThru

    if ($ciResults.FailedCount -gt 0) {
        Write-Host "   Setting CI failure indicators..." -ForegroundColor Red
        # In real CI/CD, this would set environment variables or exit codes
    }
} else {
    Write-Host "   Not in CI/CD environment - simulating CI behavior" -ForegroundColor Gray
    Invoke-ModuleTestSuite -CICD
}
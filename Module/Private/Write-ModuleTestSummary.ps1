function Write-ModuleTestSummary {
    <#
    .SYNOPSIS
    Write summary format output for test failure analysis

    .DESCRIPTION
    Internal function that displays test failure information in a summary format
    with visual indicators and basic statistics.

    .PARAMETER FailureAnalysis
    Failure analysis object containing test results

    .PARAMETER ShowSuccessful
    Include successful test information in the output

    .NOTES
    This is an internal function used by Get-ModuleTestFailures.
    Provides a concise overview with visual indicators.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$FailureAnalysis,

        [Parameter()]
        [switch]$ShowSuccessful
    )

    Write-Host "=== Module Test Results Summary ===" -ForegroundColor Cyan
    Write-Host "Source: $($FailureAnalysis.Source)" -ForegroundColor Gray
    Write-Host

    $TotalCount = $FailureAnalysis.TotalCount
    $PassedCount = $FailureAnalysis.PassedCount
    $FailedCount = $FailureAnalysis.FailedCount
    $SkippedCount = $FailureAnalysis.SkippedCount

    Write-Host "📊 Test Statistics:" -ForegroundColor Yellow
    Write-Host "   Total Tests: $TotalCount" -ForegroundColor White
    Write-Host "   ✅ Passed: $PassedCount" -ForegroundColor Green
    Write-Host "   ❌ Failed: $FailedCount" -ForegroundColor $(if ($FailedCount -gt 0) { 'Red' } else { 'Green' })
    if ($SkippedCount -gt 0) {
        Write-Host "   ⏭️  Skipped: $SkippedCount" -ForegroundColor Yellow
    }

    if ($TotalCount -gt 0) {
        $SuccessRate = [math]::Round(($PassedCount / $TotalCount) * 100, 1)
        Write-Host "   📈 Success Rate: $SuccessRate%" -ForegroundColor $(if ($SuccessRate -eq 100) { 'Green' } elseif ($SuccessRate -ge 80) { 'Yellow' } else { 'Red' })
    }

    Write-Host

    if ($FailedCount -gt 0) {
        Write-Host "❌ Failed Tests:" -ForegroundColor Red
        foreach ($Failure in $FailureAnalysis.FailedTests) {
            Write-Host "   • $($Failure.Name)" -ForegroundColor Red
        }
        Write-Host
    }

    if ($ShowSuccessful.IsPresent -and $PassedCount -gt 0) {
        Write-Host "✅ All tests passed successfully!" -ForegroundColor Green
    }
}
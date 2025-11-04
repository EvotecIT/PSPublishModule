function Write-ModuleTestDetails {
    <#
    .SYNOPSIS
    Write detailed format output for test failure analysis

    .DESCRIPTION
    Internal function that displays comprehensive test failure information
    including error messages, stack traces, and timing information.

    .PARAMETER FailureAnalysis
    Failure analysis object containing test results

    .NOTES
    This is an internal function used by Get-ModuleTestFailures.
    Provides detailed diagnostic information for troubleshooting.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$FailureAnalysis
    )

    Write-Host "=== Module Test Failure Analysis ===" -ForegroundColor Cyan
    Write-Host "Source: $($FailureAnalysis.Source)" -ForegroundColor Gray
    Write-Host "Analysis Time: $($FailureAnalysis.Timestamp)" -ForegroundColor Gray
    Write-Host

    $TotalCount = $FailureAnalysis.TotalCount
    $PassedCount = $FailureAnalysis.PassedCount
    $FailedCount = $FailureAnalysis.FailedCount

    if ($TotalCount -eq 0) {
        Write-Host "⚠️  No test results found" -ForegroundColor Yellow
        return
    }

    Write-Host "📊 Summary: $PassedCount/$TotalCount tests passed" -ForegroundColor $(if ($FailedCount -eq 0) { 'Green' } else { 'Yellow' })
    Write-Host

    if ($FailedCount -eq 0) {
        Write-Host "🎉 All tests passed successfully!" -ForegroundColor Green
        return
    }

    Write-Host "❌ Failed Tests ($FailedCount):" -ForegroundColor Red
    Write-Host

    foreach ($Failure in $FailureAnalysis.FailedTests) {
        Write-Host "🔴 $($Failure.Name)" -ForegroundColor Red

        if ($Failure.ErrorMessage -and $Failure.ErrorMessage -ne 'No error message available') {
            # Split long error messages for better readability
            $MessageLines = $Failure.ErrorMessage -split "`n"
            foreach ($Line in $MessageLines) {
                if ($Line.Trim()) {
                    Write-Host "   💬 $($Line.Trim())" -ForegroundColor Yellow
                }
            }
        }

        if ($Failure.Duration) {
            Write-Host "   ⏱️  Duration: $($Failure.Duration)" -ForegroundColor Gray
        }

        Write-Host
    }

    Write-Host "=== Summary: $FailedCount test$(if ($FailedCount -ne 1) { 's' }) failed ===" -ForegroundColor Red
}
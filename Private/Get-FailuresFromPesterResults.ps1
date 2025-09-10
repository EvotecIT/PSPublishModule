function Get-FailuresFromPesterResults {
    <#
    .SYNOPSIS
    Extract failure information from Pester results object

    .DESCRIPTION
    Internal function that processes Pester test result objects and extracts
    failure information compatible with both Pester v4 and v5+ formats.

    .PARAMETER TestResults
    Pester test results object from Invoke-Pester

    .PARAMETER FailureAnalysis
    Failure analysis object to populate with results

    .NOTES
    This is an internal function used by Get-ModuleTestFailures.
    Supports both Pester v4 and v5+ result object formats.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object]$TestResults,

        [Parameter(Mandatory)]
        [PSCustomObject]$FailureAnalysis
    )

    # Handle different Pester versions result object properties
    $FailureAnalysis.TotalCount = if ($null -ne $TestResults.TotalCount) {
        $TestResults.TotalCount
    } elseif ($TestResults.Tests) {
        $TestResults.Tests.Count
    } else {
        0
    }

    $FailureAnalysis.PassedCount = if ($null -ne $TestResults.PassedCount) {
        $TestResults.PassedCount
    } elseif ($TestResults.Tests) {
        ($TestResults.Tests | Where-Object Result -EQ 'Passed').Count
    } else {
        0
    }

    $FailureAnalysis.FailedCount = if ($null -ne $TestResults.FailedCount) {
        $TestResults.FailedCount
    } elseif ($TestResults.Tests) {
        ($TestResults.Tests | Where-Object Result -EQ 'Failed').Count
    } else {
        0
    }

    $FailureAnalysis.SkippedCount = if ($null -ne $TestResults.SkippedCount) {
        $TestResults.SkippedCount
    } elseif ($TestResults.Tests) {
        ($TestResults.Tests | Where-Object Result -EQ 'Skipped').Count
    } else {
        0
    }

    # Extract failed tests
    if ($TestResults.Tests) {
        # Pester v5+ format
        $FailedTests = $TestResults.Tests | Where-Object Result -EQ 'Failed'
    } elseif ($TestResults.TestResult) {
        # Pester v4 format
        $FailedTests = $TestResults.TestResult | Where-Object Result -EQ 'Failed'
    } else {
        $FailedTests = @()
    }

    foreach ($Test in $FailedTests) {
        $FailureInfo = [PSCustomObject]@{
            Name         = if ($Test.Name) { $Test.Name } elseif ($Test.Describe -and $Test.Context -and $Test.It) { "$($Test.Describe) $($Test.Context) $($Test.It)" } else { 'Unknown Test' }
            ErrorMessage = if ($Test.ErrorRecord) { $Test.ErrorRecord.Exception.Message } elseif ($Test.FailureMessage) { $Test.FailureMessage } else { 'No error message available' }
            StackTrace   = if ($Test.ErrorRecord) { $Test.ErrorRecord.ScriptStackTrace } else { $null }
            Duration     = if ($Test.Time) { $Test.Time } elseif ($Test.Duration) { $Test.Duration } else { $null }
        }
        $FailureAnalysis.FailedTests += $FailureInfo
    }

    return $FailureAnalysis
}
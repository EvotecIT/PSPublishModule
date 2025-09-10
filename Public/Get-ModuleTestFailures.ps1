function Get-ModuleTestFailures {
    <#
    .SYNOPSIS
    Analyzes and summarizes failed Pester tests from various sources.

    .DESCRIPTION
    Reads Pester test results and provides a concise summary of failing tests.
    Supports both NUnit XML result files and Pester result objects directly.
    Integrates with the existing PSPublishModule testing framework.

    .PARAMETER Path
    Path to the NUnit XML test results file. If not specified, looks for TestResults.xml
    in the standard locations relative to the current project.

    .PARAMETER TestResults
    Pester test results object from Invoke-Pester or Invoke-ModuleTestSuite.

    .PARAMETER ProjectPath
    Path to the project directory (defaults to current script root).
    Used to locate test results when Path is not specified.

    .PARAMETER OutputFormat
    Format for displaying test failures.
    - Summary: Shows only test names and counts
    - Detailed: Shows test names with error messages
    - JSON: Returns results as JSON object

    .PARAMETER ShowSuccessful
    Include successful tests in the output (only applies to Summary format).

    .PARAMETER PassThru
    Return the failure analysis object for further processing.

    .EXAMPLE
    # Analyze test failures from default location
    Get-ModuleTestFailures

    .EXAMPLE
    # Analyze failures from specific XML file
    Get-ModuleTestFailures -Path 'Tests\TestResults.xml'

    .EXAMPLE
    # Analyze failures from Pester results object
    $testResults = Invoke-ModuleTestSuite -PassThru
    Get-ModuleTestFailures -TestResults $testResults

    .EXAMPLE
    # Get detailed failure information
    Get-ModuleTestFailures -OutputFormat Detailed

    .EXAMPLE
    # Get results for further processing
    $failures = Get-ModuleTestFailures -PassThru
    if ($failures.FailedCount -gt 0) {
        # Process failures...
    }

    .NOTES
    This function integrates with the PSPublishModule testing framework and supports
    both Pester v4 and v5+ result formats.
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    param(
        [Parameter(ParameterSetName = 'Path')]
        [string]$Path,

        [Parameter(ParameterSetName = 'TestResults', Mandatory)]
        [object]$TestResults,

        [Parameter()]
        [string]$ProjectPath = $PSScriptRoot,

        [Parameter()]
        [ValidateSet('Summary', 'Detailed', 'JSON')]
        [string]$OutputFormat = 'Detailed',

        [Parameter()]
        [switch]$ShowSuccessful,

        [Parameter()]
        [switch]$PassThru
    )

    try {
        $FailureAnalysis = [PSCustomObject]@{
            Source        = ''
            TotalCount    = 0
            PassedCount   = 0
            FailedCount   = 0
            SkippedCount  = 0
            FailedTests   = @()
            Timestamp     = Get-Date
        }

        if ($PSCmdlet.ParameterSetName -eq 'TestResults') {
            # Process Pester results object
            $FailureAnalysis.Source = 'PesterResults'
            $FailureAnalysis = Get-FailuresFromPesterResults -TestResults $TestResults -FailureAnalysis $FailureAnalysis

        } else {
            # Process XML file
            if (-not $Path) {
                # Try to find test results file in standard locations
                $TestResultsPaths = @(
                    (Join-Path -Path $ProjectPath -ChildPath 'TestResults.xml'),
                    (Join-Path -Path $ProjectPath -ChildPath 'Tests\TestResults.xml'),
                    (Join-Path -Path $ProjectPath -ChildPath 'Test\TestResults.xml'),
                    (Join-Path -Path $ProjectPath -ChildPath 'Tests\Results\TestResults.xml')
                )

                foreach ($TestPath in $TestResultsPaths) {
                    if (Test-Path -Path $TestPath) {
                        $Path = $TestPath
                        break
                    }
                }

                if (-not $Path) {
                    Write-Warning "No test results file found. Searched in:"
                    $TestResultsPaths | ForEach-Object { Write-Warning "  $_" }
                    return
                }
            }

            if (-not (Test-Path -Path $Path)) {
                Write-Error "Test results file not found: $Path"
                return
            }

            $FailureAnalysis.Source = $Path
            $FailureAnalysis = Get-FailuresFromXmlFile -Path $Path -FailureAnalysis $FailureAnalysis
        }

        # Display results based on output format
        switch ($OutputFormat) {
            'JSON' {
                $FailureAnalysis | ConvertTo-Json -Depth 3
            }
            'Summary' {
                Write-ModuleTestSummary -FailureAnalysis $FailureAnalysis -ShowSuccessful:$ShowSuccessful.IsPresent
            }
            'Detailed' {
                Write-ModuleTestDetails -FailureAnalysis $FailureAnalysis
            }
        }

        if ($PassThru.IsPresent) {
            return $FailureAnalysis
        }

    } catch {
        Write-Error "Failed to analyze test failures: $($_.Exception.Message)"
        throw
    }
}
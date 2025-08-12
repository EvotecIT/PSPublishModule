function Invoke-ModuleTests {
    <#
    .SYNOPSIS
    Runs Pester tests for a PowerShell module with proper configuration

    .DESCRIPTION
    Executes Pester tests for a module project with comprehensive configuration options.
    Supports both Pester v4 and v5+ syntax and provides detailed error reporting.

    .PARAMETER ModuleInformation
    Module information object returned by Get-ModuleInformation

    .PARAMETER TestPath
    Path to the test files or directory (defaults to Tests folder in project)

    .PARAMETER OutputFormat
    Output format for test results (Detailed, Normal, Minimal)

    .PARAMETER EnableCodeCoverage
    Enable code coverage analysis

    .PARAMETER ExitOnFailure
    Exit PowerShell session if tests fail

    .PARAMETER PassThru
    Return the test results object

    .PARAMETER PesterVersion
    Specify Pester version to use (Auto, V4, V5)

    .EXAMPLE
    $moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
    Invoke-ModuleTests -ModuleInformation $moduleInfo

    .EXAMPLE
    $moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
    $results = Invoke-ModuleTests -ModuleInformation $moduleInfo -PassThru -EnableCodeCoverage

    .EXAMPLE
    Invoke-ModuleTests -TestPath "C:\MyModule\Tests" -OutputFormat Minimal
    #>
    [CmdletBinding()]
    param(
        [Parameter(ParameterSetName = 'ModuleInfo')]
        [hashtable]$ModuleInformation,

        [Parameter(ParameterSetName = 'TestPath')]
        [string]$TestPath,

        [Parameter()]
        [ValidateSet('Detailed', 'Normal', 'Minimal')]
        [string]$OutputFormat = 'Detailed',

        [Parameter()]
        [switch]$EnableCodeCoverage,

        [Parameter()]
        [switch]$ExitOnFailure,

        [Parameter()]
        [switch]$PassThru,

        [Parameter()]
        [ValidateSet('Auto', 'V4', 'V5')]
        [string]$PesterVersion = 'Auto'
    )

    try {
        # Determine test path
        if ($PSCmdlet.ParameterSetName -eq 'ModuleInfo') {
            if (-not $ModuleInformation) {
                throw "ModuleInformation parameter is required when using ModuleInfo parameter set"
            }
            $TestPath = Join-Path $ModuleInformation.ProjectPath 'Tests'
            $ModuleName = $ModuleInformation.ModuleName
        } else {
            $ModuleName = 'Unknown'
        }

        # Validate test path exists
        if (-not (Test-Path -Path $TestPath)) {
            throw "Test path '$TestPath' does not exist"
        }

        Write-Host "Running tests for module: $ModuleName" -ForegroundColor Yellow
        Write-Host "Test path: $TestPath" -ForegroundColor Cyan

        # Ensure Pester is available
        try {
            Import-Module -Name Pester -Force -ErrorAction Stop
            $PesterModule = Get-Module -Name Pester
            Write-Host "Using Pester version: $($PesterModule.Version)" -ForegroundColor Green
        } catch {
            $err = $_
            $errorMessage = @"
Failed to import required module 'Pester'.
Error: $($err.Exception.Message)
FQID: $($err.FullyQualifiedErrorId)
Category: $($err.CategoryInfo)
Position: $($err.InvocationInfo.PositionMessage)
"@
            Write-Error -Message $errorMessage

            if ($err.ScriptStackTrace) {
                Write-Error -Message "Stack:`n$($err.ScriptStackTrace)"
            }
            throw
        }

        # Determine Pester version to use
        $UsePesterV5 = $false
        if ($PesterVersion -eq 'V5') {
            $UsePesterV5 = $true
        } elseif ($PesterVersion -eq 'V4') {
            $UsePesterV5 = $false
        } else {
            # Auto-detect based on available version
            $UsePesterV5 = $PesterModule.Version.Major -ge 5
        }

        Write-Host "Using Pester configuration: $(if ($UsePesterV5) { 'V5+' } else { 'V4' })" -ForegroundColor Cyan

        # Run tests based on Pester version
        if ($UsePesterV5) {
            # Pester v5+ configuration
            $Configuration = [PesterConfiguration]::Default
            $Configuration.Run.Path = $TestPath
            $Configuration.Run.Exit = $ExitOnFailure.IsPresent
            $Configuration.Should.ErrorAction = 'Continue'
            $Configuration.CodeCoverage.Enabled = $EnableCodeCoverage.IsPresent

            switch ($OutputFormat) {
                'Detailed' { $Configuration.Output.Verbosity = 'Detailed' }
                'Normal' { $Configuration.Output.Verbosity = 'Normal' }
                'Minimal' { $Configuration.Output.Verbosity = 'Minimal' }
            }

            Write-Host "Executing tests..." -ForegroundColor Yellow
            $Result = Invoke-Pester -Configuration $Configuration

        } else {
            # Pester v4 configuration
            $PesterParams = @{
                Script   = $TestPath
                Verbose  = ($OutputFormat -eq 'Detailed')
                PassThru = $true
            }

            if ($OutputFormat -eq 'Detailed') {
                $PesterParams.OutputFormat = 'NUnitXml'
            }

            if ($EnableCodeCoverage.IsPresent -and $ModuleInformation) {
                # Add code coverage for module files
                $ModuleFiles = Get-ChildItem -Path $ModuleInformation.ProjectPath -Filter '*.ps1' -Recurse | Where-Object { $_.Directory.Name -in @('Public', 'Private') }
                if ($ModuleFiles) {
                    $PesterParams.CodeCoverage = $ModuleFiles.FullName
                }
            }

            Write-Host "Executing tests..." -ForegroundColor Yellow
            $Result = Invoke-Pester @PesterParams
        }

        # Display results summary
        Write-Host
        Write-Host "Test Results Summary:" -ForegroundColor Cyan

        # Handle different Pester versions result object properties
        $TotalCount = if ($null -ne $Result.TotalCount) { $Result.TotalCount } elseif ($Result.Tests) { $Result.Tests.Count } else { 0 }
        $PassedCount = if ($null -ne $Result.PassedCount) { $Result.PassedCount } elseif ($Result.Tests) { ($Result.Tests | Where-Object Result -EQ 'Passed').Count } else { 0 }
        $FailedCount = if ($null -ne $Result.FailedCount) { $Result.FailedCount } elseif ($Result.Tests) { ($Result.Tests | Where-Object Result -EQ 'Failed').Count } else { 0 }
        $SkippedCount = if ($null -ne $Result.SkippedCount) { $Result.SkippedCount } elseif ($Result.Tests) { ($Result.Tests | Where-Object Result -EQ 'Skipped').Count } else { 0 }

        Write-Host "  Total Tests: $TotalCount" -ForegroundColor White
        Write-Host "  Passed: $PassedCount" -ForegroundColor Green
        Write-Host "  Failed: $FailedCount" -ForegroundColor $(if ($FailedCount -gt 0) { 'Red' } else { 'Green' })
        Write-Host "  Skipped: $SkippedCount" -ForegroundColor Yellow

        if ($EnableCodeCoverage.IsPresent -and $Result.CodeCoverage) {
            $CoveragePercent = [math]::Round(($Result.CodeCoverage.NumberOfCommandsExecuted / $Result.CodeCoverage.NumberOfCommandsAnalyzed) * 100, 2)
            Write-Host "  Code Coverage: $CoveragePercent%" -ForegroundColor Cyan
        }

        # Handle test failures
        if ($FailedCount -gt 0) {
            $failureMessage = "$FailedCount test$(if ($FailedCount -ne 1) { 's' }) failed for module '$ModuleName'"

            if ($ExitOnFailure.IsPresent) {
                Write-Host $failureMessage -ForegroundColor Red
                exit 1
            } else {
                throw $failureMessage
            }
        } else {
            Write-Host "All tests passed successfully!" -ForegroundColor Green
        }

        # Return results if requested
        if ($PassThru.IsPresent) {
            return $Result
        }

    } catch {
        Write-Error "Failed to run module tests: $($_.Exception.Message)"
        throw
    }
}

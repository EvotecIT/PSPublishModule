function Invoke-ModuleTestSuite {
    <#
    .SYNOPSIS
    Complete module testing suite that handles dependencies, imports, and test execution

    .DESCRIPTION
    A comprehensive function that combines module information gathering, dependency management,
    module importing, and test execution into a single, easy-to-use command. This is the
    primary function users should call to test their modules.

    .PARAMETER ProjectPath
    Path to the PowerShell module project directory (defaults to current script root)

    .PARAMETER AdditionalModules
    Additional modules to install beyond those specified in the manifest

    .PARAMETER SkipModules
    Array of module names to skip during installation

    .PARAMETER TestPath
    Custom path to test files (defaults to Tests folder in project)

    .PARAMETER OutputFormat
    Test output format (Detailed, Normal, Minimal)

    .PARAMETER EnableCodeCoverage
    Enable code coverage analysis during tests

    .PARAMETER Force
    Force reinstall of modules and reimport of the target module

    .PARAMETER ExitOnFailure
    Exit PowerShell session if tests fail

    .PARAMETER SkipDependencies
    Skip dependency checking and installation

    .PARAMETER SkipImport
    Skip module import step

    .PARAMETER PassThru
    Return test results object

    .PARAMETER CICD
    Enable CI/CD mode with optimized settings and output

    .EXAMPLE
    # Basic usage - test current module
    Invoke-ModuleTestSuite

    .EXAMPLE
    # Test with additional modules and custom settings
    Invoke-ModuleTestSuite -AdditionalModules @('Pester', 'PSWriteColor') -SkipModules @('CertNoob') -EnableCodeCoverage

    .EXAMPLE
    # Test different project with minimal output
    Invoke-ModuleTestSuite -ProjectPath "C:\MyModule" -OutputFormat Minimal -Force

    .EXAMPLE
    # CI/CD optimized testing
    Invoke-ModuleTestSuite -CICD -EnableCodeCoverage

    .EXAMPLE
    # Get test results for further processing
    $results = Invoke-ModuleTestSuite -PassThru
    Write-Host "Test duration: $($results.Time)"
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$ProjectPath = $PSScriptRoot,

        [Parameter()]
        [string[]]$AdditionalModules = @('Pester', 'PSWriteColor'),

        [Parameter()]
        [string[]]$SkipModules = @(),

        [Parameter()]
        [string]$TestPath,

        [Parameter()]
        [ValidateSet('Detailed', 'Normal', 'Minimal')]
        [string]$OutputFormat = 'Detailed',

        [Parameter()]
        [switch]$EnableCodeCoverage,

        [Parameter()]
        [switch]$Force,

        [Parameter()]
        [switch]$ExitOnFailure,

        [Parameter()]
        [switch]$SkipDependencies,

        [Parameter()]
        [switch]$SkipImport,

        [Parameter()]
        [switch]$PassThru,

        [Parameter()]
        [switch]$CICD
    )

    try {
        # Apply CICD optimizations if requested
        if ($CICD.IsPresent) {
            $OutputFormat = 'Minimal'
            $ExitOnFailure = $true
            $PassThru = $true  # Always return results for CI/CD processing

            Write-Host "=== CI/CD Module Testing Pipeline ===" -ForegroundColor Magenta
        } else {
            Write-Host "=== PowerShell Module Test Suite ===" -ForegroundColor Magenta
        }

        Write-Host "Project Path: $ProjectPath" -ForegroundColor Cyan
        Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Cyan
        Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)" -ForegroundColor Cyan
        Write-Host

        # Step 1: Get module information
        Write-Host "Step 1: Gathering module information..." -ForegroundColor Yellow
        $moduleInfo = Get-ModuleInformation -Path $ProjectPath

        Write-Host "  Module Name: $($moduleInfo.ModuleName)" -ForegroundColor Green
        Write-Host "  Module Version: $($moduleInfo.ModuleVersion)" -ForegroundColor Green
        Write-Host "  Manifest Path: $($moduleInfo.ManifestPath)" -ForegroundColor Green
        Write-Host "  Required Modules: $(if ($moduleInfo.RequiredModules) { $moduleInfo.RequiredModules.Count } else { 0 })" -ForegroundColor Green
        Write-Host

        # Step 2: Handle dependencies
        if (-not $SkipDependencies.IsPresent) {
            Write-Host "Step 2: Checking and installing required modules..." -ForegroundColor Yellow
            Test-RequiredModules -ModuleInformation $moduleInfo -AdditionalModules $AdditionalModules -SkipModules $SkipModules -Force:$Force.IsPresent
            Write-Host
        } else {
            Write-Host "Step 2: Skipping dependency check (as requested)" -ForegroundColor Yellow
            Write-Host
        }

        # Step 3: Import the module
        if (-not $SkipImport.IsPresent) {
            Write-Host "Step 3: Importing module under test..." -ForegroundColor Yellow
            Test-ModuleImport -ModuleInformation $moduleInfo -Force:$Force.IsPresent -ShowInformation
            Write-Host
        } else {
            Write-Host "Step 3: Skipping module import (as requested)" -ForegroundColor Yellow
            Write-Host
        }

        # Step 4: Display required modules information
        Write-Host "Step 4: Module dependency summary..." -ForegroundColor Yellow
        if ($moduleInfo.RequiredModules -and $moduleInfo.RequiredModules.Count -gt 0) {
            Write-Host "Required modules:" -ForegroundColor Cyan
            foreach ($Module in $moduleInfo.RequiredModules) {
                if ($Module -is [System.Collections.IDictionary]) {
                    $versionInfo = ""
                    if ($Module.ModuleVersion) { $versionInfo += " (Min: $($Module.ModuleVersion))" }
                    if ($Module.RequiredVersion) { $versionInfo += " (Required: $($Module.RequiredVersion))" }
                    if ($Module.MaximumVersion) { $versionInfo += " (Max: $($Module.MaximumVersion))" }
                    Write-Host "  [>] $($Module.ModuleName)$versionInfo" -ForegroundColor Green
                } else {
                    Write-Host "  [>] $Module" -ForegroundColor Green
                }
            }
        } else {
            Write-Host "  No required modules specified in manifest" -ForegroundColor Gray
        }

        if ($AdditionalModules.Count -gt 0) {
            Write-Host "Additional modules:" -ForegroundColor Cyan
            foreach ($Module in $AdditionalModules) {
                if ($Module -notin $SkipModules) {
                    Write-Host "  [+] $Module" -ForegroundColor Green
                }
            }
        }
        Write-Host

        # Step 5: Run tests
        Write-Host "Step 5: Executing module tests..." -ForegroundColor Yellow

        $testParams = @{
            ModuleInformation  = $moduleInfo
            OutputFormat       = $OutputFormat
            EnableCodeCoverage = $EnableCodeCoverage.IsPresent
            ExitOnFailure      = $ExitOnFailure.IsPresent
            PassThru           = $true  # Always get results for our own processing
        }

        if ($TestPath) {
            $testParams.Remove('ModuleInformation')
            $testParams.TestPath = $TestPath
        }

        # Call the private function using the module scope
        $module = Get-Module -Name 'PSPublishModule'
        if (-not $module) {
            throw "PSPublishModule module is not loaded. Cannot access internal functions."
        }
        $testResults = & $module { 
            param($params)
            Invoke-ModuleTests @params 
        } -params $testParams

        Write-Host
        if ($CICD.IsPresent) {
            Write-Host "=== CI/CD Pipeline Completed Successfully ===" -ForegroundColor Green

            # Set CI/CD environment variables for common platforms
            if ($env:GITHUB_ACTIONS) {
                Write-Host "::set-output name=test-result::true"
                Write-Host "::set-output name=total-tests::$TotalCount"
                Write-Host "::set-output name=failed-tests::$FailedCount"
                if ($testResults.CodeCoverage) {
                    $coveragePercent = [math]::Round(($testResults.CodeCoverage.NumberOfCommandsExecuted / $testResults.CodeCoverage.NumberOfCommandsAnalyzed) * 100, 2)
                    Write-Host "::set-output name=code-coverage::$coveragePercent"
                }
            }

            if ($env:TF_BUILD) {
                Write-Host "##vso[task.setvariable variable=TestResult;isOutput=true]true"
                Write-Host "##vso[task.setvariable variable=TotalTests;isOutput=true]$TotalCount"
                Write-Host "##vso[task.setvariable variable=FailedTests;isOutput=true]$FailedCount"
            }
        } else {
            Write-Host "=== Test Suite Completed Successfully ===" -ForegroundColor Green
        }

        Write-Host "Module: $($moduleInfo.ModuleName) v$($moduleInfo.ModuleVersion)" -ForegroundColor Green

        # Handle different Pester versions result object properties for display
        $TotalCount = if ($null -ne $testResults.TotalCount) { $testResults.TotalCount } elseif ($testResults.Tests) { $testResults.Tests.Count } else { 0 }
        $PassedCount = if ($null -ne $testResults.PassedCount) { $testResults.PassedCount } elseif ($testResults.Tests) { ($testResults.Tests | Where-Object Result -EQ 'Passed').Count } else { 0 }
        $FailedCount = if ($null -ne $testResults.FailedCount) { $testResults.FailedCount } elseif ($testResults.Tests) { ($testResults.Tests | Where-Object Result -EQ 'Failed').Count } else { 0 }

        Write-Host "Tests: $PassedCount/$TotalCount passed" -ForegroundColor Green

        if ($testResults.Time) {
            Write-Host "Duration: $($testResults.Time)" -ForegroundColor Green
        }

        if ($PassThru.IsPresent) {
            return $testResults
        }

    } catch {
        Write-Host
        if ($CICD.IsPresent) {
            Write-Host "=== CI/CD Pipeline Failed ===" -ForegroundColor Red

            # Set failure environment variables
            if ($env:GITHUB_ACTIONS) {
                Write-Host "::set-output name=test-result::false"
                Write-Host "::set-output name=error-message::$($_.Exception.Message)"
            }

            if ($env:TF_BUILD) {
                Write-Host "##vso[task.setvariable variable=TestResult;isOutput=true]false"
                Write-Host "##vso[task.setvariable variable=ErrorMessage;isOutput=true]$($_.Exception.Message)"
            }
        } else {
            Write-Host "=== Test Suite Failed ===" -ForegroundColor Red
        }

        Write-Error "Module test suite failed: $($_.Exception.Message)"

        if ($ExitOnFailure.IsPresent) {
            exit 1
        } else {
            throw
        }
    }
}

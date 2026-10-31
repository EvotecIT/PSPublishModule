<#
.SYNOPSIS
Maps reusable workflow inputs to the shared PowerForge test command.
.DESCRIPTION
The CLI owns Pester installation, host selection, results and failure reporting.
The workflow owns checkout, optional build, artifact upload and job-summary display.
.EXAMPLE
./.github/scripts/Invoke-ModuleTestCI.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ($env:MODULE_TEST_SHELL -notin @('pwsh', 'powershell')) {
    throw 'matrix.shell must be pwsh or powershell.'
}
if ($env:MODULE_TEST_SHELL -eq 'powershell' -and -not $IsWindows) {
    throw 'Windows PowerShell 5.1 requires a Windows runner.'
}
$results = Join-Path $env:RUNNER_TEMP 'powerforge-module-test-results'
$tool = Join-Path $PSScriptRoot '../../PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll'
$arguments = @(
    $tool, 'test',
    '--project-root', $env:MODULE_PROJECT_PATH,
    '--test-path', $env:MODULE_TEST_PATH,
    '--pester-version', $env:MODULE_PESTER_VERSION,
    '--results-path', (Join-Path $results 'nunit.xml'),
    '--junit-results-path', (Join-Path $results 'junit.xml'),
    '--summary-path', (Join-Path $results 'summary.md'),
    '--additional-modules', 'Pester',
    '--skip-import', '--fail-if-no-tests',
    '--timeout-seconds', $env:MODULE_TEST_TIMEOUT
)
if ($env:MODULE_TEST_SHELL -eq 'powershell') {
    $arguments += '--windows-powershell'
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Module tests failed with exit code $LASTEXITCODE." }

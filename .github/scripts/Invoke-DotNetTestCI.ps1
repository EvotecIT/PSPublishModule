<#
.SYNOPSIS
Maps reusable workflow inputs to the shared .NET test engine.
.DESCRIPTION
PowerForge owns prerequisite builds, xUnit/TRX execution, native AOT smoke runs and project probe processes.
.EXAMPLE
./.github/scripts/Invoke-DotNetTestCI.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot '../../PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll'
$results = Join-Path $env:RUNNER_TEMP 'powerforge-dotnet-test-results'
if ($env:DOTNET_TEST_RESULTS_PATH) { $results = $env:DOTNET_TEST_RESULTS_PATH }
$arguments = @(
    $tool, 'test-dotnet', '--config', $env:DOTNET_TEST_CONFIG,
    '--profile', $env:DOTNET_TEST_PROFILE, '--results-path', $results
)
if ($env:DOTNET_TEST_FRAMEWORK) { $arguments += @('--framework', $env:DOTNET_TEST_FRAMEWORK) }
if ($env:DOTNET_TEST_RUNTIME) { $arguments += @('--runtime', $env:DOTNET_TEST_RUNTIME) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw ".NET validation failed with exit code $LASTEXITCODE." }

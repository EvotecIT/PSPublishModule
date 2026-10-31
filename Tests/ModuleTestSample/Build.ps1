<#
.SYNOPSIS
Validates the sample manifest in the workflow's selected build shell.
.EXAMPLE
./Tests/ModuleTestSample/Build.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
# The dependency exists only on the install host's custom path. The strict Desktop
# child must receive that path from the test engine, while keeping its native paths.
$dependencyRoot = Join-Path $env:RUNNER_TEMP 'module-test-sample-dependencies'
$dependencyTarget = Join-Path $dependencyRoot 'ModuleTestSampleDependency/1.0.0'
New-Item -ItemType Directory -Path $dependencyTarget -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot '../ModuleTestSampleDependency/*') -Destination $dependencyTarget
$env:PSModulePath = $dependencyRoot + [IO.Path]::PathSeparator + $env:PSModulePath
if ($env:GITHUB_ENV) {
    "PSModulePath=$env:PSModulePath" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
}
Test-ModuleManifest -Path (Join-Path $PSScriptRoot 'ModuleTestSample.psd1') -ErrorAction Stop | Out-Null
Write-Host "Sample build: $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)"

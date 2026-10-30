#Requires -Version 7.0
<#
.SYNOPSIS
Validates SDK setup selection and checks a caller-configured SDK before restore.
.PARAMETER SetupDotNet
The action's strict lowercase true/false input. True delegates to SDK setup.
.PARAMETER Restore
Checks the existing SDK only for restore mode; scans and uploads need no SDK.
#>
[CmdletBinding()]
param(
    [AllowEmptyString()][string] $SetupDotNet = 'true',
    [switch] $Restore
)

$ErrorActionPreference = 'Stop'
if ($SetupDotNet -cnotin @('true', 'false')) {
    throw "setup-dotnet must be 'true' or 'false'."
}
if (!$Restore -or $SetupDotNet -ceq 'true') { return }

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (!$dotnet) { throw 'setup-dotnet is false but dotnet is not available on PATH.' }
$version = (& $dotnet.Source --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -cnotmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw 'setup-dotnet is false but dotnet --version did not identify an installed SDK.'
}
Write-Host "Using existing .NET SDK $version."

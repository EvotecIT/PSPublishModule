[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Start', 'Stop')] [string] $Operation,
    [Parameter(Mandatory)] [string] $ScenarioRoot,
    [Parameter(Mandatory)] [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
$name = 'Test-ClassicLocksmith.ps1'
$destination = Join-Path (Split-Path -LiteralPath $ScenarioRoot) $name
$result = [ordered]@{ schemaVersion = 1; operation = $Operation; fixtureIdentity = 'classic-directory-module' }
if ($Operation -eq 'Start') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-Module.ps1') -Destination $destination
    $result.testArguments = @()
    $result.moduleTest = [ordered]@{
        script = $name
        sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        parameters = @{}
        workingDirectory = '.'
    }
} else {
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        Remove-Item -LiteralPath $destination
    }
    $result.stopped = $true
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM

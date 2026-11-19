[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Start', 'Stop')] [string] $Operation,
    [Parameter(Mandatory)] [string] $ScenarioRoot,
    [Parameter(Mandatory)] [string] $ResultPath
)
$ErrorActionPreference = 'Stop'
$target = $env:POWERFORGE_EXTERNAL_AD_AUDIT_TARGET
$name = 'Test-Locksmith2.ps1'
$destination = Join-Path (Split-Path -LiteralPath $ScenarioRoot) $name
$result = [ordered]@{ schemaVersion = 1; operation = $Operation; fixtureIdentity = 'read-only-directory-audit' }
if ($Operation -eq 'Start') {
    if ([Uri]::CheckHostName($target) -ne [UriHostNameType]::Dns -or $target -notmatch '\.') {
        throw 'Set POWERFORGE_EXTERNAL_AD_AUDIT_TARGET to the DNS name of an approved read-only AD and AD CS lab.'
    }
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
        Remove-Item -LiteralPath $destination -Force
    }
    $result.stopped = $true
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM

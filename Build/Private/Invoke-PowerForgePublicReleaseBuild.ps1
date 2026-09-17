[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$BuildScript = [Environment]::GetEnvironmentVariable('POWERFORGE_RELEASE_BUILD_SCRIPT', 'Process')
$RequestPath = [Environment]::GetEnvironmentVariable('POWERFORGE_RELEASE_BUILD_REQUEST', 'Process')
if ([string]::IsNullOrWhiteSpace($BuildScript) -or
    [string]::IsNullOrWhiteSpace($RequestPath)) {
    throw 'The isolated public-release build process did not receive its script and request paths.'
}
$parameters = Import-Clixml -LiteralPath $RequestPath
$global:LASTEXITCODE = 0
& $BuildScript @parameters 2>&1 | ForEach-Object {
    [Console]::Out.WriteLine([string] $_)
}
$exitCode = $LASTEXITCODE
exit $exitCode

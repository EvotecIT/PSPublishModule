[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string] $Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

if ($env:RUNNER_OS -ne 'Linux') {
    throw 'PowerForge Cloudflare cache policy requires a Linux runner.'
}
if ([string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_API_TOKEN)) {
    throw 'api-token is required.'
}
if ($env:POWERFORGE_CLOUDFLARE_ZONE_ID -notmatch '^[a-fA-F0-9]{32}$') {
    throw 'zone-id must be a 32-character Cloudflare zone identifier.'
}
if ($env:POWERFORGE_CLOUDFLARE_DRY_RUN -notin @('true', 'false')) {
    throw 'dry-run must be true or false.'
}

$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspacePrefix = $workspace + [IO.Path]::DirectorySeparatorChar
$siteConfig = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_CLOUDFLARE_SITE_CONFIG))
if (-not $siteConfig.StartsWith($workspacePrefix, [StringComparison]::Ordinal) -or
    -not (Test-Path -LiteralPath $siteConfig -PathType Leaf)) {
    throw 'site-config must identify a file inside the caller repository.'
}

$engineRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_ACTION_PATH '../../..'))
$project = Join-Path $engineRoot 'PowerForge.Web.Cli/PowerForge.Web.Cli.csproj'
$cli = Join-Path $engineRoot 'PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll'

dotnet build $project --configuration Release --framework net10.0 --nologo --verbosity minimal
Assert-LastExitCode -Operation 'Building PowerForge.Web CLI'

$arguments = @(
    $cli,
    'cloudflare',
    'cache-policy',
    'apply',
    '--zone-id', $env:POWERFORGE_CLOUDFLARE_ZONE_ID,
    '--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN',
    '--site-config', $siteConfig
)
if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_HOSTNAME)) {
    $arguments += @('--hostname', $env:POWERFORGE_CLOUDFLARE_HOSTNAME)
}
if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_POLICY_NAME)) {
    $arguments += @('--policy-name', $env:POWERFORGE_CLOUDFLARE_POLICY_NAME)
}
if ($env:POWERFORGE_CLOUDFLARE_DRY_RUN -eq 'true') {
    $arguments += '--dry-run'
}

dotnet @arguments
Assert-LastExitCode -Operation 'Applying Cloudflare cache policy'

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($env:RUNNER_OS -ne 'Linux') {
    throw 'PowerForge Cloudflare incremental purge requires a Linux runner.'
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

$currentManifest = [IO.Path]::GetFullPath($env:POWERFORGE_CLOUDFLARE_CURRENT_MANIFEST)
if (-not (Test-Path -LiteralPath $currentManifest -PathType Leaf)) {
    throw "Current deployment manifest was not found: $currentManifest"
}

$engineRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_ACTION_PATH '../../..'))
$cli = Join-Path $engineRoot 'PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw "Built PowerForge.Web CLI was not found: $cli"
}

$arguments = @(
    $cli,
    'cloudflare',
    'purge',
    '--zone-id', $env:POWERFORGE_CLOUDFLARE_ZONE_ID,
    '--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN',
    '--site-config', $siteConfig,
    '--current-manifest', $currentManifest
)

$previousManifest = [IO.Path]::GetFullPath($env:POWERFORGE_CLOUDFLARE_PREVIOUS_MANIFEST)
if (Test-Path -LiteralPath $previousManifest -PathType Leaf) {
    $arguments += @('--previous-manifest', $previousManifest)
}
if ($env:POWERFORGE_CLOUDFLARE_DRY_RUN -eq 'true') {
    $arguments += '--dry-run'
}

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Applying the incremental Cloudflare purge failed with exit code $LASTEXITCODE."
}

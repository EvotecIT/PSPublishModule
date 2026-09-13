$ErrorActionPreference = 'Stop'

$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspacePrefix = $workspace + [IO.Path]::DirectorySeparatorChar
$siteConfig = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_CLOUDFLARE_SITE_CONFIG))
if (-not $siteConfig.StartsWith($workspacePrefix, [StringComparison]::Ordinal) -or
    -not (Test-Path -LiteralPath $siteConfig -PathType Leaf)) {
    throw 'site-config must identify a file inside the caller repository.'
}
$engineRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_ACTION_PATH '../../..'))

$arguments = @(
    'run', '--no-build', '--no-restore', '--configuration', 'Release', '--framework', 'net10.0',
    '--project', $env:POWERFORGE_CLOUDFLARE_CLI_PROJECT, '--',
    'cloudflare', 'purge',
    '--zone-id', $env:POWERFORGE_CLOUDFLARE_ZONE_ID,
    '--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN',
    '--site-config', $siteConfig
)
if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_HOSTNAME)) {
    $arguments += @('--hostname', $env:POWERFORGE_CLOUDFLARE_HOSTNAME)
}
if ($env:POWERFORGE_CLOUDFLARE_DRY_RUN -eq 'true') {
    $arguments += '--dry-run'
}

Push-Location $engineRoot
try {
    dotnet @arguments
    $cliExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($cliExitCode -ne 0) {
    throw "Purging the configured Cloudflare scope after policy application failed with exit code $cliExitCode."
}

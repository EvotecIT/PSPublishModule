$ErrorActionPreference = 'Stop'

$arguments = @(
    'run', '--no-build', '--no-restore', '--configuration', 'Release', '--framework', 'net10.0',
    '--project', $env:POWERFORGE_CLOUDFLARE_CLI_PROJECT, '--',
    'cloudflare', 'purge',
    '--zone-id', $env:POWERFORGE_CLOUDFLARE_ZONE_ID,
    '--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN',
    '--site-config', $env:POWERFORGE_CLOUDFLARE_SITE_CONFIG
)
if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_HOSTNAME)) {
    $arguments += @('--hostname', $env:POWERFORGE_CLOUDFLARE_HOSTNAME)
}
if ($env:POWERFORGE_CLOUDFLARE_DRY_RUN -eq 'true') {
    $arguments += '--dry-run'
}

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Purging the configured Cloudflare scope after policy application failed with exit code $LASTEXITCODE."
}

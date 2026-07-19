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

foreach ($requiredName in @(
        'POWERFORGE_CLOUDFLARE_API_TOKEN',
        'POWERFORGE_CLOUDFLARE_ZONE_NAME',
        'POWERFORGE_CLOUDFLARE_DNS_NAME',
        'POWERFORGE_CLOUDFLARE_DNS_CONTENT')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($requiredName))) {
        throw "$requiredName is required."
    }
}
if ($env:POWERFORGE_CLOUDFLARE_DNS_PROXIED -notin @('true', 'false')) {
    throw 'proxied must be true or false.'
}
if ($env:POWERFORGE_CLOUDFLARE_DNS_DRY_RUN -notin @('true', 'false')) {
    throw 'dry-run must be true or false.'
}

$engineRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_ACTION_PATH '../../..'))
$project = Join-Path $engineRoot 'PowerForge.Web.Cli/PowerForge.Web.Cli.csproj'
$cli = Join-Path $engineRoot 'PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll'

dotnet build $project --configuration Release --framework net10.0 --nologo --verbosity minimal
Assert-LastExitCode -Operation 'Building PowerForge.Web CLI'

$arguments = @(
    $cli,
    'cloudflare',
    'dns-record',
    'apply',
    '--zone-name', $env:POWERFORGE_CLOUDFLARE_ZONE_NAME,
    '--record-name', $env:POWERFORGE_CLOUDFLARE_DNS_NAME,
    '--record-content', $env:POWERFORGE_CLOUDFLARE_DNS_CONTENT,
    '--record-type', $env:POWERFORGE_CLOUDFLARE_DNS_TYPE,
    '--proxied', $env:POWERFORGE_CLOUDFLARE_DNS_PROXIED,
    '--ttl', $env:POWERFORGE_CLOUDFLARE_DNS_TTL,
    '--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN'
)
if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_DNS_COMMENT)) {
    $arguments += @('--comment', $env:POWERFORGE_CLOUDFLARE_DNS_COMMENT)
}
if ($env:POWERFORGE_CLOUDFLARE_DNS_DRY_RUN -eq 'true') {
    $arguments += '--dry-run'
}

dotnet @arguments
Assert-LastExitCode -Operation 'Reconciling Cloudflare DNS record'

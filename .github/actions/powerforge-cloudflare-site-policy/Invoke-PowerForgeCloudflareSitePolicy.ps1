[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($env:RUNNER_OS -ne 'Linux') {
    throw 'PowerForge Cloudflare site policy requires a Linux runner.'
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
if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    throw 'GITHUB_OUTPUT is required.'
}

$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspacePrefix = $workspace + [IO.Path]::DirectorySeparatorChar
$siteConfig = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_CLOUDFLARE_SITE_CONFIG))
if (-not $siteConfig.StartsWith($workspacePrefix, [StringComparison]::Ordinal) -or
    -not (Test-Path -LiteralPath $siteConfig -PathType Leaf)) {
    throw 'site-config must identify a file inside the caller repository.'
}

$engineRoot = [IO.Path]::GetFullPath((Join-Path $env:GITHUB_ACTION_PATH '../../..'))
$cli = Join-Path $engineRoot 'PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw "Built PowerForge.Web CLI was not found: $cli"
}

$arguments = @(
    $cli,
    'cloudflare',
    'site-policy',
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
if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLOUDFLARE_BASE_PATH)) {
    $arguments += @('--base-path', $env:POWERFORGE_CLOUDFLARE_BASE_PATH)
}
if ($env:POWERFORGE_CLOUDFLARE_DRY_RUN -eq 'true') {
    $arguments += '--dry-run'
}
$arguments += @('--output', 'json')

$jsonOutput = [object[]] @(dotnet @arguments)
$cliExitCode = $LASTEXITCODE
$jsonText = $jsonOutput -join [Environment]::NewLine
if ($cliExitCode -ne 0) {
    if (-not [string]::IsNullOrWhiteSpace($jsonText)) {
        Write-Host $jsonText
    }
    throw "Applying Cloudflare site policy failed with exit code $cliExitCode."
}
try {
    $result = $jsonText | ConvertFrom-Json
    if ($result.success -ne $true -or $null -eq $result.result.changesRequired) {
        throw 'The site-policy result did not contain the required reconciliation state.'
    }
} catch {
    throw "Applying Cloudflare site policy returned invalid JSON output: $($_.Exception.Message)"
}
Write-Host ([string]$result.result.message)
"changes_required=$(([bool]$result.result.changesRequired).ToString().ToLowerInvariant())" |
    Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append

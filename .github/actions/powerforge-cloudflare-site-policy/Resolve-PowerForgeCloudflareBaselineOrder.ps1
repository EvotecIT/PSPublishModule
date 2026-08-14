[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-PositiveInt64 {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Value
    )

    [long] $number = 0
    if (-not [long]::TryParse($Value, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref] $number) -or $number -le 0) {
        throw "$Name must be a positive integer."
    }
    $number
}

function Write-Decision {
    param(
        [Parameter(Mandatory)][bool] $Stale,
        [Parameter(Mandatory)][bool] $UsePrevious,
        [Parameter(Mandatory)][string] $Reason
    )

    "stale=$($Stale.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "use_previous=$($UsePrevious.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "reason=$Reason" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    throw 'GITHUB_OUTPUT is required.'
}

$deploymentRunId = Get-PositiveInt64 -Name 'deployment-run-id' -Value $env:POWERFORGE_DEPLOYMENT_RUN_ID
$deploymentRunAttempt = Get-PositiveInt64 -Name 'deployment-run-attempt' -Value $env:POWERFORGE_DEPLOYMENT_RUN_ATTEMPT
$deploymentReceipt = [IO.Path]::GetFullPath($env:POWERFORGE_CLOUDFLARE_DEPLOYMENT_RECEIPT)
$previousManifest = [IO.Path]::GetFullPath($env:POWERFORGE_CLOUDFLARE_PREVIOUS_MANIFEST)
$baselineState = [IO.Path]::GetFullPath($env:POWERFORGE_CLOUDFLARE_BASELINE_STATE)
if (-not (Test-Path -LiteralPath $deploymentReceipt -PathType Leaf)) {
    throw 'The latest GitHub Pages deployment-order receipt is unavailable.'
}

try {
    $deployed = Get-Content -LiteralPath $deploymentReceipt -Raw | ConvertFrom-Json
    if ([int] $deployed.schemaVersion -ne 1) {
        throw "unsupported schema version '$($deployed.schemaVersion)'"
    }
    $latestRunId = Get-PositiveInt64 -Name 'latest deployment run id' -Value ([string] $deployed.deploymentRunId)
    $latestRunAttempt = Get-PositiveInt64 -Name 'latest deployment run attempt' -Value ([string] $deployed.deploymentRunAttempt)
} catch {
    throw "The latest GitHub Pages deployment-order receipt is invalid: $($_.Exception.Message)"
}

if ($latestRunId -ne $deploymentRunId -or $latestRunAttempt -ne $deploymentRunAttempt) {
    Write-Warning "Skipping stale Cloudflare policy job for deployment run $deploymentRunId attempt $deploymentRunAttempt because the latest Pages deployment is run $latestRunId attempt $latestRunAttempt."
    Write-Decision -Stale $true -UsePrevious $false -Reason 'a different GitHub Pages deployment is currently active'
    exit 0
}

if (-not (Test-Path -LiteralPath $previousManifest -PathType Leaf)) {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'no previous manifest is available'
    exit 0
}
if (-not (Test-Path -LiteralPath $baselineState -PathType Leaf)) {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'the previous baseline has no deployment-order receipt'
    exit 0
}

try {
    $state = Get-Content -LiteralPath $baselineState -Raw | ConvertFrom-Json
    if ([int] $state.schemaVersion -ne 1) {
        throw "unsupported schema version '$($state.schemaVersion)'"
    }
    [void] (Get-PositiveInt64 -Name 'baseline deployment run id' -Value ([string] $state.deploymentRunId))
    [void] (Get-PositiveInt64 -Name 'baseline deployment run attempt' -Value ([string] $state.deploymentRunAttempt))
} catch {
    Write-Decision -Stale $false -UsePrevious $false -Reason 'the previous baseline deployment-order receipt is invalid'
    exit 0
}

Write-Decision -Stale $false -UsePrevious $true -Reason 'the previous baseline is ordered for this deployment'

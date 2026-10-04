param(
    [Parameter(Mandatory)][ValidateSet('Prepare', 'Reserve', 'Submit')][string] $Action,
    [Parameter(Mandatory)][string] $ToolPath,
    [Parameter(Mandatory)][string] $ProfilePath,
    [Parameter(Mandatory)][string] $ReleaseTag,
    [Parameter(Mandatory)][string] $DeliveryReleaseId,
    [Parameter(Mandatory)][string] $OutputPath,
    [ValidateSet('winget', 'store', 'all')][string] $Channel = 'all',
    [string] $ManifestName = 'release-manifest.json',
    [string] $ChecksumsName = 'SHA256SUMS.txt'
)

$ErrorActionPreference = 'Stop'
$profileFullPath = (Resolve-Path -LiteralPath $ProfilePath).Path
$profile = Get-Content -LiteralPath $profileFullPath -Raw | ConvertFrom-Json
$identity = "$env:GITHUB_REPOSITORY`n$ProfilePath`n$ReleaseTag"
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity))).ToLowerInvariant()
$artifactPrefix = "catalog-update-$($digest.Substring(0, 24))-"
$receiptPath = Join-Path $OutputPath 'catalog-update.json'
. "$PSScriptRoot/catalog-update-receipt.ps1"
function Invoke-Catalog([string[]] $Arguments) {
    & dotnet $ToolPath release catalog @Arguments --config $profileFullPath --out $OutputPath
    if ($LASTEXITCODE -ne 0) { throw "Catalog command failed: $($Arguments[0]). Inspect the retained receipt and workflow log." }
}

if ($Action -eq 'Prepare') {
    if ($ReleaseTag -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$') { throw 'Use a release tag containing letters, numbers, dots, underscores and hyphens.' }
    foreach ($name in @($ManifestName, $ChecksumsName)) {
        if ($name -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$') { throw 'Release metadata names must be safe basenames.' }
    }
    $previous = Get-TrustedCatalogArtifact $artifactPrefix
    $restoreRoot = "$OutputPath-restored"
    if (Test-Path -LiteralPath $restoreRoot) { throw 'Catalog restore scratch directory already exists.' }
    if ($previous) {
        & gh run download $previous.workflow_run.id --repo $env:GITHUB_REPOSITORY --name $previous.name --dir $restoreRoot
        if ($LASTEXITCODE -ne 0) { throw 'Cannot restore the previous catalog receipt.' }
    }
    try {
        $assetRoot = "$OutputPath-assets"
        if (Test-Path -LiteralPath $assetRoot) { throw 'Catalog asset scratch directory already exists.' }
        New-Item -ItemType Directory -Path $assetRoot | Out-Null
        try {
            & gh release download $ReleaseTag --repo $env:GITHUB_REPOSITORY --dir $assetRoot --pattern '*.msi' --pattern $ManifestName --pattern $ChecksumsName
            if ($LASTEXITCODE -ne 0) { throw 'Cannot download the published signed release assets.' }
            $prepare = @('prepare', '--manifest', (Join-Path $assetRoot $ManifestName), '--checksums', (Join-Path $assetRoot $ChecksumsName),
                '--asset-root', $assetRoot, '--delivery-release', $DeliveryReleaseId)
            if ($previous) { $prepare += @('--resume-from', $restoreRoot) }
            Invoke-Catalog -Arguments $prepare
            if ($previous -and (Test-Path -LiteralPath (Join-Path $restoreRoot 'workflow-history.json'))) {
                Copy-Item -LiteralPath (Join-Path $restoreRoot 'workflow-history.json') -Destination $OutputPath
            }
            Confirm-CatalogHistory $OutputPath
        } finally {
            # This sibling was created empty above and holds only downloads from this invocation.
            Remove-Item -LiteralPath $assetRoot -Recurse
        }
    } finally { if (Test-Path -LiteralPath $restoreRoot) { Remove-Item -LiteralPath $restoreRoot -Recurse } }
    $preflight = @('submit', '--channel', $Channel)
    if ($env:CATALOG_EXECUTE -eq 'true') { $preflight += '--require-authentication' }
    Invoke-Catalog -Arguments $preflight
    "artifact-prefix=$artifactPrefix" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
} elseif ($Action -eq 'Reserve') {
    $reservationKey = [Guid]::NewGuid().ToString('N')
    Invoke-Catalog -Arguments @('reserve', '--channel', $Channel, '--reservation-key', $reservationKey)
    "reservation-key=$reservationKey" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
} else {
    if ([string]::IsNullOrWhiteSpace($env:CATALOG_RESERVATION_KEY)) { throw 'Missing archived catalog reservation.' }
    Invoke-Catalog -Arguments @('submit', '--channel', $Channel, '--execute', '--reservation-key', $env:CATALOG_RESERVATION_KEY)
}

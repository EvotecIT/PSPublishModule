[CmdletBinding()]
param(
    [string] $PinPath,
    [string] $OutputPath,
    [string] $ObservedRefsPath,
    [string] $RemoteUrl = 'https://github.com/PowerShell/PowerShell.git',
    [switch] $FailOnChange
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PinPath)) {
    $PinPath = Join-Path $repositoryRoot 'PowerForge/Resources/PowerShellCompilation/SemanticOracle/host-artifact-pins.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot 'artifacts/powershell-semantic-upstream-review.json'
}
$PinPath = [System.IO.Path]::GetFullPath($PinPath)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ($PinPath.Equals($OutputPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath must not resolve to the immutable semantic host-pin document.'
}
if (-not (Test-Path -LiteralPath $PinPath -PathType Leaf)) {
    throw "Semantic host-pin document was not found: $PinPath"
}

function Add-ObservedRef {
    param(
        [System.Collections.Generic.Dictionary[string, string]] $Map,
        [string] $Tag,
        [string] $Commit,
        [bool] $Peeled
    )
    if ($Tag -notmatch '^v\d+\.\d+\.\d+$' -or $Commit -notmatch '^[0-9a-fA-F]{40}$') { return }
    if ($Peeled -or -not $Map.ContainsKey($Tag)) {
        $Map[$Tag] = $Commit.ToLowerInvariant()
    }
}

$observed = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
if (-not [string]::IsNullOrWhiteSpace($ObservedRefsPath)) {
    $observedDocument = Get-Content -LiteralPath ([System.IO.Path]::GetFullPath($ObservedRefsPath)) -Raw | ConvertFrom-Json
    foreach ($item in @($observedDocument)) {
        Add-ObservedRef -Map $observed -Tag ([string] $item.Tag) -Commit ([string] $item.Commit) -Peeled $true
    }
} else {
    $remoteLines = @(& git ls-remote --tags $RemoteUrl 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to query PowerShell release tags from '$RemoteUrl': $($remoteLines -join [Environment]::NewLine)"
    }
    foreach ($line in $remoteLines) {
        if ($line -notmatch '^([0-9a-fA-F]{40})\s+refs/tags/(v\d+\.\d+\.\d+)(\^\{\})?$') { continue }
        Add-ObservedRef -Map $observed -Tag $Matches[2] -Commit $Matches[1] -Peeled (-not [string]::IsNullOrEmpty($Matches[3]))
    }
}

$pinDocument = Get-Content -LiteralPath $PinPath -Raw | ConvertFrom-Json
if ([int] $pinDocument.SchemaVersion -ne 1) {
    throw "Unsupported semantic host-pin schema '$($pinDocument.SchemaVersion)'."
}
$profiles = [System.Collections.Generic.List[object]]::new()
$reviewRequests = [System.Collections.Generic.List[object]]::new()
foreach ($pin in @($pinDocument.Pins | Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_.TrackedTagPrefix) })) {
    $prefix = [string] $pin.TrackedTagPrefix
    $candidates = foreach ($pair in $observed.GetEnumerator()) {
        if (-not $pair.Key.StartsWith($prefix, [System.StringComparison]::Ordinal)) { continue }
        $parsedVersion = [version]::new()
        if (-not [version]::TryParse($pair.Key.Substring(1), [ref] $parsedVersion)) { continue }
        [pscustomobject] @{ Tag = $pair.Key; Commit = $pair.Value; Version = $parsedVersion }
    }
    $latest = $candidates | Sort-Object Version -Descending | Select-Object -First 1
    if ($null -eq $latest) {
        throw "No stable upstream tag matched '$prefix' for profile '$($pin.ProfileId)'."
    }
    $changed = -not $latest.Tag.Equals([string] $pin.ReleaseTag, [System.StringComparison]::Ordinal) -or
               -not $latest.Commit.Equals([string] $pin.UpstreamCommit, [System.StringComparison]::OrdinalIgnoreCase)
    $entry = [pscustomobject] [ordered] @{
        ProfileId = [string] $pin.ProfileId
        Status = if ($changed) { 'ReviewRequired' } else { 'Current' }
        PinnedTag = [string] $pin.ReleaseTag
        PinnedCommit = [string] $pin.UpstreamCommit
        ObservedTag = [string] $latest.Tag
        ObservedCommit = [string] $latest.Commit
        AffectedCaseIds = @($pin.ReviewedCaseIds)
    }
    $profiles.Add($entry)
    if ($changed) { $reviewRequests.Add($entry) }
}

$report = [pscustomobject] [ordered] @{
    SchemaVersion = 1
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O', [System.Globalization.CultureInfo]::InvariantCulture)
    RemoteUrl = $RemoteUrl
    PinDocumentSha256 = (Get-FileHash -LiteralPath $PinPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Profiles = @($profiles)
    ReviewRequests = @($reviewRequests)
}
$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
[System.IO.File]::WriteAllText(
    $OutputPath,
    ($report | ConvertTo-Json -Depth 8),
    [System.Text.UTF8Encoding]::new($false))

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $summary = [System.Collections.Generic.List[string]]::new()
    $summary.Add('## PowerShell semantic upstream review')
    $summary.Add('')
    $summary.Add('| Profile | Status | Pinned | Observed |')
    $summary.Add('| --- | --- | --- | --- |')
    foreach ($profile in $profiles) {
        $summary.Add("| $($profile.ProfileId) | $($profile.Status) | $($profile.PinnedTag) | $($profile.ObservedTag) |")
    }
    [System.IO.File]::AppendAllLines($env:GITHUB_STEP_SUMMARY, $summary, [System.Text.UTF8Encoding]::new($false))
}

Write-Output $OutputPath
if ($FailOnChange -and $reviewRequests.Count -gt 0) {
    throw "$($reviewRequests.Count) semantic profile release line(s) require review. Immutable pins were not changed."
}

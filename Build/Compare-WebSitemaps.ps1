[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $LegacySitemapUrls,

    [Parameter(Mandatory)]
    [string] $NewSitemapPath,

    [string[]] $AdditionalLegacyUrls,

    [string] $OutputJsonPath,

    [string] $OutputCsvPath,

    [string] $OutputRedirectCsvPath,

    [string] $OutputReviewCsvPath,

    [switch] $DiscoverAmpHtml,

    [switch] $IncludeAmpListingRoots,

    [ValidateRange(1, 300)]
    [int] $TimeoutSec = 30,

    [Nullable[int]] $FetchTimeoutSec = $null,

    [ValidateRange(0, 64)]
    [int] $MaxSitemapDepth = 4,

    [switch] $PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if ($null -ne $FetchTimeoutSec) {
    if ($FetchTimeoutSec.Value -lt 1) {
        throw "FetchTimeoutSec must be greater than zero."
    }

    $TimeoutSec = $FetchTimeoutSec.Value
}

$helperPath = Join-Path $PSScriptRoot 'Compare-WebSitemaps.Helpers.ps1'
if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "Compare-WebSitemaps helper file not found: $helperPath"
}
. $helperPath

$legacyUrls = [System.Collections.Generic.List[string]]::new()
foreach ($legacySitemapUrl in $LegacySitemapUrls) {
    foreach ($legacyUrl in (Import-SitemapUrls -Url $legacySitemapUrl)) {
        $legacyUrls.Add($legacyUrl)
    }
}

foreach ($legacyUrl in ($AdditionalLegacyUrls | Where-Object { $_ })) {
    $legacyUrls.Add($legacyUrl)
}

$legacyUnique = $legacyUrls |
    Where-Object { $_ } |
    ForEach-Object { $_.Trim() } |
    Sort-Object -Unique

$newUnique = Import-LocalSitemapUrls -Path $NewSitemapPath |
    Where-Object { $_ } |
    ForEach-Object { $_.Trim() } |
    Sort-Object -Unique

$newSiteRoot = Split-Path -Parent ([System.IO.Path]::GetFullPath($NewSitemapPath))
$newLookup = New-UrlLookup -Urls $newUnique
$pathAliasLookup = New-PathAliasLookup -UrlLookup $newLookup
$legacyNormalized = $legacyUnique | ForEach-Object { Get-NormalizedUrl -Url $_ } | Sort-Object -Unique
$newNormalized = $newUnique | ForEach-Object { Get-NormalizedUrl -Url $_ } | Sort-Object -Unique

$missingLegacy = foreach ($legacyUrl in $legacyUnique) {
    $normalized = Get-NormalizedUrl -Url $legacyUrl
    if ($normalized -notin $newNormalized) {
        Get-RedirectCandidate -LegacyUrl $legacyUrl -NewLookup $newLookup -PathAliasLookup $pathAliasLookup -NewSiteRoot $newSiteRoot
    }
}

$redirectExports = [System.Collections.Generic.List[object]]::new()
foreach ($candidate in ($missingLegacy | Where-Object { $_ -and -not $_.NeedsReview -and $_.TargetUrl })) {
    $redirectExports.Add((New-RedirectExportRow -LegacyUrl $candidate.LegacyUrl -TargetUrl $candidate.TargetUrl -MatchKind $candidate.MatchKind -Notes $candidate.Notes))
}

foreach ($candidate in ($missingLegacy | Where-Object { $_ -and (Test-SyntheticAmpRedirectCandidate -Candidate $_) })) {
    $ampLegacyUrl = Get-SyntheticAmpLegacyUrl -LegacyUrl ([string] $candidate.LegacyUrl)
    $targetUrl = Get-SafeTargetUrl -Candidate $candidate
    if ($ampLegacyUrl -and $targetUrl) {
        $redirectExports.Add((New-RedirectExportRow -LegacyUrl $ampLegacyUrl -TargetUrl $targetUrl -MatchKind ('synthetic-amp-to-' + [string] $candidate.MatchKind) -Notes 'Synthetic AMP continuity redirect generated from the resolved canonical legacy route.'))
    }
}

$reviewExports = [System.Collections.Generic.List[object]]::new()
foreach ($candidate in ($missingLegacy | Where-Object { $_ -and ($_.NeedsReview -or [string]::IsNullOrWhiteSpace([string] $_.TargetUrl)) })) {
    $reviewExports.Add([pscustomobject] @{
        legacy_url = $candidate.LegacyUrl
        target_url = $candidate.TargetUrl
        match_kind = $candidate.MatchKind
        notes = $candidate.Notes
    })
}

$ampDiscovery = [System.Collections.Generic.List[object]]::new()
if ($DiscoverAmpHtml) {
    $candidateBySource = @{}
    foreach ($candidate in $missingLegacy) {
        $candidateBySource[(Get-NormalizedUrl -Url $candidate.LegacyUrl)] = $candidate
    }

    $ampSeen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $ampProbeSources = $legacyUnique | Where-Object { Test-AmpProbeCandidate -Url $_ }
    foreach ($legacyUrl in $ampProbeSources) {
        $ampUrl = Get-AmpHtmlAlternateUrl -Url $legacyUrl
        if ([string]::IsNullOrWhiteSpace($ampUrl)) {
            continue
        }

        $ampNormalized = Get-NormalizedUrl -Url $ampUrl
        if (-not $ampSeen.Add($ampNormalized)) {
            continue
        }

        $legacyNormalized = Get-NormalizedUrl -Url $legacyUrl
        $targetUrl = $null
        $matchKind = 'amphtml-review'
        $notes = 'AMP alternate discovered from legacy page. Review manually.'

        if ($newLookup.ContainsKey($legacyNormalized)) {
            $targetUrl = $newLookup[$legacyNormalized].Url
            $matchKind = 'amphtml-to-canonical'
            $notes = 'AMP alternate discovered from legacy page and mapped to the canonical route.'
        } elseif ($candidateBySource.ContainsKey($legacyNormalized)) {
            $candidate = $candidateBySource[$legacyNormalized]
            $targetUrl = Get-SafeTargetUrl -Candidate $candidate
            if ($targetUrl) {
                $matchKind = 'amphtml-to-' + $candidate.MatchKind
                $notes = 'AMP alternate discovered from legacy page and mapped to the resolved canonical route.'
            }
        }

        if ($targetUrl) {
            $redirectRow = New-RedirectExportRow -LegacyUrl $ampUrl -TargetUrl $targetUrl -MatchKind $matchKind -Notes $notes
            $redirectExports.Add($redirectRow)
            $ampDiscovery.Add([pscustomobject] @{
                LegacyUrl   = $ampUrl
                TargetUrl   = $targetUrl
                MatchKind   = $matchKind
                NeedsReview = $false
                Notes       = $notes
                SourceUrl   = $legacyUrl
            })
        } else {
            $reviewRow = [pscustomobject] @{
                legacy_url = $ampUrl
                target_url = ''
                match_kind = $matchKind
                notes = $notes
            }
            $reviewExports.Add($reviewRow)
            $ampDiscovery.Add([pscustomobject] @{
                LegacyUrl   = $ampUrl
                TargetUrl   = ''
                MatchKind   = $matchKind
                NeedsReview = $true
                Notes       = $notes
                SourceUrl   = $legacyUrl
            })
        }
    }
}

if ($IncludeAmpListingRoots) {
    $legacyOrigins = $legacyUnique | ForEach-Object { Get-UrlOrigin -Url $_ } | Sort-Object -Unique
    foreach ($origin in $legacyOrigins) {
        $ampRoot = '{0}/amp/' -f $origin.TrimEnd('/')
        $targetBlog = '{0}/blog/' -f $origin.TrimEnd('/')
        if ($newLookup.ContainsKey((Get-NormalizedUrl -Url $targetBlog)) -or (Test-GeneratedRouteExists -SiteRoot $newSiteRoot -Url $targetBlog)) {
            $redirectExports.Add((New-RedirectExportRow -LegacyUrl $ampRoot -TargetUrl $targetBlog -MatchKind 'amp-root-to-blog' -Notes 'Legacy AMP index route moved to /blog/.'))
        } else {
            $reviewExports.Add([pscustomobject] @{
                legacy_url = $ampRoot
                target_url = ''
                match_kind = 'amp-review'
                notes = 'AMP index route could not be mapped automatically. Review manually.'
            })
        }
    }
}

$redirectRows = $redirectExports |
    Group-Object -Property legacy_url, target_url, status |
    ForEach-Object { $_.Group | Select-Object -First 1 } |
    Sort-Object -Property legacy_url, target_url

$reviewRows = $reviewExports |
    Group-Object -Property legacy_url, target_url, match_kind |
    ForEach-Object { $_.Group | Select-Object -First 1 } |
    Sort-Object -Property legacy_url, match_kind

$result = [pscustomobject] @{
    GeneratedAt          = (Get-Date).ToString('s')
    LegacySitemapUrls    = $LegacySitemapUrls
    NewSitemapPath       = (Resolve-Path -Path $NewSitemapPath).Path
    LegacyUrlCount       = @($legacyNormalized).Count
    NewUrlCount          = @($newNormalized).Count
    ExactOverlapCount    = @($legacyNormalized | Where-Object { $_ -in $newNormalized }).Count
    MissingLegacyCount   = @($missingLegacy).Count
    MissingByMatchKind   = @(
        $missingLegacy |
            Group-Object -Property MatchKind |
            Sort-Object -Property Count -Descending |
            ForEach-Object {
                [pscustomobject] @{
                    MatchKind = $_.Name
                    Count     = $_.Count
                }
            }
    )
    MissingLegacy        = @($missingLegacy)
    RedirectRowCount     = @($redirectRows).Count
    ReviewRowCount       = @($reviewRows).Count
    AmpDiscoveryCount    = @($ampDiscovery).Count
    AmpDiscovery         = @($ampDiscovery)
    NewOnlyUrls          = @(
        foreach ($newUrl in $newUnique) {
            $normalized = Get-NormalizedUrl -Url $newUrl
            if ($normalized -notin $legacyNormalized) {
                $newUrl
            }
        }
    )
}

if ($OutputJsonPath) {
    $jsonPath = [System.IO.Path]::GetFullPath($OutputJsonPath)
    $jsonDir = Split-Path -Parent $jsonPath
    if ($jsonDir) {
        [System.IO.Directory]::CreateDirectory($jsonDir) | Out-Null
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8
}

if ($OutputCsvPath) {
    $csvPath = [System.IO.Path]::GetFullPath($OutputCsvPath)
    $csvDir = Split-Path -Parent $csvPath
    if ($csvDir) {
        [System.IO.Directory]::CreateDirectory($csvDir) | Out-Null
    }
    $missingLegacy |
        Select-Object LegacyUrl, TargetUrl, MatchKind, NeedsReview, Notes |
        Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
}

if ($OutputRedirectCsvPath) {
    $redirectPath = [System.IO.Path]::GetFullPath($OutputRedirectCsvPath)
    $redirectDir = Split-Path -Parent $redirectPath
    if ($redirectDir) {
        [System.IO.Directory]::CreateDirectory($redirectDir) | Out-Null
    }
    $redirectRows | Export-Csv -Path $redirectPath -NoTypeInformation -Encoding UTF8
}

if ($OutputReviewCsvPath) {
    $reviewPath = [System.IO.Path]::GetFullPath($OutputReviewCsvPath)
    $reviewDir = Split-Path -Parent $reviewPath
    if ($reviewDir) {
        [System.IO.Directory]::CreateDirectory($reviewDir) | Out-Null
    }
    $reviewRows | Export-Csv -Path $reviewPath -NoTypeInformation -Encoding UTF8
}

if ($PassThru) {
    $result
    return
}

Write-Host ("Legacy URLs: {0}" -f $result.LegacyUrlCount)
Write-Host ("New URLs: {0}" -f $result.NewUrlCount)
Write-Host ("Exact overlap: {0}" -f $result.ExactOverlapCount)
Write-Host ("Missing legacy URLs: {0}" -f $result.MissingLegacyCount)
Write-Host ''
Write-Host 'Missing by match kind:'
$result.MissingByMatchKind | Format-Table -AutoSize | Out-Host

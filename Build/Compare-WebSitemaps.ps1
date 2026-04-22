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

    [switch] $PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Get-NormalizedUrl {
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    $uri = [System.Uri] $Url
    $path = $uri.AbsolutePath
    if ($path.Length -gt 1 -and $path.EndsWith('/')) {
        $path = $path.TrimEnd('/')
    }

    $builder = [System.UriBuilder]::new($uri.Scheme, $uri.Host.ToLowerInvariant())
    $builder.Path = $path.ToLowerInvariant()
    $builder.Query = ''
    $builder.Fragment = ''
    $builder.Uri.AbsoluteUri.TrimEnd('/')
}

function Get-UrlPath {
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    $uri = [System.Uri] $Url
    if ($uri.AbsolutePath.Length -gt 1 -and $uri.AbsolutePath.EndsWith('/')) {
        return $uri.AbsolutePath.TrimEnd('/').ToLowerInvariant()
    }

    return $uri.AbsolutePath.ToLowerInvariant()
}

function Get-UrlOrigin {
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    $uri = [System.Uri] $Url
    '{0}://{1}' -f $uri.Scheme.ToLowerInvariant(), $uri.Host.ToLowerInvariant()
}

function Import-SitemapUrls {
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    $content = [string] (Invoke-WebRequest -UseBasicParsing -Uri $Url).Content
    $content = $content.TrimStart([char] 0xFEFF)
    [xml] $xml = $content

    if ($xml.PSObject.Properties.Name -contains 'sitemapindex' -and $xml.sitemapindex -and $xml.sitemapindex.sitemap) {
        $nested = foreach ($node in $xml.sitemapindex.sitemap) {
            if ($node.loc) {
                Import-SitemapUrls -Url ([string] $node.loc)
            }
        }
        return $nested
    }

    if ($xml.PSObject.Properties.Name -contains 'urlset' -and $xml.urlset -and $xml.urlset.url) {
        return @(
            foreach ($node in $xml.urlset.url) {
                if ($node.loc) {
                    [string] $node.loc
                }
            }
        )
    }

    throw "Unsupported sitemap document at $Url"
}

function Import-LocalSitemapUrls {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    [xml] $xml = Get-Content -Path $Path -Raw
    if (-not ($xml.PSObject.Properties.Name -contains 'urlset') -or -not $xml.urlset -or -not $xml.urlset.url) {
        throw "Unsupported local sitemap document at $Path"
    }

    @(
        foreach ($node in $xml.urlset.url) {
            if ($node.loc) {
                [string] $node.loc
            }
        }
    )
}

function Test-GeneratedRouteExists {
    param(
        [Parameter(Mandatory)]
        [string] $SiteRoot,
        [Parameter(Mandatory)]
        [string] $Url
    )

    if (-not (Test-Path -LiteralPath $SiteRoot -PathType Container)) {
        return $false
    }

    $uri = [System.Uri] $Url
    $path = $uri.AbsolutePath.Trim('/')
    $candidate = if ([string]::IsNullOrWhiteSpace($path)) {
        Join-Path $SiteRoot 'index.html'
    } else {
        Join-Path $SiteRoot ($path -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    }

    foreach ($filePath in @($candidate, ($candidate + '.html'), (Join-Path $candidate 'index.html')) | Select-Object -Unique) {
        if (Test-Path -LiteralPath $filePath -PathType Leaf) {
            return $true
        }
    }

    return $false
}

function New-UrlLookup {
    param(
        [Parameter(Mandatory)]
        [string[]] $Urls
    )

    $lookup = @{}
    foreach ($url in $Urls) {
        $normalized = Get-NormalizedUrl -Url $url
        if (-not $lookup.ContainsKey($normalized)) {
            $lookup[$normalized] = [pscustomobject] @{
                Url        = $url
                Normalized = $normalized
                Path       = Get-UrlPath -Url $url
            }
        }
    }

    $lookup
}

function ConvertTo-ComparablePath {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $normalized = $Value.ToLowerInvariant().Normalize([Text.NormalizationForm]::FormD)
    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $normalized.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void] $builder.Append($character)
        }
    }

    return $builder.ToString().Normalize([Text.NormalizationForm]::FormC)
}

function New-PathAliasLookup {
    param(
        [Parameter(Mandatory)]
        [hashtable] $UrlLookup
    )

    $lookup = @{}
    foreach ($entry in $UrlLookup.Values) {
        $key = ConvertTo-ComparablePath -Value ([string] $entry.Path)
        if (-not [string]::IsNullOrWhiteSpace($key) -and -not $lookup.ContainsKey($key)) {
            $lookup[$key] = $entry.Url
        }
    }

    return $lookup
}

function Get-SlugVariants {
    param(
        [Parameter(Mandatory)]
        [string] $Slug
    )

    $variants = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($Slug.Trim('/').ToLowerInvariant())

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if ([string]::IsNullOrWhiteSpace($current) -or -not $variants.Add($current)) {
            continue
        }

        if ($current -match '^(.*?)-(pl|fr|de|es)$' -and -not [string]::IsNullOrWhiteSpace($Matches[1])) {
            $queue.Enqueue($Matches[1])
        }
        if ($current -match '^(.*?)-\d+$' -and -not [string]::IsNullOrWhiteSpace($Matches[1])) {
            $queue.Enqueue($Matches[1])
        }
        if ($current -match '^microsoft-(.+)$' -and -not [string]::IsNullOrWhiteSpace($Matches[1])) {
            $queue.Enqueue($Matches[1])
        }
    }

    return @($variants)
}

function Add-CandidateIfFound {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]] $Candidates,
        [Parameter(Mandatory)]
        [string] $LegacyUrl,
        [Parameter(Mandatory)]
        [string] $Scheme,
        [Parameter(Mandatory)]
        [string] $HostName,
        [Parameter(Mandatory)]
        [string[]] $CandidatePaths,
        [Parameter(Mandatory)]
        [hashtable] $NewLookup,
        [Parameter(Mandatory)]
        [hashtable] $PathAliasLookup,
        [string] $NewSiteRoot,
        [Parameter(Mandatory)]
        [string] $MatchKind,
        [Parameter(Mandatory)]
        [string] $Notes
    )

    foreach ($candidatePath in ($CandidatePaths | Where-Object { $_ } | Select-Object -Unique)) {
        $path = $candidatePath
        if (-not $path.StartsWith('/')) {
            $path = '/' + $path
        }

        $candidateUrl = "{0}://{1}{2}" -f $Scheme, $HostName, $path
        $normalizedCandidate = Get-NormalizedUrl -Url $candidateUrl
        if ($NewLookup.ContainsKey($normalizedCandidate)) {
            $Candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = $NewLookup[$normalizedCandidate].Url
                MatchKind   = $MatchKind
                NeedsReview = $false
                Notes       = $Notes
            })
            return
        }

        $aliasKey = ConvertTo-ComparablePath -Value (Get-UrlPath -Url $candidateUrl)
        if ($PathAliasLookup.ContainsKey($aliasKey)) {
            $Candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = $PathAliasLookup[$aliasKey]
                MatchKind   = $MatchKind
                NeedsReview = $false
                Notes       = $Notes
            })
            return
        }

        if ($NewSiteRoot) {
            $generatedUrl = if ($candidateUrl.EndsWith('/')) { $candidateUrl } else { $candidateUrl + '/' }
            if (Test-GeneratedRouteExists -SiteRoot $NewSiteRoot -Url $generatedUrl) {
                $Candidates.Add([pscustomobject] @{
                    LegacyUrl   = $LegacyUrl
                    TargetUrl   = $generatedUrl
                    MatchKind   = $MatchKind
                    NeedsReview = $false
                    Notes       = $Notes
                })
                return
            }
        }
    }
}

function Get-SafeTargetUrl {
    param(
        $Candidate
    )

    if ($null -eq $Candidate) {
        return $null
    }

    if ($Candidate.NeedsReview -or [string]::IsNullOrWhiteSpace([string] $Candidate.TargetUrl)) {
        return $null
    }

    return [string] $Candidate.TargetUrl
}

function Get-SearchEquivalentUrl {
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    $uri = [System.Uri] $Url
    $builder = [System.UriBuilder]::new($uri)
    $path = $uri.AbsolutePath
    if ($path.Length -gt 1 -and $path.EndsWith('/')) {
        $path = $path.TrimEnd('/')
    }
    $builder.Path = $path
    $builder.Query = ''
    $builder.Fragment = ''
    $builder.Uri.AbsoluteUri
}

function Test-AmpProbeCandidate {
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    $path = Get-UrlPath -Url $Url
    if ([string]::IsNullOrWhiteSpace($path)) {
        return $false
    }

    if ($path -eq '/' -or $path -eq '/amp') {
        return $false
    }

    if ($path -match '^/(tag|post_tag|category|author|search)(/|$)') {
        return $false
    }

    if ($path -match '/amp$') {
        return $false
    }

    return $true
}

function Get-AmpHtmlAlternateUrl {
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url
    } catch {
        return $null
    }

    $match = [regex]::Match(
        [string] $response.Content,
        '<link[^>]+rel=["'']amphtml["''][^>]+href=["'']([^"'']+)["'']',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    if (-not $match.Success) {
        return $null
    }

    $href = [string] $match.Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($href)) {
        return $null
    }

    try {
        return ([System.Uri]::new([System.Uri] $Url, $href)).AbsoluteUri
    } catch {
        return $null
    }
}

function New-RedirectExportRow {
    param(
        [Parameter(Mandatory)]
        [string] $LegacyUrl,
        [Parameter(Mandatory)]
        [string] $TargetUrl,
        [Parameter(Mandatory)]
        [string] $MatchKind,
        [string] $Notes
    )

    [pscustomobject] @{
        legacy_url = $LegacyUrl
        target_url = $TargetUrl
        status = 301
        match_kind = $MatchKind
        notes = $Notes
    }
}

function Get-SyntheticAmpLegacyUrl {
    param(
        [Parameter(Mandatory)]
        [string] $LegacyUrl
    )

    $uri = [System.Uri] $LegacyUrl
    $path = Get-UrlPath -Url $LegacyUrl
    if ([string]::IsNullOrWhiteSpace($path) -or $path -eq '/') {
        return $null
    }

    if ($path -eq '/amp' -or $path -match '/amp$') {
        return $null
    }

    $ampPath = '{0}/amp/' -f $path.TrimEnd('/')
    return ('{0}://{1}{2}' -f $uri.Scheme.ToLowerInvariant(), $uri.Host.ToLowerInvariant(), $ampPath)
}

function Test-SyntheticAmpRedirectCandidate {
    param(
        $Candidate
    )

    $targetUrl = Get-SafeTargetUrl -Candidate $Candidate
    if ([string]::IsNullOrWhiteSpace($targetUrl)) {
        return $false
    }

    $legacyPath = Get-UrlPath -Url ([string] $Candidate.LegacyUrl)
    if ([string]::IsNullOrWhiteSpace($legacyPath) -or $legacyPath -eq '/' -or $legacyPath -eq '/amp' -or $legacyPath -match '/amp$') {
        return $false
    }

    $targetPath = Get-UrlPath -Url $targetUrl
    return $targetPath -match '^/(blog|categories|tags)(/|$)'
}

function Get-RedirectCandidate {
    param(
        [Parameter(Mandatory)]
        [string] $LegacyUrl,

        [Parameter(Mandatory)]
        [hashtable] $NewLookup,

        [Parameter(Mandatory)]
        [hashtable] $PathAliasLookup,

        [string] $NewSiteRoot
    )

    $legacyNormalized = Get-NormalizedUrl -Url $LegacyUrl
    if ($NewLookup.ContainsKey($legacyNormalized)) {
        return [pscustomobject] @{
            LegacyUrl   = $LegacyUrl
            TargetUrl   = $NewLookup[$legacyNormalized].Url
            MatchKind   = 'exact'
            NeedsReview = $false
            Notes       = 'Already present in the new sitemap.'
        }
    }

    $legacyUri = [System.Uri] $LegacyUrl
    $legacyHost = $legacyUri.Host.ToLowerInvariant()
    $legacyPath = Get-UrlPath -Url $LegacyUrl
    $candidates = [System.Collections.Generic.List[object]]::new()

    if ($legacyPath -notmatch '^/blog(?:/|$)') {
        $candidate = Get-NormalizedUrl -Url ("{0}://{1}/blog{2}" -f $legacyUri.Scheme, $legacyHost, $legacyPath)
        if ($NewLookup.ContainsKey($candidate)) {
            $candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = $NewLookup[$candidate].Url
                MatchKind   = 'root-to-blog'
                NeedsReview = $false
                Notes       = 'Legacy root content moved under /blog/.'
            })
        }
    }

    if ($legacyPath -like '/category/*') {
        $suffix = $legacyPath.Substring('/category/'.Length)
        $candidate = Get-NormalizedUrl -Url ("{0}://{1}/categories/{2}" -f $legacyUri.Scheme, $legacyHost, $suffix)
        if ($NewLookup.ContainsKey($candidate)) {
            $candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = $NewLookup[$candidate].Url
                MatchKind   = 'category-to-categories'
                NeedsReview = $false
                Notes       = 'Legacy WordPress category route moved to /categories/.'
            })
        }

        if ($candidates.Count -eq 0) {
            $candidatePaths = foreach ($variant in (Get-SlugVariants -Slug $suffix)) {
                "/categories/$variant"
                "/tags/$variant"
            }
            Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths $candidatePaths -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'category-normalized' -Notes 'Legacy category route matched a normalized category/tag target.'
        }
    }

    if ($legacyPath -like '/tag/*' -or $legacyPath -like '/post_tag/*') {
        $suffix = if ($legacyPath -like '/tag/*') {
            $legacyPath.Substring('/tag/'.Length)
        } else {
            $legacyPath.Substring('/post_tag/'.Length)
        }
        $candidate = Get-NormalizedUrl -Url ("{0}://{1}/tags/{2}" -f $legacyUri.Scheme, $legacyHost, $suffix)
        if ($NewLookup.ContainsKey($candidate)) {
            $candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = $NewLookup[$candidate].Url
                MatchKind   = 'tag-to-tags'
                NeedsReview = $false
                Notes       = 'Legacy tag route moved to /tags/.'
            })
        }

        if ($candidates.Count -eq 0) {
            $candidatePaths = foreach ($variant in (Get-SlugVariants -Slug $suffix)) {
                "/tags/$variant"
            }
            Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths $candidatePaths -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'tag-normalized' -Notes 'Legacy tag route matched a normalized tag target.'
        }
    }

    if ($legacyPath -match '\.html$') {
        $withoutHtml = $legacyPath -replace '\.html$', ''
        $candidate = Get-NormalizedUrl -Url ("{0}://{1}{2}" -f $legacyUri.Scheme, $legacyHost, $withoutHtml)
        if ($NewLookup.ContainsKey($candidate)) {
            $candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = $NewLookup[$candidate].Url
                MatchKind   = 'drop-html-extension'
                NeedsReview = $false
                Notes       = 'Legacy .html route matches the slash-route in the new sitemap.'
            })
        }
    }

    if ($legacyPath -eq '/amp') {
        $candidate = Get-NormalizedUrl -Url ("{0}://{1}/blog" -f $legacyUri.Scheme, $legacyHost)
        if ($NewLookup.ContainsKey($candidate) -or (($NewSiteRoot) -and (Test-GeneratedRouteExists -SiteRoot $NewSiteRoot -Url ("{0}://{1}/blog/" -f $legacyUri.Scheme, $legacyHost)))) {
            $targetUrl = if ($NewLookup.ContainsKey($candidate)) {
                $NewLookup[$candidate].Url
            } else {
                "{0}://{1}/blog/" -f $legacyUri.Scheme, $legacyHost
            }
            $candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = $targetUrl
                MatchKind   = 'amp-root-to-blog'
                NeedsReview = $false
                Notes       = 'Legacy AMP index route moved to /blog/.'
            })
        } else {
            $candidates.Add([pscustomobject] @{
                LegacyUrl   = $LegacyUrl
                TargetUrl   = ''
                MatchKind   = 'amp-review'
                NeedsReview = $true
                Notes       = 'AMP root route could not be mapped automatically. Review manually.'
            })
        }
    } elseif ($legacyPath -match '^/amp(?:/|$)') {
        $candidates.Add([pscustomobject] @{
            LegacyUrl   = $LegacyUrl
            TargetUrl   = ''
            MatchKind   = 'amp-review'
            NeedsReview = $true
            Notes       = 'AMP route requires manual review. It may be a listing page rather than a 1:1 canonical page.'
        })
    }

    if ($candidates.Count -eq 0) {
        switch -Regex ($legacyPath) {
            '^/docs$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/projects') -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'docs-to-projects' -Notes 'Legacy docs hub mapped to the current projects landing page.'
                break
            }
            '^/author/[^/]+/?$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/blog') -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'author-to-blog' -Notes 'Legacy author archive mapped to the current blog landing page.'
                break
            }
            '^/hub/scripts/?$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/scripts') -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'hub-scripts-root' -Notes 'Legacy hub scripts landing page matched the current scripts page.'
                break
            }
            '^/hub/scripts/(.+?)/?$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/' + $Matches[1]) -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'hub-scripts-detail' -Notes 'Legacy hub script detail mapped to the current root route.'
                break
            }
            '^/powershell-modules/(.+?)/?$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/' + $Matches[1], '/projects/' + $Matches[1]) -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'powershell-modules-detail' -Notes 'Legacy PowerShell module page mapped to the current route.'
                break
            }
            '^/net-products/(.+?)/?$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/' + $Matches[1], '/projects/' + $Matches[1]) -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'net-products-detail' -Notes 'Legacy .NET product page mapped to the current route.'
                break
            }
            '^/offer/(.+?)/?$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/' + $Matches[1]) -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'offer-detail' -Notes 'Legacy offer page mapped to the current route.'
                break
            }
            '^/start/(.+?)/?$' {
                Add-CandidateIfFound -Candidates $candidates -LegacyUrl $LegacyUrl -Scheme $legacyUri.Scheme -HostName $legacyHost -CandidatePaths @('/' + $Matches[1]) -NewLookup $NewLookup -PathAliasLookup $PathAliasLookup -NewSiteRoot $NewSiteRoot -MatchKind 'start-detail' -Notes 'Legacy start section page mapped to the current route.'
                break
            }
        }
    }

    if ($candidates.Count -eq 0) {
        return [pscustomobject] @{
            LegacyUrl   = $LegacyUrl
            TargetUrl   = ''
            MatchKind   = 'missing'
            NeedsReview = $true
            Notes       = 'No direct candidate found in the new sitemap.'
        }
    }

    if ($candidates.Count -eq 1) {
        return $candidates[0]
    }

    [pscustomobject] @{
        LegacyUrl   = $LegacyUrl
        TargetUrl   = ($candidates | Select-Object -First 1).TargetUrl
        MatchKind   = 'multiple-candidates'
        NeedsReview = $true
        Notes       = 'Multiple redirect candidates found. Review manually.'
    }
}

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

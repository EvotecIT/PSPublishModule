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
    param([Parameter(Mandatory)][string] $Url)

    $uri = [System.Uri] $Url
    '{0}://{1}' -f $uri.Scheme.ToLowerInvariant(), $uri.Host.ToLowerInvariant()
}

function ConvertTo-SafeXmlDocument {
    param([Parameter(Mandatory)][string] $Content, [Parameter(Mandatory)][string] $Source)

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $stringReader = [System.IO.StringReader]::new($Content)
    $xmlReader = $null
    try {
        $xmlReader = [System.Xml.XmlReader]::Create($stringReader, $settings)
        $xml = [System.Xml.XmlDocument]::new()
        $xml.XmlResolver = $null
        $xml.Load($xmlReader)
        return $xml
    } catch {
        throw "Failed to parse sitemap XML from $Source. $($_.Exception.Message)"
    } finally {
        if ($null -ne $xmlReader) { $xmlReader.Dispose() }
        $stringReader.Dispose()
    }
}

function Import-SitemapUrls {
    param(
        [Parameter(Mandatory)]
        [string] $Url,
        [Parameter(Mandatory)]
        [int] $TimeoutSec,
        [Parameter(Mandatory)]
        [int] $MaxDepth,
        [int] $Depth = 0
    )

    if ($Depth -gt $MaxDepth) {
        throw "Sitemap nesting exceeded MaxDepth ($MaxDepth) at $Url"
    }

    $content = [string] (Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec $TimeoutSec).Content
    $content = $content.TrimStart([char] 0xFEFF)
    $xml = ConvertTo-SafeXmlDocument -Content $content -Source $Url

    if ($xml.PSObject.Properties.Name -contains 'sitemapindex' -and $xml.sitemapindex -and $xml.sitemapindex.sitemap) {
        $nested = foreach ($node in $xml.sitemapindex.sitemap) {
            if ($node.loc) {
                Import-SitemapUrls -Url ([string] $node.loc) -TimeoutSec $TimeoutSec -MaxDepth $MaxDepth -Depth ($Depth + 1)
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

    $xml = ConvertTo-SafeXmlDocument -Content (Get-Content -Path $Path -Raw) -Source $Path
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
        [string] $Url,
        [Parameter(Mandatory)]
        [int] $TimeoutSec
    )

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec $TimeoutSec
    } catch {
        Write-Verbose ("AMP discovery skipped for {0}: {1}" -f $Url, $_.Exception.Message)
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
        Write-Verbose ("AMP discovery returned an invalid href for {0}: {1}" -f $Url, $_.Exception.Message)
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

function Invoke-PowerForgeGitHubReleaseProbe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Uri,

        [Parameter(Mandatory)]
        [string] $Token
    )

    $client = [Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd('PSPublishModule-Release/1.0')
        $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
        $client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
        $response = $client.GetAsync($Uri).ConfigureAwait($false).GetAwaiter().GetResult()
        try {
            $content = $response.Content.ReadAsStringAsync().ConfigureAwait($false).GetAwaiter().GetResult()
            if ([int] $response.StatusCode -eq 404) {
                return $null
            }
            if (-not $response.IsSuccessStatusCode) {
                throw "GitHub release recovery probe failed ($([int] $response.StatusCode) $($response.ReasonPhrase))."
            }
            $content | ConvertFrom-Json -Depth 20
        } finally {
            $response.Dispose()
        }
    } finally {
        $client.Dispose()
    }
}

function Enable-PowerForgeVerifiedGitHubReleaseRecovery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $ReleaseConfig,

        [Parameter(Mandatory)]
        [ValidatePattern('^\d+\.\d+\.\d+$')]
        [string] $Version,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{40}$')]
        [string] $ExpectedCommit,

        [Parameter(Mandatory)]
        [string] $Token,

        [scriptblock] $GetReleaseByTag,

        [scriptblock] $GetTagCommit
    )

    $gitHub = $ReleaseConfig.GitHub
    if ($null -eq $gitHub -or $gitHub.Publish -ne $true) {
        throw 'The release configuration does not enable the unified GitHub release.'
    }
    if ([string]::IsNullOrWhiteSpace($Token)) {
        throw 'The GitHub release token is empty.'
    }

    $owner = [string] $gitHub.Owner
    $repository = [string] $gitHub.Repository
    if ($owner -notmatch '^[A-Za-z0-9_.-]+$' -or $repository -notmatch '^[A-Za-z0-9_.-]+$') {
        throw 'The GitHub release owner or repository contains unsupported characters.'
    }

    $tagTemplate = if ([string]::IsNullOrWhiteSpace([string] $gitHub.TagTemplate)) { 'v{Version}' } else { [string] $gitHub.TagTemplate }
    $tagName = $tagTemplate.Replace('{Version}', $Version).Replace('{Repository}', $repository)
    if ([string]::IsNullOrWhiteSpace($tagName) -or $tagName.Contains('{') -or $tagName.Contains('}')) {
        throw "The GitHub release tag template did not resolve to an exact tag: '$tagName'."
    }

    $gitHub.ReuseExistingRelease = $false
    $gitHub.ReplaceExistingAssets = $false
    $gitHub | Add-Member -NotePropertyName RequireExpectedExistingRelease -NotePropertyValue $false -Force
    $gitHub | Add-Member -NotePropertyName ExpectedExistingReleaseId -NotePropertyValue $null -Force
    $gitHub | Add-Member -NotePropertyName RequirePublishedStableRelease -NotePropertyValue $false -Force
    $gitHub | Add-Member -NotePropertyName RequirePublishedNuGetAssets -NotePropertyValue $false -Force

    if ($null -eq $GetReleaseByTag) {
        $GetReleaseByTag = {
            param($probeOwner, $probeRepository, $probeTag, $probeToken)
            $escapedTag = [Uri]::EscapeDataString([string] $probeTag)
            Invoke-PowerForgeGitHubReleaseProbe `
                -Uri "https://api.github.com/repos/$probeOwner/$probeRepository/releases/tags/$escapedTag" `
                -Token $probeToken
        }
    }
    if ($null -eq $GetTagCommit) {
        $GetTagCommit = {
            param($probeOwner, $probeRepository, $probeTag, $probeToken)
            $escapedTag = [Uri]::EscapeDataString([string] $probeTag)
            $commit = Invoke-PowerForgeGitHubReleaseProbe `
                -Uri "https://api.github.com/repos/$probeOwner/$probeRepository/commits/$escapedTag" `
                -Token $probeToken
            if ($null -eq $commit) { return $null }
            [string] $commit.sha
        }
    }

    $release = & $GetReleaseByTag $owner $repository $tagName $Token
    $tagCommit = & $GetTagCommit $owner $repository $tagName $Token
    if ($null -eq $release -and [string]::IsNullOrWhiteSpace([string] $tagCommit)) {
        return [pscustomobject]@{
            ReuseEnabled = $false
            TagName       = $tagName
            ReleaseId     = $null
        }
    }
    if ($null -eq $release) {
        throw "GitHub tag '$tagName' exists without a matching release; automatic recovery is unsafe."
    }
    if ([string]::IsNullOrWhiteSpace([string] $tagCommit)) {
        throw "GitHub release '$tagName' exists without a resolvable tag commit; automatic recovery is unsafe."
    }
    if ([string] $release.tag_name -cne $tagName -or [long] $release.id -le 0) {
        throw "GitHub release recovery returned an invalid release identity for '$tagName'."
    }
    if ($release.draft -eq $true -or $release.prerelease -eq $true -or [string]::IsNullOrWhiteSpace([string] $release.published_at)) {
        throw "GitHub release '$tagName' is not a published stable release; automatic recovery is unsafe."
    }
    if ([string] $tagCommit -ine $ExpectedCommit) {
        throw "GitHub tag '$tagName' resolves to '$tagCommit', expected authorized commit '$ExpectedCommit'."
    }

    $gitHub.ReuseExistingRelease = $true
    $gitHub.ReplaceExistingAssets = $true
    $gitHub.RequireExpectedExistingRelease = $true
    $gitHub.ExpectedExistingReleaseId = [long] $release.id
    $gitHub.RequirePublishedStableRelease = $true
    $gitHub.RequirePublishedNuGetAssets = $true
    [pscustomobject]@{
        ReuseEnabled = $true
        TagName       = $tagName
        ReleaseId     = [long] $release.id
    }
}

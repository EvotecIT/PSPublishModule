$ErrorActionPreference = 'Stop'

function Read-GitHubJson {
    param([string[]] $Arguments)
    $json = & gh api @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read or reserve the npm release version in GitHub.' }
    return ($json | ConvertFrom-Json)
}

function Read-ReleaseVersion {
    param([string] $Value)
    if ($Value -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "Expected a three-part release version, received '$Value'."
    }
    return [Version]::Parse($Value)
}

if ($env:GITHUB_SHA -notmatch '^[a-fA-F0-9]{40}$' -or
    $env:GITHUB_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $env:GITHUB_REF -cne "refs/heads/$env:DEFAULT_BRANCH" -or
    $env:GITHUB_EVENT_NAME -cnotin @('workflow_dispatch', 'workflow_run')) {
    throw 'Version reservations require a default-branch release workflow.'
}
if ($env:RELEASE_TAG_PREFIX -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or
    $env:RESERVE_VERSION -cnotin @('true', 'false')) {
    throw 'Invalid release tag prefix or reservation mode.'
}
if ($env:QUALITY_RUN -notmatch '^\d+$') { throw 'A qualifying workflow run is required before version reservation.' }
$sourceSha = $env:GITHUB_SHA
if ($env:GITHUB_EVENT_NAME -ceq 'workflow_run') {
    $event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
    if ($event.action -cne 'completed' -or [string]$event.workflow_run.id -cne $env:QUALITY_RUN -or
        $event.workflow_run.head_repository.full_name -cne $env:GITHUB_REPOSITORY) {
        throw 'The automatic release event does not identify this repository''s qualifying run.'
    }
    $sourceSha = [string]$event.workflow_run.head_sha
}
if ($sourceSha -notmatch '^[a-fA-F0-9]{40}$') { throw 'Invalid qualified source commit.' }
$source = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $source.Trim() -cne $sourceSha) { throw 'The checkout does not match the qualified source commit.' }
$quality = Read-GitHubJson -Arguments @("repos/$env:GITHUB_REPOSITORY/actions/runs/$env:QUALITY_RUN")
if ($quality.head_sha -cne $sourceSha -or $quality.head_branch -cne $env:DEFAULT_BRANCH -or
    $quality.event -cne 'push' -or $quality.status -cne 'completed' -or $quality.conclusion -cne 'success' -or
    ($quality.path -split '@', 2)[0] -cne ".github/workflows/$env:QUALITY_WORKFLOW") {
    throw 'The quality run does not qualify this default-branch source commit.'
}
$manifest = Get-Content -LiteralPath $env:PACKAGE_MANIFEST -Raw | ConvertFrom-Json
$baseline = Read-ReleaseVersion $manifest.version
if ($manifest.name -notmatch '^(@[a-z0-9._-]+/)?[a-z0-9][a-z0-9._-]*$' -or
    $manifest.repository.url -cne "git+https://github.com/$env:GITHUB_REPOSITORY.git") {
    throw 'The manifest must identify this repository and its npm package.'
}

# A tag is a reservation, not evidence of a successful upload. Reruns keep it.
$pages = Read-GitHubJson -Arguments @("repos/$env:GITHUB_REPOSITORY/git/matching-refs/tags/$env:RELEASE_TAG_PREFIX", '--paginate', '--slurp')
$reservations = @(
    foreach ($page in $pages) {
        foreach ($ref in $page) {
            $prefix = "refs/tags/$env:RELEASE_TAG_PREFIX"
            if ($ref.ref.StartsWith($prefix, [StringComparison]::Ordinal)) {
                $suffix = $ref.ref.Substring($prefix.Length)
                if ($suffix -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
                    [pscustomobject]@{ Version = Read-ReleaseVersion $suffix; Ref = $ref }
                }
            }
        }
    }
)
$current = @($reservations | Where-Object { $_.Ref.object.type -ceq 'commit' -and $_.Ref.object.sha -ceq $sourceSha } | Sort-Object Version -Descending)
$uri = 'https://registry.npmjs.org/' + [Uri]::EscapeDataString($manifest.name)
$response = Invoke-WebRequest -Uri $uri -SkipHttpErrorCheck
if ($response.StatusCode -notin @(200, 404)) { throw 'Cannot determine published npm versions.' }
$publishedVersions = @()
if ($response.StatusCode -eq 200) {
    $published = $response.Content | ConvertFrom-Json
    if ($published.name -cne $manifest.name -or $null -eq $published.versions) { throw 'Invalid npm package metadata.' }
    $publishedVersions = @($published.versions.PSObject.Properties.Name | Where-Object {
        $_ -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'
    } | ForEach-Object { Read-ReleaseVersion $_ })
}
$highestPublished = $publishedVersions | Sort-Object -Descending | Select-Object -First 1
$publishedReservation = $reservations | Where-Object { $_.Version -eq $highestPublished } | Select-Object -First 1
if ($highestPublished -and -not $publishedReservation) {
    throw 'The highest public npm version has no source reservation. Verify its published source and seed the release tag before continuing.'
}
if ($publishedReservation -and $publishedReservation.Ref.object.sha -cne $sourceSha) {
    $comparison = Read-GitHubJson -Arguments @("repos/$env:GITHUB_REPOSITORY/compare/$sourceSha...$($publishedReservation.Ref.object.sha)")
    if ($comparison.status -ceq 'ahead') {
        # These changes are already included; never make older code the latest release.
        "version=$highestPublished" >> $env:GITHUB_OUTPUT
        "tag=$env:RELEASE_TAG_PREFIX$highestPublished" >> $env:GITHUB_OUTPUT
        'should-release=false' >> $env:GITHUB_OUTPUT
        return
    }
    if ($comparison.status -cne 'behind') { throw 'Published source history diverged; reconcile release tags before retrying.' }
}
$superseded = $current.Count -gt 0 -and $highestPublished -and $current[0].Version -lt $highestPublished -and $current[0].Version -notin $publishedVersions
if ($superseded -and -not $publishedReservation) { throw 'The higher public release has no source reservation; reconcile it before retrying.' }
if ($current.Count -gt 0 -and -not $superseded) {
    $version = $current[0].Version
    if ($env:REQUESTED_VERSION -and (Read-ReleaseVersion $env:REQUESTED_VERSION) -ne $version) {
        throw 'The requested version differs from this source commit''s reserved version.'
    }
} else {
    $known = @($reservations | ForEach-Object Version) + $publishedVersions
    $highest = $known | Sort-Object -Descending | Select-Object -First 1
    if ($env:REQUESTED_VERSION) {
        $version = Read-ReleaseVersion $env:REQUESTED_VERSION
        if ($version -lt $baseline) { throw 'An explicit release cannot fall below the manifest minimum.' }
        if ($highest -and $version -le $highest) { throw 'An explicit new release must exceed all published and reserved versions.' }
    } elseif ($highest -and $highest -ge $baseline) {
        if ($highest.Build -eq [int]::MaxValue) { throw 'Patch version is exhausted; raise the manifest version line.' }
        $version = [Version]::new($highest.Major, $highest.Minor, $highest.Build + 1)
    } else {
        $version = $baseline
    }
    if ($env:RESERVE_VERSION -ceq 'true') {
        # Atomic creation fails closed if another run selected the same version.
        $created = Read-GitHubJson -Arguments @("repos/$env:GITHUB_REPOSITORY/git/refs", '--method', 'POST',
            '-f', "ref=refs/tags/$env:RELEASE_TAG_PREFIX$version", '-f', "sha=$sourceSha")
        if ($created.object.type -cne 'commit' -or $created.object.sha -cne $sourceSha) {
            throw 'The version reservation did not bind the expected source commit.'
        }
    }
}
"version=$version" >> $env:GITHUB_OUTPUT
"tag=$env:RELEASE_TAG_PREFIX$version" >> $env:GITHUB_OUTPUT
'should-release=true' >> $env:GITHUB_OUTPUT

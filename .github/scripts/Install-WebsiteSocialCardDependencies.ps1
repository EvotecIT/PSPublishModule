[CmdletBinding()]
param(
    [Parameter(Mandatory)][bool] $Enabled,
    # Keeps the downloaded .deb files between hosted runs. -Plan refreshes the apt lists and returns
    # the cache key; the install call in the same job then reuses those lists and fills the directory.
    [string] $PackageCacheDirectory,
    [switch] $Plan
)

$ErrorActionPreference = 'Stop'
if (-not $Enabled) {
    Write-Host 'Social card dependency setup skipped: generation is disabled or social-cards is false.'
    if ($Plan) { return [pscustomobject]@{ Install = $false; PackageCacheKey = '' } }
    return
}
if (-not $IsLinux) {
    Write-Host 'Social card dependency setup skipped: Windows and macOS use the bundled Magick.NET renderer.'
    if ($Plan) { return [pscustomobject]@{ Install = $false; PackageCacheKey = '' } }
    return
}
$aptArchives = '/var/cache/apt/archives'

function Test-SocialCardRender([string] $Executable) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('pf-renderer-probe-' + [Guid]::NewGuid().ToString('N'))
    try {
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $svg = Join-Path $root 'card.svg'
        $png = Join-Path $root 'card.png'
        [IO.File]::WriteAllText($svg, '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"><rect width="16" height="16" fill="#345678"/></svg>')
        $mvg = Join-Path $root 'card.mvg'
        $diagnostic = & $Executable $svg "MVG:$mvg" 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $mvg)) {
            Write-Host "ImageMagick SVG-to-MVG probe failed: $diagnostic"
            return $false
        }
        # Exercise the normalized MVG route used by the runtime on both ImageMagick 6 and 7.
        $text = [IO.File]::ReadAllText($mvg) -replace '(?m)^compliance "SVG"\r?$', ''
        $text = $text -replace '(?m)^push gradient "([A-Za-z0-9_-]+)" ', 'push gradient $1 '
        $text = $text -replace '(?m)^(fill|stroke) "url\(#([A-Za-z0-9_-]+)\)"\r?$', '$1 url(#$2)'
        [IO.File]::WriteAllText($mvg, $text)
        $inputPath = "MVG:$mvg"
        $diagnostic = & $Executable -background none -size 16x16 $inputPath "PNG32:$png" 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $png)) {
            Write-Host "ImageMagick PNG probe failed: $diagnostic"
            return $false
        }
        $bytes = [IO.File]::ReadAllBytes($png)
        return $bytes.Length -ge 8 -and [Convert]::ToHexString($bytes[0..7]) -eq '89504E470D0A1A0A'
    } finally {
        if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
    }
}

function Get-SocialCardRenderer {
    foreach ($name in @('magick', 'convert')) {
        $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) {
            $version = & $command.Source -version 2>&1
            if ($LASTEXITCODE -ne 0 -or ($version -join "`n") -notmatch 'ImageMagick') {
                throw "Preferred renderer '$($command.Source)' is not a working ImageMagick executable. Check the runner PATH."
            }
            $formats = (& $command.Source -list format 2>&1) -join "`n"
            if ($LASTEXITCODE -eq 0 -and
                $formats -match '(?m)^\s*SVG\*?\s+(?:\S+\s+)?r[w-][+\-]?(?:\s|$)' -and
                $formats -match '(?m)^\s*MVG\*?\s+(?:\S+\s+)?rw[+\-]?(?:\s|$)' -and
                $formats -match '(?m)^\s*PNG\*?\s+(?:\S+\s+)?rw[+\-]?(?:\s|$)') {
                if (Test-SocialCardRender $command.Source) { return $command.Source }
                throw "ImageMagick at $($command.Source) failed the SVG-to-PNG rendering probe. Check renderer diagnostics and runner policy; setup does not modify security policy."
            }
            Write-Host "ImageMagick at $($command.Source) lacks required SVG, MVG or PNG support."
            # Runtime also selects the first magick executable before convert.
            return $null
        }
    }
}

# Lists the packages apt would download with their SHA256 from the signed package index.
# Returns nothing when any checksum is unavailable, which turns caching off for that run.
function Get-PlannedPackages {
    $lines = @(& apt-get install --print-uris --yes -qq --install-recommends imagemagick 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Resolving ImageMagick packages failed: $($lines -join ' ')" }
    $packages = @(foreach ($line in $lines) {
        if ("$line" -match "^'[^']+'\s+(?<file>(?<name>[^\s/_]+)_(?<version>[^\s/_]+)_(?<arch>[^\s/_]+)\.deb)\s+(?<size>\d+)\s") {
            [pscustomobject]@{
                File = $Matches.file; Size = [long]$Matches.size; Hash = $null
                Id = "$($Matches.name)|$([Uri]::UnescapeDataString($Matches.version))|$($Matches.arch)"
                Query = "$($Matches.name)$(if ($Matches.arch -ne 'all') { ':' + $Matches.arch })=$([Uri]::UnescapeDataString($Matches.version))"
            }
        }
    })
    if ($packages.Count -eq 0) { return }
    $records = (@(& apt-cache show @($packages.Query) 2>&1) -join "`n") -split '\n\s*\n'
    if ($LASTEXITCODE -ne 0) { return }
    $hashes = @{}
    foreach ($record in $records) {
        $fields = @{}
        foreach ($match in [regex]::Matches($record, '(?m)^(Package|Version|Architecture|SHA256): *(\S+)')) { $fields[$match.Groups[1].Value] = $match.Groups[2].Value }
        if ($fields.SHA256) { $hashes["$($fields.Package)|$($fields.Version)|$($fields.Architecture)"] = $fields.SHA256 }
    }
    foreach ($package in $packages) {
        $package.Hash = $hashes[$package.Id]
        if (-not $package.Hash) { Write-Host "No SHA256 for $($package.File); package caching disabled."; return }
    }
    $packages
}

# Apt installs any archive already in its cache when only the size matches, so cached files
# must match the index checksum before they are offered to apt.
function Test-PackageFile([string] $Path, $Package) {
    if (-not [IO.File]::Exists($Path) -or ([IO.FileInfo]::new($Path)).Length -ne $Package.Size) { return $false }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Package.Hash.ToUpperInvariant()
}

$renderer = Get-SocialCardRenderer
if ($renderer) {
    Write-Host "Social card renderer already available: $renderer (SVG-to-PNG render verified)."
    if ($Plan) { return [pscustomobject]@{ Install = $false; PackageCacheKey = '' } }
    return
}

if ($Plan) {
    $key = ''
    if ($PackageCacheDirectory) {
        & sudo apt-get update | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'apt-get update failed while preparing social cards.' }
        $packages = @(Get-PlannedPackages)
        if ($packages.Count -gt 0) {
            $manifest = ($packages | ForEach-Object { "$($_.File) $($_.Size) $($_.Hash)" } | Sort-Object) -join "`n"
            $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifest))).ToLowerInvariant()
            $image = if ($env:ImageOS) { $env:ImageOS } else { 'linux' }
            $key = "powerforge-social-card-packages-v1-$image-$([Runtime.InteropServices.RuntimeInformation]::OSArchitecture)-$digest"
            Write-Host "ImageMagick needs $($packages.Count) package(s); package cache key: $key"
        }
    }
    return [pscustomobject]@{ Install = $true; PackageCacheKey = $key }
}

Write-Host 'Installing ImageMagick with its recommended SVG rendering support.'
$packages = @()
if ($PackageCacheDirectory) {
    $packages = @(Get-PlannedPackages)
    $cached = @($packages | Where-Object { Test-PackageFile (Join-Path $PackageCacheDirectory $_.File) $_ } | ForEach-Object { Join-Path $PackageCacheDirectory $_.File })
    Write-Host "Reusing $($cached.Count) of $($packages.Count) cached package(s)."
    if ($cached.Count -gt 0) {
        & sudo cp -- @cached $aptArchives | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Copying cached ImageMagick packages failed.' }
    }
} else {
    & sudo apt-get update | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'apt-get update failed while preparing social cards.' }
}
& sudo apt-get install --yes --install-recommends -o APT::Keep-Downloaded-Packages=true imagemagick | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'ImageMagick installation failed.' }
$renderer = Get-SocialCardRenderer
if (-not $renderer) {
    # Apt does not backfill omitted recommendations on an existing minimal install.
    # Resolve its extra coder package from the installed binary's package metadata.
    $command = Get-Command magick, convert -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        $target = (Get-Item -LiteralPath $command.Source).ResolveLinkTarget($true)
        $binary = if ($target) { $target.FullName } else { $command.Source }
        $owners = @(& dpkg-query --search $binary 2>&1)
        if ($LASTEXITCODE -eq 0 -and $owners.Count -eq 1 -and
            $owners[0] -match '^(?<package>imagemagick[-a-z0-9+.]*(:[a-z0-9]+)?): /') {
            $dependencies = @(& apt-cache depends $Matches.package)
            if ($LASTEXITCODE -ne 0) { throw 'Reading ImageMagick package dependencies failed.' }
            $coders = @($dependencies | ForEach-Object {
                if ($_ -match '^\s*Recommends:\s+(libmagickcore[-a-z0-9+.]*-extra)\s*$') { $Matches[1] }
            })
            if ($coders.Count -gt 0) {
                & sudo apt-get install --yes --install-recommends @coders | Out-Host
                if ($LASTEXITCODE -ne 0) { throw 'Repairing ImageMagick rendering support failed.' }
            }
        }
    }
    $renderer = Get-SocialCardRenderer
}
if (-not $renderer) { throw 'ImageMagick installation did not provide required SVG, MVG and PNG support.' }
Write-Host "Social card renderer ready: $renderer (SVG-to-PNG render verified)."
if ($PackageCacheDirectory) {
    # Keep exactly the verified planned packages so a saved cache never carries stale or altered files.
    [IO.Directory]::CreateDirectory($PackageCacheDirectory) | Out-Null
    $planned = @($packages | ForEach-Object File)
    Get-ChildItem -LiteralPath $PackageCacheDirectory -File | Where-Object Name -notin $planned | Remove-Item -Force
    $ready = $packages.Count -gt 0
    foreach ($package in $packages) {
        $target = Join-Path $PackageCacheDirectory $package.File
        if (Test-PackageFile $target $package) { continue }
        $source = Join-Path $aptArchives $package.File
        if (Test-PackageFile $source $package) {
            Copy-Item -LiteralPath $source -Destination $target -Force
        } else {
            $ready = $false
        }
    }
    return [pscustomobject]@{ PackageCacheReady = $ready }
}

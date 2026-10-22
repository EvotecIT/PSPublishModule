[CmdletBinding()]
param([Parameter(Mandatory)][bool] $Enabled)

$ErrorActionPreference = 'Stop'
if (-not $Enabled) {
    Write-Host 'Social card dependency setup skipped: generation is disabled or social-cards is false.'
    return
}
if (-not $IsLinux) {
    Write-Host 'Social card dependency setup skipped: Windows and macOS use the bundled Magick.NET renderer.'
    return
}

function Get-SocialCardRenderer {
    foreach ($name in @('magick', 'convert')) {
        $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) {
            $version = & $command.Source -version 2>&1
            if ($LASTEXITCODE -ne 0 -or ($version -join "`n") -notmatch 'ImageMagick') { continue }
            $formats = (& $command.Source -list format 2>&1) -join "`n"
            if ($LASTEXITCODE -eq 0 -and
                $formats -match '(?m)^\s+SVG\s+\S+\s+r' -and
                $formats -match '(?m)^\s+MVG\*?\s+\S+\s+r' -and
                $formats -match '(?m)^\s+PNG\*?\s+\S+\s+rw') {
                return $command.Source
            }
            Write-Host "ImageMagick at $($command.Source) lacks required SVG, MVG or PNG support."
        }
    }
}

$renderer = Get-SocialCardRenderer
if ($renderer) {
    Write-Host "Social card renderer already available: $renderer (SVG/MVG/PNG verified)."
    return
}

Write-Host 'Installing ImageMagick with its recommended SVG rendering support.'
& sudo apt-get update
if ($LASTEXITCODE -ne 0) { throw 'apt-get update failed while preparing social cards.' }
& sudo apt-get install --yes --install-recommends imagemagick
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
                & sudo apt-get install --yes --install-recommends @coders
                if ($LASTEXITCODE -ne 0) { throw 'Repairing ImageMagick rendering support failed.' }
            }
        }
    }
    $renderer = Get-SocialCardRenderer
}
if (-not $renderer) { throw 'ImageMagick installation did not provide required SVG, MVG and PNG support.' }
Write-Host "Social card renderer ready: $renderer (SVG/MVG/PNG verified)."

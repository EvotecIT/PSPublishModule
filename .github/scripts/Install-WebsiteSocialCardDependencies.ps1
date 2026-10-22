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

function Test-SocialCardRender([string] $Executable, [bool] $Legacy) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('pf-renderer-probe-' + [Guid]::NewGuid().ToString('N'))
    try {
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $svg = Join-Path $root 'card.svg'
        $png = Join-Path $root 'card.png'
        [IO.File]::WriteAllText($svg, '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"><rect width="16" height="16" fill="#345678"/></svg>')
        $inputPath = $svg
        if ($Legacy) {
            $mvg = Join-Path $root 'card.mvg'
            $diagnostic = & $Executable $svg "MVG:$mvg" 2>&1
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $mvg)) {
                Write-Host "ImageMagick SVG-to-MVG probe failed: $diagnostic"
                return $false
            }
            # The runtime removes the SVG compliance directive on the same IM6 path.
            $text = [IO.File]::ReadAllText($mvg) -replace '(?m)^compliance "SVG"\r?$', ''
            [IO.File]::WriteAllText($mvg, $text)
            $inputPath = "MVG:$mvg"
        }
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
            $legacy = ($version -join "`n") -match 'ImageMagick 6\.'
            if ($LASTEXITCODE -eq 0 -and
                $formats -match '(?m)^\s*SVG\*?\s+(?:\S+\s+)?r[w-][+\-]?(?:\s|$)' -and
                (-not $legacy -or $formats -match '(?m)^\s*MVG\*?\s+(?:\S+\s+)?rw[+\-]?(?:\s|$)') -and
                $formats -match '(?m)^\s*PNG\*?\s+(?:\S+\s+)?rw[+\-]?(?:\s|$)') {
                if (Test-SocialCardRender $command.Source $legacy) { return $command.Source }
                throw "ImageMagick at $($command.Source) failed the SVG-to-PNG rendering probe. Check renderer diagnostics and runner policy; setup does not modify security policy."
            }
            Write-Host "ImageMagick at $($command.Source) lacks required SVG, MVG or PNG support."
            # Runtime also selects the first magick executable before convert.
            return $null
        }
    }
}

$renderer = Get-SocialCardRenderer
if ($renderer) {
    Write-Host "Social card renderer already available: $renderer (SVG-to-PNG render verified)."
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
Write-Host "Social card renderer ready: $renderer (SVG-to-PNG render verified)."

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

foreach ($name in @('magick', 'convert')) {
    $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        $version = & $command.Source -version 2>&1
        if ($LASTEXITCODE -eq 0 -and ($version -join "`n") -match 'ImageMagick') {
            Write-Host "Social card renderer already available: $($command.Source)"
            return
        }
    }
}

Write-Host 'Installing ImageMagick for Linux social card rendering.'
& sudo apt-get update
if ($LASTEXITCODE -ne 0) { throw 'apt-get update failed while preparing social cards.' }
& sudo apt-get install --yes --no-install-recommends imagemagick
if ($LASTEXITCODE -ne 0) { throw 'ImageMagick installation failed.' }
& convert -version
if ($LASTEXITCODE -ne 0) { throw 'ImageMagick is not available after installation.' }

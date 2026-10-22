$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { Write-Host 'Social card dependency fixtures run on Linux.'; return }

$root = Join-Path ([IO.Path]::GetTempPath()) ('pf-renderer-setup-' + [Guid]::NewGuid().ToString('N'))
$originalPath = $env:PATH
$installer = Join-Path $PSScriptRoot 'Install-WebsiteSocialCardDependencies.ps1'
$classic = "SVG SVG rw+`nMVG MVG rw-`nPNG PNG rw-"
$cases = @(
    @{ Name = 'classic'; Formats = $classic; Reject = $false },
    @{ Name = 'starred'; Formats = "SVG* SVG rw+`nMVG* MVG rw-`nPNG* PNG rw-"; Reject = $false },
    @{ Name = 'compact'; Formats = "SVG* rw+`nMVG* rw-`nPNG* rw-"; Reject = $false },
    @{ Name = 'unrelated-magick'; Formats = $classic; Version = 'Unrelated tool'; Reject = $true },
    @{ Name = 'incomplete-magick'; Formats = 'PNG PNG rw-'; Reject = $true },
    @{ Name = 'write-only-svg'; Formats = "SVG SVG -w+`nMVG MVG rw-`nPNG PNG rw-"; Reject = $true }
)
$failures = [Collections.Generic.List[string]]::new()
try {
    foreach ($case in $cases) {
        $bin = Join-Path $root $case.Name
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        foreach ($name in @('magick', 'convert', 'sudo')) {
            $path = Join-Path $bin $name
            $version = if ($name -eq 'magick' -and $case.Version) { $case.Version } else { 'ImageMagick 7.1.0' }
            $formats = if ($name -eq 'convert' -and $case.Reject) { $classic } else { $case.Formats }
            $formats = ($formats -split "`n" | ForEach-Object { '  ' + $_ }) -join "`n"
            $script = if ($name -eq 'sudo') { "#!/bin/sh`nexit 1`n" } else {
                @'
#!/bin/sh
if [ "$1" = '-version' ]; then
  printf '%s\n' '__VERSION__'
else
  printf '%s\n' '__FORMATS__'
fi
'@.Replace('__VERSION__', $version).Replace('__FORMATS__', $formats)
            }
            [IO.File]::WriteAllText($path, $script.Replace("`r", ''))
            [IO.File]::SetUnixFileMode($path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
        }
        $env:PATH = $bin + [IO.Path]::PathSeparator + $originalPath
        $failure = $null
        try { & $installer -Enabled $true } catch { $failure = $_ }
        if ($case.Reject -ne ($null -ne $failure)) {
            $failures.Add($case.Name)
            Write-Host "FAIL: $($case.Name); $failure"
        } else { Write-Host "PASS: $($case.Name)" }
    }
    if ($failures.Count -gt 0) { throw "Renderer setup contracts failed: $($failures -join ', ')" }
    $global:LASTEXITCODE = 0
} finally {
    $env:PATH = $originalPath
    if (Test-Path -LiteralPath $root) { [IO.Directory]::Delete($root, $true) }
}

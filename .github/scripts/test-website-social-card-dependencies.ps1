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
    @{ Name = 'write-only-svg'; Formats = "SVG SVG -w+`nMVG MVG rw-`nPNG PNG rw-"; Reject = $true },
    @{ Name = 'legacy'; Formats = $classic; Version = 'ImageMagick 6.9.12'; Reject = $false },
    @{ Name = 'legacy-read-only-mvg'; Formats = "SVG SVG rw+`nMVG MVG r--`nPNG PNG rw-"; Version = 'ImageMagick 6.9.12'; Reject = $true },
    @{ Name = 'modern-without-mvg'; Formats = "SVG SVG rw+`nPNG PNG rw-"; Reject = $true },
    @{ Name = 'modern-mvg-denied'; Formats = $classic; MvgProbe = 'exit 1'; Reject = $true },
    @{ Name = 'policy-denied'; Formats = $classic; Probe = 'exit 1'; Reject = $true },
    @{ Name = 'legacy-policy-denied'; Formats = $classic; Version = 'ImageMagick 6.9.12'; Probe = 'exit 1'; Reject = $true },
    @{ Name = 'legacy-mvg-denied'; Formats = $classic; Version = 'ImageMagick 6.9.12'; MvgProbe = 'exit 1'; Reject = $true },
    @{ Name = 'missing-output'; Formats = $classic; Probe = 'exit 0'; Reject = $true },
    @{ Name = 'invalid-output'; Formats = $classic; Probe = 'printf invalid > "$output"'; Reject = $true }
)
$failures = [Collections.Generic.List[string]]::new()
try {
    foreach ($case in $cases) {
        $bin = Join-Path $root $case.Name
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        foreach ($name in @('magick', 'convert', 'sudo')) {
            $path = Join-Path $bin $name
            $version = if ($name -eq 'magick' -and $case.Version) { $case.Version } else { 'ImageMagick 7.1.0' }
            $probe = if ($name -eq 'magick' -and $case.Probe) { $case.Probe } else { 'printf "\211PNG\015\012\032\012" > "$output"' }
            $mvgProbe = if ($name -eq 'magick' -and $case.MvgProbe) { $case.MvgProbe } else { 'printf ''compliance "SVG"\npush graphic-context\npop graphic-context\n'' > "$output"' }
            $formats = if ($name -eq 'convert' -and $case.Reject) { $classic } else { $case.Formats }
            $formats = ($formats -split "`n" | ForEach-Object { '  ' + $_ }) -join "`n"
            $script = if ($name -eq 'sudo') { "#!/bin/sh`nexit 1`n" } else {
                @'
#!/bin/sh
if [ "$1" = '-version' ]; then
  printf '%s\n' '__VERSION__'
elif [ "$1" = '-list' ]; then
  printf '%s\n' '__FORMATS__'
else
  printf '%s\n' "$*" >> '__LOG__'
  for last do :; done
  output="${last#*:}"
  case "$last" in
    MVG:*) __MVG_PROBE__ ;;
    PNG32:*) __PROBE__ ;;
    *) exit 1 ;;
  esac
fi
'@.Replace('__VERSION__', $version).Replace('__FORMATS__', $formats).Replace('__PROBE__', $probe).Replace('__MVG_PROBE__', $mvgProbe).Replace('__LOG__', (Join-Path $bin 'calls.log'))
            }
            [IO.File]::WriteAllText($path, $script.Replace("`r", ''))
            [IO.File]::SetUnixFileMode($path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
        }
        $env:PATH = $bin + [IO.Path]::PathSeparator + $originalPath
        $failure = $null
        try { & $installer -Enabled $true } catch { $failure = $_ }
        if (-not $case.Reject -and $null -eq $failure) {
            $calls = Get-Content -Raw (Join-Path $bin 'calls.log')
            if ($calls -notmatch 'MVG:' -or $calls -notmatch 'PNG32:') {
                $failure = 'Probe did not exercise the runtime rendering route.'
            }
        }
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

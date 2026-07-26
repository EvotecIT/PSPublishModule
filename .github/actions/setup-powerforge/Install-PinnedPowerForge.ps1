param(
    [Parameter(Mandatory)]
    [string] $ManifestPath
)

$ErrorActionPreference = 'Stop'

$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).Path
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json -Depth 20
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported PowerForge tool manifest schema '$($manifest.schemaVersion)'."
}

$version = [string] $manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "PowerForge tool manifest version must use x.y.z."
}

$repository = [string] $manifest.repository
if ([string]::IsNullOrWhiteSpace($repository)) {
    $repository = 'EvotecIT/PSPublishModule'
}
if ($repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "PowerForge repository must use owner/name."
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$rid = if ($IsMacOS) {
    "osx-$architecture"
} elseif ($IsLinux) {
    "linux-$architecture"
} elseif ($IsWindows) {
    "win-$architecture"
} else {
    throw 'PowerForge setup supports macOS, Linux, and Windows runners.'
}

$asset = $manifest.assets.$rid
if ($null -eq $asset) {
    throw "PowerForge tool manifest does not define asset '$rid'."
}

$expectedSha256 = ([string] $asset.sha256).ToLowerInvariant()
if ($expectedSha256 -notmatch '^[a-f0-9]{64}$') {
    throw "PowerForge asset '$rid' requires a 64-character SHA-256 digest."
}

$assetName = "PowerForge-$version-net10.0-$rid-SingleContained.zip"
$downloadUrl = "https://github.com/$repository/releases/download/v$version/$assetName"
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$executionScope = if ($env:GITHUB_RUN_ID) {
    "$($env:GITHUB_RUN_ID)-$($env:GITHUB_JOB)-$($env:GITHUB_RUN_ATTEMPT)"
} else {
    "local-$PID"
}
$executionScope = $executionScope -replace '[^A-Za-z0-9_.-]', '-'
$downloadRoot = Join-Path $tempRoot "powerforge-$version-$rid-$($expectedSha256.Substring(0, 12))-$executionScope"
$installRoot = Join-Path $downloadRoot 'bin'
$archivePath = Join-Path $downloadRoot $assetName
$executableName = if ($IsWindows) { 'PowerForge.exe' } else { 'PowerForge' }
$toolPath = Join-Path $installRoot $executableName

New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    $downloaded = $false
    for ($attempt = 1; $attempt -le 3 -and -not $downloaded; $attempt++) {
        try {
            Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
            $downloaded = $true
        } catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
        }
    }

}

$actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256) {
    throw "PowerForge asset checksum mismatch for '$assetName'. Expected '$expectedSha256', received '$actualSha256'."
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Expand-Archive -LiteralPath $archivePath -DestinationPath $installRoot -Force

if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
    throw "Verified PowerForge archive did not contain '$executableName'."
}
if (-not $IsWindows) {
    & chmod +x $toolPath
    if ($LASTEXITCODE -ne 0) { throw "Failed to mark '$toolPath' executable." }
}

$probe = & $toolPath apple-release --help --output json
if ($LASTEXITCODE -ne 0 -or ($probe -join "`n") -notmatch 'apple-release') {
    throw "The verified PowerForge executable does not expose the Apple release command."
}

if ($env:GITHUB_PATH) {
    $installRoot | Out-File -FilePath $env:GITHUB_PATH -Encoding utf8 -Append
}
if ($env:GITHUB_OUTPUT) {
    "tool-path=$toolPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "rid=$rid" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

[pscustomobject]@{
    ToolPath = $toolPath
    Version = $version
    Rid = $rid
    Sha256 = $expectedSha256
}

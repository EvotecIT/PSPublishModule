[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ManifestPath,

    [string] $CacheRoot,

    [string] $ArtifactRoot,

    [switch] $AddToPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-SafePathSegment {
    param([string] $Value, [string] $Name)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        throw "$Name contains unsupported characters."
    }
}

function Get-ManifestPropertyValue {
    param([object] $Object, [string] $Name)
    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).Path
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
$schemaVersion = Get-ManifestPropertyValue -Object $manifest -Name 'schemaVersion'
if ($schemaVersion -notin @(1, 2)) {
    throw "Unsupported PowerForge tool manifest schema '$schemaVersion'."
}

$version = [string] (Get-ManifestPropertyValue -Object $manifest -Name 'version')
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'PowerForge tool manifest version must use x.y.z.'
}

$releaseTag = [string] (Get-ManifestPropertyValue -Object $manifest -Name 'releaseTag')
if ([string]::IsNullOrWhiteSpace($releaseTag)) {
    $releaseTag = "v$version"
}
Assert-SafePathSegment -Value $releaseTag -Name 'PowerForge release tag'

$repository = [string] (Get-ManifestPropertyValue -Object $manifest -Name 'repository')
if ([string]::IsNullOrWhiteSpace($repository)) {
    $repository = 'EvotecIT/PSPublishModule'
}
if ($repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'PowerForge repository must use owner/name.'
}
$expectedCommit = [string] (Get-ManifestPropertyValue -Object $manifest -Name 'commit')
if (-not [string]::IsNullOrWhiteSpace($expectedCommit) -and $expectedCommit -notmatch '^[A-Fa-f0-9]{40}$') {
    throw 'PowerForge tool manifest commit must be an exact 40-character Git SHA.'
}

$isWindowsHost = $PSVersionTable.PSEdition -eq 'Desktop' -or
    [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$isMacOSHost = -not $isWindowsHost -and
    [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)
$isLinuxHost = -not $isWindowsHost -and
    [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$rid = if ($isMacOSHost) {
    "osx-$architecture"
} elseif ($isLinuxHost) {
    "linux-$architecture"
} elseif ($isWindowsHost) {
    "win-$architecture"
} else {
    throw 'PowerForge supports macOS, Linux, and Windows.'
}

$assets = Get-ManifestPropertyValue -Object $manifest -Name 'assets'
$asset = Get-ManifestPropertyValue -Object $assets -Name $rid
if ($null -eq $asset) {
    throw "PowerForge tool manifest does not define asset '$rid'."
}
$expectedSha256 = ([string] (Get-ManifestPropertyValue -Object $asset -Name 'sha256')).ToLowerInvariant()
if ($expectedSha256 -notmatch '^[a-f0-9]{64}$') {
    throw "PowerForge asset '$rid' requires a 64-character SHA-256 digest."
}
$expectedExecutableSha256 = ([string] (Get-ManifestPropertyValue -Object $asset -Name 'executableSha256')).ToLowerInvariant()
$legacyArchiveOnlyManifest = $schemaVersion -eq 1 -and [string]::IsNullOrWhiteSpace($expectedExecutableSha256)
if (-not $legacyArchiveOnlyManifest -and $expectedExecutableSha256 -notmatch '^[a-f0-9]{64}$') {
    throw "PowerForge asset '$rid' requires a 64-character executable SHA-256 digest."
}
$trustedExecutableSha256 = $expectedExecutableSha256

$assetName = [string] (Get-ManifestPropertyValue -Object $asset -Name 'name')
if ([string]::IsNullOrWhiteSpace($assetName)) {
    $assetName = "PowerForge-$version-net10.0-$rid-SingleContained.zip"
}
if ([IO.Path]::GetFileName($assetName) -cne $assetName -or $assetName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$') {
    throw "PowerForge asset '$rid' has an unsafe archive name."
}

if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_TOOL_CACHE)) {
        $env:POWERFORGE_TOOL_CACHE
    } elseif ($isWindowsHost -and -not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Join-Path $env:LOCALAPPDATA 'PowerForge\tools'
    } else {
        Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.powerforge/tools'
    }
}
$CacheRoot = [IO.Path]::GetFullPath($CacheRoot)
$installRoot = Join-Path $CacheRoot "$version/$rid/$expectedSha256"
$executableName = if ($isWindowsHost) { 'PowerForge.exe' } else { 'PowerForge' }
$toolPath = Join-Path $installRoot $executableName
$markerPath = Join-Path $installRoot '.powerforge-install.json'

function Test-InstalledTool {
    if ([string]::IsNullOrWhiteSpace($trustedExecutableSha256)) {
        return $false
    }
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        return $false
    }
    try {
        $actualExecutableSha256 = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualExecutableSha256 -cne $trustedExecutableSha256) {
            return $false
        }
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ([string] $marker.sha256 -cne $expectedSha256 -or
            [string] $marker.executableSha256 -cne $trustedExecutableSha256 -or
            [string] $marker.version -cne $version) {
            return $false
        }
        $versionOutput = @(& $toolPath --version 2>$null)
        $toolExitCode = $LASTEXITCODE
        $observedVersion = if ($versionOutput.Count -gt 0) { ([string] $versionOutput[0]).Trim() } else { '' }
        $versionMatches = $observedVersion -ceq $version -or $observedVersion.StartsWith("$version+", [StringComparison]::Ordinal)
        $commitMatches = [string]::IsNullOrWhiteSpace($expectedCommit) -or
            $observedVersion.EndsWith("+$expectedCommit", [StringComparison]::OrdinalIgnoreCase)
        return $toolExitCode -eq 0 -and $versionMatches -and $commitMatches
    } catch {
        return $false
    }
}

$reused = Test-InstalledTool
if (-not $reused) {
    $tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
    $attemptRoot = Join-Path $tempRoot "powerforge-install-$PID-$([Guid]::NewGuid().ToString('N'))"
    $archivePath = Join-Path $attemptRoot $assetName
    $installParent = Split-Path -Parent $installRoot
    New-Item -ItemType Directory -Path $installParent -Force | Out-Null
    Get-ChildItem -LiteralPath $installParent -Directory -Filter '.powerforge-stage-*' -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
            $_.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-1)
        } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    $stageRoot = Join-Path $installParent ".powerforge-stage-$PID-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $attemptRoot -Force | Out-Null
    try {
        if (-not [string]::IsNullOrWhiteSpace($ArtifactRoot)) {
            $localArchive = Join-Path ([IO.Path]::GetFullPath($ArtifactRoot)) $assetName
            Copy-Item -LiteralPath $localArchive -Destination $archivePath -Force
        } else {
            $downloadUrl = "https://github.com/$repository/releases/download/$releaseTag/$assetName"
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                try {
                    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
                    break
                } catch {
                    if ($attempt -eq 3) { throw }
                    Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
                }
            }
        }

        $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSha256 -cne $expectedSha256) {
            throw "PowerForge asset checksum mismatch for '$assetName'. Expected '$expectedSha256', received '$actualSha256'."
        }

        New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
        Expand-Archive -LiteralPath $archivePath -DestinationPath $stageRoot -Force
        $stagedTool = Join-Path $stageRoot $executableName
        if (-not (Test-Path -LiteralPath $stagedTool -PathType Leaf)) {
            throw "Verified PowerForge archive did not contain '$executableName'."
        }
        $stagedExecutableSha256 = (Get-FileHash -LiteralPath $stagedTool -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not [string]::IsNullOrWhiteSpace($expectedExecutableSha256) -and
            $stagedExecutableSha256 -cne $expectedExecutableSha256) {
            throw "PowerForge executable checksum mismatch. Expected '$expectedExecutableSha256', received '$stagedExecutableSha256'."
        }
        if ($legacyArchiveOnlyManifest) {
            $trustedExecutableSha256 = $stagedExecutableSha256
        }
        if (-not $isWindowsHost) {
            & chmod +x $stagedTool
            if ($LASTEXITCODE -ne 0) { throw "Failed to mark '$stagedTool' executable." }
        }
        $versionOutput = @(& $stagedTool --version)
        $toolExitCode = $LASTEXITCODE
        $observedVersion = if ($versionOutput.Count -gt 0) { ([string] $versionOutput[0]).Trim() } else { '' }
        $versionMatches = $observedVersion -ceq $version -or $observedVersion.StartsWith("$version+", [StringComparison]::Ordinal)
        $commitMatches = [string]::IsNullOrWhiteSpace($expectedCommit) -or
            $observedVersion.EndsWith("+$expectedCommit", [StringComparison]::OrdinalIgnoreCase)
        if ($toolExitCode -ne 0 -or -not $versionMatches -or -not $commitMatches) {
            throw "Verified PowerForge executable version '$observedVersion' does not match manifest version '$version'."
        }
        $probe = & $stagedTool apple-release --help --output json
        if ($LASTEXITCODE -ne 0 -or ($probe -join "`n") -notmatch 'apple-release') {
            throw 'The verified PowerForge executable does not expose the Apple release command.'
        }
        [ordered]@{
            schemaVersion = 2
            version = $version
            rid = $rid
            sha256 = $expectedSha256
            executableSha256 = $trustedExecutableSha256
            sourceManifest = $resolvedManifest
            installedAtUtc = [DateTime]::UtcNow.ToString('O')
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stageRoot '.powerforge-install.json') -Encoding utf8

        if ((Test-Path -LiteralPath $installRoot) -and -not (Test-InstalledTool)) {
            $quarantineRoot = "$installRoot.invalid-$([Guid]::NewGuid().ToString('N'))"
            try {
                Move-Item -LiteralPath $installRoot -Destination $quarantineRoot -ErrorAction Stop
                Remove-Item -LiteralPath $quarantineRoot -Recurse -Force -ErrorAction Stop
            } catch {
                if ((Test-Path -LiteralPath $installRoot) -and -not (Test-InstalledTool)) { throw }
            }
        }
        try {
            [IO.Directory]::Move($stageRoot, $installRoot)
        } catch {
            if (-not (Test-InstalledTool)) { throw }
        }
    } finally {
        Remove-Item -LiteralPath $attemptRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-InstalledTool)) {
    throw "PowerForge installation did not converge at '$installRoot'."
}
if ($AddToPath -and $env:GITHUB_PATH) {
    $installRoot | Out-File -FilePath $env:GITHUB_PATH -Encoding utf8 -Append
}
if ($env:GITHUB_OUTPUT) {
    "tool-path=$toolPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "rid=$rid" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "reused=$($reused.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

[pscustomobject]@{
    ToolPath = $toolPath
    Version = $version
    Rid = $rid
    Sha256 = $expectedSha256
    ExecutableSha256 = $trustedExecutableSha256
    Reused = $reused
}

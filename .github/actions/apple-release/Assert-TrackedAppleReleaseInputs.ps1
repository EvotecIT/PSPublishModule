param(
    [Parameter(Mandatory)] [string] $ConfigPath,
    [string] $ToolManifestPath,
    [string] $SourceCommit,
    [switch] $SkipToolManifest
)

$ErrorActionPreference = 'Stop'

function Assert-TrackedSourceFile {
    param(
        [Parameter(Mandatory)] [string] $SourceRoot,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Name
    )

    $root = [IO.Path]::GetFullPath($SourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $candidate = [IO.Path]::GetFullPath($Path)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, $comparison)) {
        throw "$Name must resolve inside the checked-out source: $candidate"
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "$Name was not found: $candidate"
    }

    $current = $candidate
    while ($true) {
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Name must not traverse a symbolic link or reparse point: $current"
        }
        if ($current.Equals($root, $comparison)) { break }
        $current = Split-Path -Parent $current
    }

    $relative = [IO.Path]::GetRelativePath($root, $candidate).Replace('\', '/')
    & git -C $root ls-files --error-unmatch -- $relative *> $null
    if ($LASTEXITCODE -ne 0) { throw "$Name must be tracked at the exact source commit: $relative" }
    & git -C $root diff --quiet HEAD -- $relative
    if ($LASTEXITCODE -ne 0) { throw "$Name differs from the exact source commit: $relative" }
}

$configFullPath = [IO.Path]::GetFullPath($ConfigPath)
$configDirectory = Split-Path -Parent $configFullPath
$sourceRoot = (& git -C $configDirectory rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceRoot)) {
    throw 'config-path must belong to a Git checkout.'
}
$sourceRoot = [IO.Path]::GetFullPath($sourceRoot)

if (-not [string]::IsNullOrWhiteSpace($SourceCommit)) {
    if ($SourceCommit -notmatch '^[0-9A-Fa-f]{40}$') { throw 'source-commit must be an exact 40-character commit SHA.' }
    $actualCommit = (& git -C $sourceRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $actualCommit.Equals($SourceCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Checked-out source '$actualCommit' does not match source-commit '$SourceCommit'."
    }
}

Assert-TrackedSourceFile -SourceRoot $sourceRoot -Path $configFullPath -Name 'config-path'
if (-not $SkipToolManifest) {
    Assert-TrackedSourceFile -SourceRoot $sourceRoot -Path $ToolManifestPath -Name 'tool-manifest-path'
}

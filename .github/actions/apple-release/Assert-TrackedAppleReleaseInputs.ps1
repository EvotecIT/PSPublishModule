param(
    [Parameter(Mandatory)] [string] $ConfigPath,
    [string] $ToolManifestPath,
    [string] $SourceCommit,
    [string] $GitPath = 'git',
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
    & $GitPath -C $root ls-files --error-unmatch -- $relative *> $null
    if ($LASTEXITCODE -ne 0) { throw "$Name must be tracked at the exact source commit: $relative" }
    & $GitPath -C $root diff --quiet HEAD -- $relative
    if ($LASTEXITCODE -ne 0) { throw "$Name differs from the exact source commit: $relative" }
}

$configFullPath = [IO.Path]::GetFullPath($ConfigPath)
$configDirectory = Split-Path -Parent $configFullPath
$sourceRoot = (& $GitPath -C $configDirectory rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceRoot)) {
    throw 'config-path must belong to a Git checkout.'
}
$sourceRoot = [IO.Path]::GetFullPath($sourceRoot)

if (-not [string]::IsNullOrWhiteSpace($SourceCommit)) {
    if ($SourceCommit -notmatch '^[0-9A-Fa-f]{40}$') { throw 'source-commit must be an exact 40-character commit SHA.' }
    $actualCommit = (& $GitPath -C $sourceRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $actualCommit.Equals($SourceCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Checked-out source '$actualCommit' does not match source-commit '$SourceCommit'."
    }
}

Assert-TrackedSourceFile -SourceRoot $sourceRoot -Path $configFullPath -Name 'config-path'
if (-not $SkipToolManifest) {
    Assert-TrackedSourceFile -SourceRoot $sourceRoot -Path $ToolManifestPath -Name 'tool-manifest-path'
}

$config = Get-Content -LiteralPath $configFullPath -Raw | ConvertFrom-Json -Depth 100
$projectRootSetting = [string] $config.AppleApps.ProjectRoot
if ([string]::IsNullOrWhiteSpace($projectRootSetting)) { $projectRootSetting = '.' }
$projectRoot = if ([IO.Path]::IsPathRooted($projectRootSetting)) {
    [IO.Path]::GetFullPath($projectRootSetting)
} else {
    [IO.Path]::GetFullPath((Join-Path $configDirectory $projectRootSetting))
}
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$sourcePrefix = $sourceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $projectRoot.Equals($sourceRoot, $comparison) -and
    -not $projectRoot.StartsWith($sourcePrefix, $comparison)) {
    throw "AppleApps.ProjectRoot must resolve inside the exact checked-out source: $projectRoot"
}
if (-not (Test-Path -LiteralPath $projectRoot -PathType Container)) {
    throw "AppleApps.ProjectRoot was not found inside the exact checked-out source: $projectRoot"
}

$current = $projectRoot
while ($true) {
    $item = Get-Item -LiteralPath $current -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "AppleApps.ProjectRoot must not traverse a symbolic link or reparse point: $current"
    }
    if ($current.Equals($sourceRoot, $comparison)) { break }
    $current = Split-Path -Parent $current
}

$trackedInputProperties = @(
    'ScreenshotConfigPath',
    'ScreenshotConfigPaths',
    'MetadataConfigPath',
    'MetadataConfigPaths',
    'AppInfoConfigPath',
    'AppInfoConfigPaths',
    'GovernanceConfigPath',
    'GovernanceConfigPaths'
)
foreach ($propertyName in $trackedInputProperties) {
    foreach ($configuredPath in @($config.AppleApps.$propertyName)) {
        if ([string]::IsNullOrWhiteSpace([string] $configuredPath)) { continue }
        $inputPath = if ([IO.Path]::IsPathRooted([string] $configuredPath)) {
            [IO.Path]::GetFullPath([string] $configuredPath)
        } else {
            [IO.Path]::GetFullPath((Join-Path $projectRoot ([string] $configuredPath)))
        }
        Assert-TrackedSourceFile `
            -SourceRoot $sourceRoot `
            -Path $inputPath `
            -Name "AppleApps.$propertyName"
    }
}

foreach ($app in @($config.AppleApps.Apps)) {
    $configuredProjectPath = [string] $app.ProjectPath
    if ([string]::IsNullOrWhiteSpace($configuredProjectPath)) { continue }
    $projectPath = if ([IO.Path]::IsPathRooted($configuredProjectPath)) {
        [IO.Path]::GetFullPath($configuredProjectPath)
    } else {
        [IO.Path]::GetFullPath((Join-Path $projectRoot $configuredProjectPath))
    }
    if (-not $projectPath.StartsWith($sourcePrefix, $comparison)) {
        throw "AppleApps.Apps.ProjectPath must resolve inside the exact checked-out source: $projectPath"
    }
    if (Test-Path -LiteralPath $projectPath -PathType Leaf) {
        Assert-TrackedSourceFile -SourceRoot $sourceRoot -Path $projectPath -Name 'AppleApps.Apps.ProjectPath'
    } elseif (Test-Path -LiteralPath $projectPath -PathType Container) {
        Assert-TrackedSourceFile `
            -SourceRoot $sourceRoot `
            -Path (Join-Path $projectPath 'project.pbxproj') `
            -Name 'AppleApps.Apps.ProjectPath/project.pbxproj'
    } elseif ([bool] $app.GenerateProjectIfMissing) {
        Assert-TrackedSourceFile `
            -SourceRoot $sourceRoot `
            -Path (Join-Path $projectRoot 'project.yml') `
            -Name 'AppleApps.Apps.ProjectPath generation source'
    } else {
        throw "AppleApps.Apps.ProjectPath was not found inside the exact checked-out source: $projectPath"
    }
}

$versionSourcePath = [string] $config.AppleApps.Automation.VersionSourcePath
if (-not [string]::IsNullOrWhiteSpace($versionSourcePath)) {
    $inputPath = if ([IO.Path]::IsPathRooted($versionSourcePath)) {
        [IO.Path]::GetFullPath($versionSourcePath)
    } else {
        [IO.Path]::GetFullPath((Join-Path $projectRoot $versionSourcePath))
    }
    Assert-TrackedSourceFile `
        -SourceRoot $sourceRoot `
        -Path $inputPath `
        -Name 'AppleApps.Automation.VersionSourcePath'
}

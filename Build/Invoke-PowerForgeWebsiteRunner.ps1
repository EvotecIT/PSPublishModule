[CmdletBinding()] param(
    [Parameter(Mandatory)]
    [string] $WebsiteRoot,
    [Parameter(Mandatory)]
    [string] $PipelineConfig,
    [ValidateSet('source', 'binary')]
    [string] $EngineMode = 'source',
    [string] $PipelineMode = 'ci',
    [string] $PowerForgeLockPath,
    [string] $PowerForgeRepository,
    [string] $PowerForgeRef,
    [string] $PowerForgeRepositoryOverride,
    [string] $PowerForgeRefOverride,
    [string] $PowerForgeToolLockPath,
    [switch] $MaintenanceModeNote
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-NormalizedPath {
    param(
        [string] $Path,
        [string] $BasePath
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $TargetPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $rootFull = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\', '/')
    $targetFull = [System.IO.Path]::GetFullPath($TargetPath)
    $comparison = [System.StringComparison]::OrdinalIgnoreCase

    if ($targetFull.Equals($rootFull, $comparison)) {
        return
    }

    if (-not $targetFull.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, $comparison) -and
        -not $targetFull.StartsWith($rootFull + [System.IO.Path]::AltDirectorySeparatorChar, $comparison)) {
        throw "$Description must stay within '$rootFull' but resolved to '$targetFull'."
    }
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    $data = $raw | ConvertFrom-Json
    if ($null -eq $data) {
        throw "Invalid JSON in ${Description}: $Path"
    }

    return $data
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,
        [Parameter(Mandatory)]
        [string[]] $Arguments,
        [switch] $IgnoreExitCode
    )

    & $FilePath @Arguments
    if (-not $IgnoreExitCode -and $LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Get-GitHubRemoteUrl {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,
        [string] $Token
    )

    if ($Repository -match '^(?:https?|git)://') {
        return $Repository
    }

    if ([string]::IsNullOrWhiteSpace($Token)) {
        return "https://github.com/$Repository.git"
    }

    return "https://x-access-token:$Token@github.com/$Repository.git"
}

function Resolve-WebsiteExecutablePath {
    param(
        [Parameter(Mandatory)]
        [string] $ExtractRoot,
        [string] $BinaryPath,
        [string] $Target = 'PowerForgeWeb'
    )

    if (-not [string]::IsNullOrWhiteSpace($BinaryPath)) {
        $resolvedBinaryPath = Resolve-NormalizedPath -Path $BinaryPath -BasePath $ExtractRoot
        if (-not (Test-Path -LiteralPath $resolvedBinaryPath)) {
            throw "Configured binaryPath was not found after extraction: $resolvedBinaryPath"
        }

        return $resolvedBinaryPath
    }

    $candidates = Get-ChildItem -LiteralPath $ExtractRoot -Recurse -File | Where-Object {
        $_.BaseName -eq $Target -or $_.Name -eq "$Target.exe"
    }

    if ($candidates.Count -eq 1) {
        return $candidates[0].FullName
    }

    if ($candidates.Count -eq 0) {
        throw "Unable to locate executable for target '$Target' under '$ExtractRoot'."
    }

    throw "Multiple executables matched target '$Target' under '$ExtractRoot': $($candidates.FullName -join ', ')"
}

$workspaceRoot = (Get-Location).Path
$websiteRootPath = Resolve-NormalizedPath -Path $WebsiteRoot -BasePath $workspaceRoot
$pipelineConfigPath = Resolve-NormalizedPath -Path $PipelineConfig -BasePath $workspaceRoot
$gitHubToken = if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }
$runnerTempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetTempPath()
} else {
    [System.IO.Path]::GetFullPath($env:RUNNER_TEMP)
}
$sessionRoot = Join-Path $runnerTempRoot ("powerforge-website-runner-" + [guid]::NewGuid().ToString('N'))

if ([string]::IsNullOrWhiteSpace($websiteRootPath) -or -not (Test-Path -LiteralPath $websiteRootPath)) {
    throw "Website root not found: $WebsiteRoot"
}

if ([string]::IsNullOrWhiteSpace($pipelineConfigPath) -or -not (Test-Path -LiteralPath $pipelineConfigPath)) {
    throw "Pipeline config not found: $PipelineConfig"
}

if ($MaintenanceModeNote.IsPresent) {
    Write-Host 'Maintenance uses the same strict CI mode; the selected pipeline config controls which tasks run.'
}

try {
    New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null

    if ($EngineMode -eq 'source') {
        $lockPath = Resolve-NormalizedPath -Path $PowerForgeLockPath -BasePath $workspaceRoot
        if ([string]::IsNullOrWhiteSpace($lockPath)) {
            $lockPath = Join-Path $websiteRootPath '.powerforge/engine-lock.json'
        }

        $resolvedRepository = [string] $PowerForgeRepository
        $resolvedRef = [string] $PowerForgeRef

        if (([string]::IsNullOrWhiteSpace($resolvedRepository) -or [string]::IsNullOrWhiteSpace($resolvedRef)) -and (Test-Path -LiteralPath $lockPath)) {
            $lock = Read-JsonFile -Path $lockPath -Description 'engine lock'

            if ([string]::IsNullOrWhiteSpace($resolvedRepository)) {
                $resolvedRepository = [string] $lock.repository
            }

            if ([string]::IsNullOrWhiteSpace($resolvedRef)) {
                $resolvedRef = [string] $lock.ref
            }
        }

        if ([string]::IsNullOrWhiteSpace($resolvedRepository) -or [string]::IsNullOrWhiteSpace($resolvedRef)) {
            throw "Provide either PowerForge repository/ref overrides or a valid engine lock file at '$lockPath'."
        }

        $finalRepository = if ([string]::IsNullOrWhiteSpace($PowerForgeRepositoryOverride)) { $resolvedRepository } else { $PowerForgeRepositoryOverride }
        $finalRef = if ([string]::IsNullOrWhiteSpace($PowerForgeRefOverride)) { $resolvedRef } else { $PowerForgeRefOverride }

        if ($finalRef -notmatch '^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$') {
            throw "PowerForge ref must be an immutable commit SHA (40/64 hex): '$finalRef'."
        }

        $engineRoot = Join-Path $sessionRoot 'engine'
        Invoke-NativeCommand -FilePath 'git' -Arguments @('clone', '--filter=blob:none', '--no-checkout', '--quiet', (Get-GitHubRemoteUrl -Repository $finalRepository -Token $gitHubToken), $engineRoot)
        Invoke-NativeCommand -FilePath 'git' -Arguments @('-C', $engineRoot, 'fetch', '--depth', '1', 'origin', $finalRef)
        Invoke-NativeCommand -FilePath 'git' -Arguments @('-C', $engineRoot, '-c', 'advice.detachedHead=false', 'checkout', '--force', 'FETCH_HEAD')

        $projectPath = Join-Path $engineRoot 'PowerForge.Web.Cli/PowerForge.Web.Cli.csproj'
        Invoke-NativeCommand -FilePath 'dotnet' -Arguments @('run', '--framework', 'net10.0', '--project', $projectPath, '--', 'pipeline', '--config', $pipelineConfigPath, '--mode', $PipelineMode)
        return
    }

    $toolLockPath = Resolve-NormalizedPath -Path $PowerForgeToolLockPath -BasePath $workspaceRoot
    if ([string]::IsNullOrWhiteSpace($toolLockPath)) {
        $toolLockPath = Join-Path $websiteRootPath '.powerforge/tool-lock.json'
    }

    $toolLock = Read-JsonFile -Path $toolLockPath -Description 'tool lock'
    $toolRepository = [string] $toolLock.repository
    $toolTag = [string] $toolLock.tag
    $toolAsset = [string] $toolLock.asset
    $toolTarget = if ([string]::IsNullOrWhiteSpace([string] $toolLock.target)) { 'PowerForgeWeb' } else { [string] $toolLock.target }
    $toolBinaryPath = [string] $toolLock.binaryPath

    if ([string]::IsNullOrWhiteSpace($toolRepository) -or [string]::IsNullOrWhiteSpace($toolTag) -or [string]::IsNullOrWhiteSpace($toolAsset)) {
        throw "Tool lock must define repository, tag, and asset: $toolLockPath"
    }

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI 'gh' is required for binary mode."
    }

    if ([string]::IsNullOrWhiteSpace($gitHubToken)) {
        throw "Binary mode requires GITHUB_TOKEN or GH_TOKEN so the workflow can download release assets."
    }

    $env:GH_TOKEN = $gitHubToken

    $downloadRoot = Join-Path $sessionRoot 'download'
    $extractRoot = Join-Path $sessionRoot 'extract'
    Invoke-NativeCommand -FilePath 'gh' -Arguments @('release', 'download', $toolTag, '--repo', $toolRepository, '--pattern', $toolAsset, '--dir', $downloadRoot)

    $assetPath = Join-Path $downloadRoot $toolAsset
    if (-not (Test-Path -LiteralPath $assetPath)) {
        throw "Downloaded release asset was not found: $assetPath"
    }

    New-Item -ItemType Directory -Path $extractRoot | Out-Null

    if ($assetPath.EndsWith('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -LiteralPath $assetPath -DestinationPath $extractRoot -Force
    } elseif ($assetPath.EndsWith('.tar.gz', [System.StringComparison]::OrdinalIgnoreCase) -or $assetPath.EndsWith('.tgz', [System.StringComparison]::OrdinalIgnoreCase)) {
        Invoke-NativeCommand -FilePath 'tar' -Arguments @('-xzf', $assetPath, '-C', $extractRoot)
    } else {
        throw "Unsupported tool asset format: $assetPath"
    }

    $executablePath = Resolve-WebsiteExecutablePath -ExtractRoot $extractRoot -BinaryPath $toolBinaryPath -Target $toolTarget
    if (-not $IsWindows) {
        Invoke-NativeCommand -FilePath 'chmod' -Arguments @('+x', $executablePath)
    }

    Invoke-NativeCommand -FilePath $executablePath -Arguments @('pipeline', '--config', $pipelineConfigPath, '--mode', $PipelineMode)
}
finally {
    if (Test-Path -LiteralPath $sessionRoot) {
        Remove-Item -LiteralPath $sessionRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

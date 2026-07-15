[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Resolve-CanonicalPath {
    param([Parameter(Mandatory)][string] $Path)

    $resolved = realpath --canonicalize-existing -- $Path
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$resolved)) {
        throw "Unable to resolve canonical path: $Path"
    }
    return [IO.Path]::GetFullPath(([string]$resolved).Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar)
}

function Assert-WorkspacePath {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Workspace,
        [Parameter(Mandatory)][string] $Description
    )

    $workspacePrefix = $Workspace + [IO.Path]::DirectorySeparatorChar
    if (-not [string]::Equals($Path, $Workspace, [StringComparison]::Ordinal) -and
        -not $Path.StartsWith($workspacePrefix, [StringComparison]::Ordinal)) {
        throw "$Description resolved outside the caller repository."
    }
}

if ($env:RUNNER_OS -ne 'Linux') {
    throw 'PowerForge Linux service packaging requires a Linux runner.'
}
if ($env:POWERFORGE_SOURCE_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'Unable to resolve the caller repository for service provenance.'
}
if ($env:POWERFORGE_SOURCE_SHA -notmatch '^[a-fA-F0-9]{40,64}$') {
    throw 'Unable to resolve an exact caller commit for service provenance.'
}
if ($env:GITHUB_RUN_ID -notmatch '^\d+$' -or $env:GITHUB_RUN_ATTEMPT -notmatch '^\d+$') {
    throw 'GitHub workflow run identity is missing or invalid.'
}

$workspace = Resolve-CanonicalPath -Path $env:GITHUB_WORKSPACE
$serviceRoot = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_SERVICE_ROOT))
Assert-WorkspacePath -Path $serviceRoot -Workspace $workspace -Description 'service-root'

if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_SERVICE_VALIDATION_SCRIPT)) {
    $validationCandidate = [IO.Path]::GetFullPath((Join-Path $workspace $env:POWERFORGE_SERVICE_VALIDATION_SCRIPT))
    Assert-WorkspacePath -Path $validationCandidate -Workspace $workspace -Description 'service-validation-script'
    if (-not (Test-Path -LiteralPath $validationCandidate -PathType Leaf)) {
        throw 'service-validation-script must identify a file inside the caller repository.'
    }
    $validationScript = Resolve-CanonicalPath -Path $validationCandidate
    Assert-WorkspacePath -Path $validationScript -Workspace $workspace -Description 'service-validation-script'
    $workflowCommandVariables = @('GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_OUTPUT', 'GITHUB_STEP_SUMMARY')
    $workflowCommandValues = @{}
    try {
        foreach ($name in $workflowCommandVariables) {
            $workflowCommandValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        bash $validationScript
        if ($LASTEXITCODE -ne 0) {
            throw "Validating and preparing the service failed with exit code $LASTEXITCODE."
        }
    } finally {
        foreach ($name in $workflowCommandVariables) {
            [Environment]::SetEnvironmentVariable($name, $workflowCommandValues[$name], 'Process')
        }
    }
}

if (-not (Test-Path -LiteralPath $serviceRoot -PathType Container)) {
    throw "Service root not found after validation: $serviceRoot"
}
$resolvedServiceRoot = Resolve-CanonicalPath -Path $serviceRoot
Assert-WorkspacePath -Path $resolvedServiceRoot -Workspace $workspace -Description 'service-root'

$runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$packageRoot = [IO.Path]::GetFullPath((Join-Path $runnerTemp 'powerforge-service-package'))
if (-not $packageRoot.StartsWith($runnerTemp + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Service package staging escaped the runner temporary directory.'
}
$artifactPath = Join-Path $packageRoot 'artifact.tar'
$packageMetadataPath = Join-Path $packageRoot 'package.json'
if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot | Out-Null

tar --directory $resolvedServiceRoot -cf $artifactPath --exclude=.git --exclude=.github --exclude=_powerforge .
if ($LASTEXITCODE -ne 0) {
    throw "Archiving the service artifact failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf) -or (Get-Item -LiteralPath $artifactPath).Length -eq 0) {
    throw 'The packaged service artifact is missing or empty.'
}

$artifactSha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
[ordered]@{
    schemaVersion      = 1
    sourceRepository   = $env:POWERFORGE_SOURCE_REPOSITORY
    sourceSha          = $env:POWERFORGE_SOURCE_SHA.ToLowerInvariant()
    workflowRunId      = $env:GITHUB_RUN_ID
    workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
    artifactSha256     = $artifactSha256
    packagedAtUtc      = [DateTimeOffset]::UtcNow.ToString('O')
} | ConvertTo-Json | Set-Content -LiteralPath $packageMetadataPath -Encoding utf8NoBOM
"artifact-sha256=$artifactSha256" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append

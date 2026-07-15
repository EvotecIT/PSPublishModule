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
    throw 'PowerForge Linux service validation requires a Linux runner.'
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
    bash $validationScript
    if ($LASTEXITCODE -ne 0) {
        throw "Validating and preparing the service failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $serviceRoot -PathType Container)) {
    throw "Service root not found after validation: $serviceRoot"
}
$resolvedServiceRoot = Resolve-CanonicalPath -Path $serviceRoot
Assert-WorkspacePath -Path $resolvedServiceRoot -Workspace $workspace -Description 'service-root'

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DeploymentTarget,
    [AllowEmptyString()][string] $DeploymentUrl = '',
    [Parameter(Mandatory)][string] $PipelineConfig,
    [string] $RequireSeoDoctorGuardrail = 'true',
    [string] $Workspace = $env:GITHUB_WORKSPACE
)

$ErrorActionPreference = 'Stop'

if ($DeploymentTarget -notin @('github-pages', 'linux')) {
    throw "Unsupported deployment target '$DeploymentTarget'. Expected github-pages or linux."
}
if ($DeploymentTarget -eq 'linux' -and [string]::IsNullOrWhiteSpace($DeploymentUrl)) {
    throw 'deployment_url is required when deployment_target is linux.'
}
if ($RequireSeoDoctorGuardrail -ne 'true') {
    Write-Host 'SEO doctor deploy guardrail disabled for this workflow call.'
    return
}
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = (Get-Location).Path
}

$workspaceRoot = [IO.Path]::GetFullPath($Workspace).TrimEnd([IO.Path]::DirectorySeparatorChar)
$workspacePrefix = $workspaceRoot + [IO.Path]::DirectorySeparatorChar

function Resolve-WorkspacePath {
    param([Parameter(Mandatory)][string] $Path)

    $candidate = if ([IO.Path]::IsPathFullyQualified($Path)) { $Path } else { Join-Path $workspaceRoot $Path }
    $resolved = [IO.Path]::GetFullPath($candidate)
    if (-not [string]::Equals($resolved, $workspaceRoot, [StringComparison]::Ordinal) -and
        -not $resolved.StartsWith($workspacePrefix, [StringComparison]::Ordinal)) {
        throw "Pipeline config must remain inside the caller workspace: $Path"
    }
    return $resolved
}

function Get-ResolvedPipelineSteps {
    param(
        [Parameter(Mandatory)][string] $PipelinePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]] $Visited
    )

    $resolvedPath = Resolve-WorkspacePath -Path $PipelinePath
    if (-not $Visited.Add($resolvedPath)) {
        return @()
    }
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Pipeline config not found: $resolvedPath"
    }

    $document = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json -AsHashtable
    $steps = @()
    if ($document.ContainsKey('extends') -and -not [string]::IsNullOrWhiteSpace([string]$document['extends'])) {
        $parentPath = Join-Path ([IO.Path]::GetDirectoryName($resolvedPath)) ([string]$document['extends'])
        $steps += @(Get-ResolvedPipelineSteps -PipelinePath $parentPath -Visited $Visited)
    }
    if ($document.ContainsKey('steps') -and $null -ne $document['steps']) {
        $steps += @($document['steps'])
    }
    return $steps
}

$pipelinePath = Resolve-WorkspacePath -Path $PipelineConfig
$visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$steps = @(Get-ResolvedPipelineSteps -PipelinePath $pipelinePath -Visited $visited)
$seoDoctorSteps = @($steps | Where-Object {
    $_ -is [Collections.IDictionary] -and
    $_.Contains('task') -and
    [string]::Equals([string]$_['task'], 'seo-doctor', [StringComparison]::OrdinalIgnoreCase)
})
if ($seoDoctorSteps.Count -eq 0) {
    throw 'Deploy guardrail requires a seo-doctor step in the resolved pipeline config chain.'
}

$gatingSteps = @($seoDoctorSteps | Where-Object {
    (($_.Contains('failOnWarnings')) -and ($_['failOnWarnings'] -eq $true)) -or
    (($_.Contains('failOnNewIssues')) -and ($_['failOnNewIssues'] -eq $true)) -or
    (($_.Contains('failOnNew')) -and ($_['failOnNew'] -eq $true))
})
if ($gatingSteps.Count -eq 0) {
    throw 'Deploy guardrail found seo-doctor, but no resolved seo-doctor step gates deployment.'
}

$completeSteps = @($gatingSteps | Where-Object {
    $_.Contains('checkContentLeaks') -and ($_['checkContentLeaks'] -eq $true) -and
    $_.Contains('requireCanonical') -and ($_['requireCanonical'] -eq $true) -and
    $_.Contains('requireHreflang') -and ($_['requireHreflang'] -eq $true) -and
    $_.Contains('requireHreflangXDefault') -and ($_['requireHreflangXDefault'] -eq $true)
})
if ($completeSteps.Count -eq 0) {
    throw 'Deploy guardrail requires a gating seo-doctor step with content-leak, canonical, hreflang, and x-default checks enabled.'
}

Write-Host "Validated $($seoDoctorSteps.Count) seo-doctor step(s); $($gatingSteps.Count) gate deployment; $($completeSteps.Count) enforce localized SEO/content-leak guardrails."

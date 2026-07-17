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

function Get-StepBooleanOption {
    param(
        [Parameter(Mandatory)][Collections.IDictionary] $Step,
        [Parameter(Mandatory)][string[]] $Names
    )

    foreach ($name in $Names) {
        if ($Step.Contains($name) -and ($Step[$name] -is [bool])) {
            return [pscustomobject]@{
                IsBoolean = $true
                Value = $Step[$name]
            }
        }
    }

    return [pscustomobject]@{
        IsBoolean = $false
        Value = $null
    }
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
    $contentLeaks = Get-StepBooleanOption -Step $_ -Names @('checkContentLeaks', 'check-content-leaks')
    $canonical = Get-StepBooleanOption -Step $_ -Names @('requireCanonical', 'require-canonical')
    $contentLeaks.IsBoolean -and ($contentLeaks.Value -eq $true) -and
    $canonical.IsBoolean -and ($canonical.Value -eq $true)
})
if ($completeSteps.Count -eq 0) {
    throw 'Deploy guardrail requires a gating seo-doctor step with content-leak and canonical checks enabled.'
}

$explicitModeSteps = @($completeSteps | ForEach-Object {
    $hreflang = Get-StepBooleanOption -Step $_ -Names @('requireHreflang', 'require-hreflang')
    $xDefault = Get-StepBooleanOption -Step $_ -Names @('requireHreflangXDefault', 'require-hreflang-x-default')
    if ($hreflang.IsBoolean -and $xDefault.IsBoolean -and ($hreflang.Value -eq $xDefault.Value)) {
        [pscustomobject]@{
            Localized = [bool]$hreflang.Value
            Step = $_
        }
    }
})
if ($explicitModeSteps.Count -eq 0) {
    throw 'Deploy guardrail requires an explicit SEO localization mode: set requireHreflang and requireHreflangXDefault to true for localized sites or false for single-language sites.'
}

$localizedSteps = @($explicitModeSteps | Where-Object { $_.Localized })
$singleLanguageSteps = @($explicitModeSteps | Where-Object { -not $_.Localized })
Write-Host "Validated $($seoDoctorSteps.Count) seo-doctor step(s); $($gatingSteps.Count) gate deployment; $($completeSteps.Count) enforce content-leak/canonical checks; localization modes: $($localizedSteps.Count) localized, $($singleLanguageSteps.Count) single-language."

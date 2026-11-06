function Invoke-PowerForgeReleaseMetadataPreparation {
    <#
    .SYNOPSIS
    Prepares exact project, manifest, and generated help versions without building release payloads.
    .DESCRIPTION
    Uses the project version updater and module Manifest gate with separate output paths.
    Existing signed package and module staging remains available for publication recovery.
    .PARAMETER ConfigPath
    Caller-owned unified release configuration whose directory anchors source paths.
    .PARAMETER EffectiveConfigurationPath
    Optional release configuration with exact versions already selected.
    .PARAMETER Version
    Exact version to write to the module and package projects.
    .EXAMPLE
    Invoke-PowerForgeReleaseMetadataPreparation -ConfigPath ./Build/release.json -Version 3.0.189
    .OUTPUTS
    A result containing the project version updates and module manifest result.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,
        [string] $EffectiveConfigurationPath,
        [Parameter(Mandatory)]
        [ValidatePattern('^\d+\.\d+\.\d+$')]
        [string] $Version
    )

    $configDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $ConfigPath).Path
    $inputPath = if ([string]::IsNullOrWhiteSpace($EffectiveConfigurationPath)) {
        $ConfigPath
    } else { $EffectiveConfigurationPath }
    $config = Get-Content -Raw -LiteralPath $inputPath | ConvertFrom-Json
    . (Join-Path $PSScriptRoot 'Set-PowerForgeAuthorizedReleaseVersion.ps1')
    $config = Set-PowerForgeAuthorizedReleaseVersion -ReleaseConfig $config -Version $Version

    $moduleRoot = if ([IO.Path]::IsPathRooted($config.Module.RepositoryRoot)) {
        [IO.Path]::GetFullPath($config.Module.RepositoryRoot)
    } else { [IO.Path]::GetFullPath((Join-Path $configDirectory $config.Module.RepositoryRoot)) }
    $moduleConfigPath = Join-Path $moduleRoot $config.Module.ConfigPath
    $moduleConfig = Get-Content -Raw -LiteralPath $moduleConfigPath | ConvertFrom-Json
    $documentation = @($moduleConfig.Segments | Where-Object { $_.Type -eq 'Documentation' })
    $readmePath = $null
    $readmeText = $null
    if ($documentation.Count -gt 0 -and $documentation[0].Configuration.PathReadme) {
        $readmePath = Join-Path (Join-Path $moduleRoot $moduleConfig.Build.SourcePath) $documentation[0].Configuration.PathReadme
        $readmeText = [IO.File]::ReadAllText($readmePath)
        $helpVersion = [regex]::new('(?m)^Help Version: [^\r\n]*')
        if ($helpVersion.Matches($readmeText).Count -ne 1) {
            throw "Generated module README must contain one Help Version field: $readmePath"
        }
        $readmeText = $helpVersion.Replace($readmeText, "Help Version: $Version")
    }

    . (Join-Path $PSScriptRoot 'New-PowerForgeReleaseEvidenceWorkspace.ps1')
    $outputRoot = Join-Path (New-PowerForgeReleaseEvidenceWorkspace -RepositoryRoot $moduleRoot) 'metadata'
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $packages = $config.Packages
    $packages.RootPath = if ([IO.Path]::IsPathRooted($packages.RootPath)) {
        [IO.Path]::GetFullPath($packages.RootPath)
    } else { [IO.Path]::GetFullPath((Join-Path $configDirectory $packages.RootPath)) }
    foreach ($setting in @{
            StagingPath = (Join-Path $outputRoot 'projects')
            OutputPath = (Join-Path $outputRoot 'packages')
            ReleaseZipOutputPath = (Join-Path $outputRoot 'releases')
            PlanOutputPath = (Join-Path $outputRoot 'project.build.plan.json')
            CleanStaging = $false
            CreateReleaseZip = $false
            SignAssemblies = $false
            SignPackages = $false
        }.GetEnumerator()) {
        $packages | Add-Member -NotePropertyName $setting.Key -NotePropertyValue $setting.Value -Force
    }
    $packageConfigPath = Join-Path $outputRoot 'project.build.json'
    $packages | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $packageConfigPath -Encoding UTF8
    $projectResult = Invoke-ProjectBuild -ConfigPath $packageConfigPath -UpdateVersions `
        -Plan:$false -Build:$false -StageNuget:$false -PublishNuget:$false -PublishGitHub:$false `
        -Quiet -ErrorAction Stop
    if (-not $projectResult.Success) {
        throw "Project version preparation failed: $($projectResult.ErrorMessage)"
    }
    $moduleResult = Invoke-ModuleBuild -ConfigPath $moduleConfigPath -RunMode Manifest `
        -ModuleVersion $Version -StagingPath (Join-Path $outputRoot 'module') `
        -NoSign -SkipInstall -Quiet -PassThru -ErrorAction Stop
    if ($null -ne $readmePath) {
        [IO.File]::WriteAllText($readmePath, $readmeText, [Text.UTF8Encoding]::new($false))
    }
    [pscustomobject]@{
        Success = $true
        Operation = 'PrepareMetadata'
        Version = $Version
        Packages = $projectResult
        Module = $moduleResult
        OutputRoot = $outputRoot
    }
}

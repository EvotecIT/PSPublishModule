# please notice I may be using PSM1 here (not always), as the module may not be built or PSD1 may be broken
# since PSD1 is not required for proper rebuilding, we use PSM1 for this module only
# most modules should be run via PSD1 or by it's name (which in the background uses PD1)
#
# Default path: use PowerForge CLI (staging-based) to avoid file locking when building PSPublishModule itself.
[CmdletBinding()]
param(
    [switch] $Legacy,
    [string] $Version,
    [ValidateSet('exact', 'autorevision')][string] $InstallStrategy = 'autorevision',
    [int] $Keep = 3,
    [string] $StagingPath,
    [switch] $SkipInstall,
    [switch] $KeepStaging
)

if (-not $Legacy) {
    $repoRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..')))).Path
    $moduleRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..')))).Path

    $moduleName = 'PSPublishModule'
    $resolvedVersion = $Version
    if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
        $manifestPath = [IO.Path]::Combine($moduleRoot, "$moduleName.psd1")
        if (Test-Path -LiteralPath $manifestPath) {
            try {
                $manifest = Import-PowerShellDataFile -Path $manifestPath
                $resolvedVersion = [string]$manifest.ModuleVersion
            } catch {
                Write-Warning "Failed to read ModuleVersion from manifest: $manifestPath. Provide -Version to override. Error: $($_.Exception.Message)"
                $resolvedVersion = $null
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
        Write-Warning "ModuleVersion not provided and could not be resolved from the manifest. Falling back to 1.0.0."
        $resolvedVersion = '1.0.0'
    }

    $cliProject = [IO.Path]::Combine($repoRoot, 'PowerForge.Cli', 'PowerForge.Cli.csproj')
    $csprojPath = [IO.Path]::Combine($repoRoot, 'PSPublishModule', 'PSPublishModule.csproj')
    if (-not (Test-Path -LiteralPath $cliProject)) { throw "PowerForge.Cli project not found: $cliProject" }
    if (-not (Test-Path -LiteralPath $csprojPath)) { throw "PSPublishModule project not found: $csprojPath" }

    if (-not $StagingPath) {
        $StagingPath = [IO.Path]::Combine($env:TEMP, 'PowerForge', 'build', "${moduleName}_$([Guid]::NewGuid().ToString('N'))")
    }
    $StagingPath = [IO.Path]::GetFullPath($StagingPath)

    $buildSpecPath = [IO.Path]::Combine($env:TEMP, 'PowerForge', 'build', "buildspec_${moduleName}_$([Guid]::NewGuid().ToString('N')).json")
    New-Item -Path ([IO.Path]::GetDirectoryName($buildSpecPath)) -ItemType Directory -Force | Out-Null

    $buildSpec = [ordered] @{
        Name              = $moduleName
        SourcePath        = $moduleRoot
        StagingPath       = $StagingPath
        CsprojPath        = $csprojPath
        Version           = $resolvedVersion
        Configuration     = 'Release'
        Frameworks        = @('net8.0', 'net472')
        ExcludeDirectories = @('.git', '.vs', '.vscode', 'bin', 'obj', 'packages', 'node_modules', 'Artefacts', 'Build', 'Docs', 'Documentation', 'Examples', 'Ignore', 'Tests')
        ExcludeFiles       = @('.gitignore', "$moduleName.Tests.ps1")
    }

    $buildSpec | ConvertTo-Json -Depth 4 | Set-Content -Path $buildSpecPath -Encoding UTF8

    Write-Host "[i] Building $moduleName $resolvedVersion via PowerForge CLI" -ForegroundColor Cyan
    Write-Host "[i] RepoRoot:  $repoRoot" -ForegroundColor DarkGray
    Write-Host "[i] Source:    $moduleRoot" -ForegroundColor DarkGray
    Write-Host "[i] Staging:   $StagingPath" -ForegroundColor DarkGray

    try {
        dotnet run --project $cliProject -c Release -- build --config $buildSpecPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        if (-not $SkipInstall) {
            dotnet run --project $cliProject -c Release -- install --name $moduleName --version $resolvedVersion --staging $StagingPath --strategy $InstallStrategy --keep $Keep
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }

        if (-not $KeepStaging) {
            try { Remove-Item -LiteralPath $StagingPath -Recurse -Force -ErrorAction Stop } catch { }
        }
    } finally {
        try { Remove-Item -LiteralPath $buildSpecPath -Force -ErrorAction SilentlyContinue } catch { }
    }

    return
}
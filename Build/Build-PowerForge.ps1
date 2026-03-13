[CmdletBinding()] param(
    [ValidateSet('PowerForge', 'PowerForgeWeb', 'All')]
    [string[]] $Tool = @('PowerForge'),
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64', 'osx-x64', 'osx-arm64')]
    [string[]] $Runtime = @('win-x64'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidateSet('net10.0', 'net8.0')]
    [string] $Framework = 'net10.0',
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string] $Flavor = 'SingleContained',
    [string] $OutDir,
    [switch] $ClearOut,
    [switch] $Zip,
    [switch] $UseStaging = $true,
    [switch] $KeepSymbols,
    [switch] $KeepDocs,
    [switch] $PublishGitHub,
    [string] $GitHubUsername = 'EvotecIT',
    [string] $GitHubRepositoryName = 'PSPublishModule',
    [string] $GitHubAccessToken,
    [string] $GitHubAccessTokenFilePath,
    [string] $GitHubAccessTokenEnvName = 'GITHUB_TOKEN',
    [string] $GitHubTagName,
    [string] $GitHubReleaseName,
    [switch] $GenerateReleaseNotes = $true,
    [switch] $IsPreRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($Text) { Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Step($Text) { Write-Host "-> $Text" -ForegroundColor Yellow }
function Write-Ok($Text) { Write-Host "[ok] $Text" -ForegroundColor Green }

$repoRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..')))).Path
$moduleManifest = Join-Path $repoRoot 'PSPublishModule\PSPublishModule.psd1'
$outDirProvided = $PSBoundParameters.ContainsKey('OutDir') -and -not [string]::IsNullOrWhiteSpace($OutDir)

if ($PublishGitHub) {
    $Zip = $true
}

$toolDefinitions = @{
    PowerForge = @{
        ProjectPath = Join-Path $repoRoot 'PowerForge.Cli\PowerForge.Cli.csproj'
        ArtifactRoot = Join-Path $repoRoot 'Artifacts\PowerForge'
        OutputName = 'PowerForge'
        OutputNameLower = 'powerforge'
        PublishedBinaryCandidates = @('PowerForge.Cli.exe', 'PowerForge.Cli')
    }
    PowerForgeWeb = @{
        ProjectPath = Join-Path $repoRoot 'PowerForge.Web.Cli\PowerForge.Web.Cli.csproj'
        ArtifactRoot = Join-Path $repoRoot 'Artifacts\PowerForgeWeb'
        OutputName = 'PowerForgeWeb'
        OutputNameLower = 'powerforge-web'
        PublishedBinaryCandidates = @('PowerForge.Web.Cli.exe', 'PowerForge.Web.Cli')
    }
}

function Resolve-ToolSelection {
    param([string[]] $SelectedTools)

    $normalized = @(
        @($SelectedTools) |
            Where-Object { $_ -and $_.Trim() } |
            ForEach-Object { $_.Trim() } |
            Select-Object -Unique
    )

    if ($normalized.Count -eq 0) {
        throw 'Tool selection cannot be empty.'
    }

    if ($normalized -contains 'All') {
        return @('PowerForge', 'PowerForgeWeb')
    }

    return $normalized
}

function Resolve-ProjectVersion {
    param([Parameter(Mandatory)][string] $ProjectPath)

    [xml] $xml = Get-Content -LiteralPath $ProjectPath -Raw
    $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='Version']")
    if (-not $node) {
        $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='VersionPrefix']")
    }

    if (-not $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Unable to resolve Version/VersionPrefix from $ProjectPath"
    }

    return $node.InnerText.Trim()
}

function Resolve-OutDir {
    param(
        [Parameter(Mandatory)][string] $ToolName,
        [Parameter(Mandatory)][string] $Rid,
        [Parameter(Mandatory)][string] $DefaultRoot,
        [Parameter(Mandatory)][bool] $MultiTool,
        [Parameter(Mandatory)][bool] $MultiRuntime
    )

    if ($outDirProvided) {
        $root = $OutDir
        if ($MultiTool) {
            $root = Join-Path $root $ToolName
        }
        if ($MultiRuntime) {
            return Join-Path $root ("{0}/{1}/{2}" -f $Rid, $Framework, $Flavor)
        }
        return $root
    }

    return Join-Path $DefaultRoot ("{0}/{1}/{2}" -f $Rid, $Framework, $Flavor)
}

function Resolve-ToolOutputRoot {
    param(
        [Parameter(Mandatory)][string] $ToolName,
        [Parameter(Mandatory)][string] $DefaultRoot,
        [Parameter(Mandatory)][bool] $MultiTool
    )

    if ($outDirProvided) {
        if ($MultiTool) {
            return Join-Path $OutDir $ToolName
        }

        return $OutDir
    }

    return $DefaultRoot
}

function Remove-DirectoryContents {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Get-ChildItem -Path $Path -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

function Resolve-GitHubToken {
    if (-not $PublishGitHub) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubAccessToken)) {
        return $GitHubAccessToken.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubAccessTokenFilePath)) {
        $tokenPath = if ([IO.Path]::IsPathRooted($GitHubAccessTokenFilePath)) {
            $GitHubAccessTokenFilePath
        } else {
            Join-Path $repoRoot $GitHubAccessTokenFilePath
        }

        if (Test-Path -LiteralPath $tokenPath) {
            return (Get-Content -LiteralPath $tokenPath -Raw).Trim()
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubAccessTokenEnvName)) {
        $envToken = [Environment]::GetEnvironmentVariable($GitHubAccessTokenEnvName)
        if (-not [string]::IsNullOrWhiteSpace($envToken)) {
            return $envToken.Trim()
        }
    }

    throw 'GitHub token is required when -PublishGitHub is used.'
}

function Publish-GitHubAssets {
    param(
        [Parameter(Mandatory)][string] $ToolName,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string[]] $AssetPaths,
        [Parameter(Mandatory)][string] $Token
    )

    if ($AssetPaths.Count -eq 0) {
        throw "No assets were created for $ToolName, so there is nothing to publish."
    }

    Import-Module $moduleManifest -Force -ErrorAction Stop

    $tagName = if (-not [string]::IsNullOrWhiteSpace($GitHubTagName) -and $selectedTools.Count -eq 1) {
        $GitHubTagName.Trim()
    } else {
        "$ToolName-v$Version"
    }

    $releaseName = if (-not [string]::IsNullOrWhiteSpace($GitHubReleaseName) -and $selectedTools.Count -eq 1) {
        $GitHubReleaseName.Trim()
    } else {
        "$ToolName $Version"
    }

    Write-Step "Publishing $ToolName assets to GitHub release $tagName"
    $publishResult = Send-GitHubRelease `
        -GitHubUsername $GitHubUsername `
        -GitHubRepositoryName $GitHubRepositoryName `
        -GitHubAccessToken $Token `
        -TagName $tagName `
        -ReleaseName $releaseName `
        -GenerateReleaseNotes:$GenerateReleaseNotes `
        -IsPreRelease:$IsPreRelease `
        -AssetFilePaths $AssetPaths

    if (-not $publishResult.Succeeded) {
        throw "GitHub release publish failed for ${ToolName}: $($publishResult.ErrorMessage)"
    }

    Write-Ok "$ToolName release published -> $($publishResult.ReleaseUrl)"
}

$selectedTools = @(Resolve-ToolSelection -SelectedTools $Tool)
$multiTool = $selectedTools.Count -gt 1
$rids = @(
    @($Runtime) |
        Where-Object { $_ -and $_.Trim() } |
        ForEach-Object { $_.Trim() } |
        Select-Object -Unique
)
if ($rids.Count -eq 0) {
    throw 'Runtime must not be empty.'
}
$multiRuntime = $rids.Count -gt 1
$singleFile = $Flavor -in @('SingleContained', 'SingleFx')
$selfContained = $Flavor -in @('SingleContained', 'Portable')
$compress = $singleFile
$selfExtract = $Flavor -eq 'SingleContained'
$gitHubToken = Resolve-GitHubToken
$publishedAssets = @{}

Write-Header "Build tools ($Flavor)"
Write-Step "Framework -> $Framework"
Write-Step "Configuration -> $Configuration"
Write-Step ("Tools -> {0}" -f ($selectedTools -join ', '))
Write-Step ("Runtimes -> {0}" -f ($rids -join ', '))

foreach ($toolName in $selectedTools) {
    $definition = $toolDefinitions[$toolName]
    if (-not $definition) {
        throw "Unsupported tool: $toolName"
    }

    $projectPath = [string] $definition.ProjectPath
    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "Project not found: $projectPath"
    }

    $artifactRoot = [string] $definition.ArtifactRoot
    $toolOutputRoot = Resolve-ToolOutputRoot -ToolName $toolName -DefaultRoot $artifactRoot -MultiTool:$multiTool
    $outputName = [string] $definition.OutputName
    $lowerAlias = [string] $definition.OutputNameLower
    $candidateNames = @($definition.PublishedBinaryCandidates)
    $version = Resolve-ProjectVersion -ProjectPath $projectPath
    $publishedAssets[$toolName] = @()

    foreach ($rid in $rids) {
        $outDirThis = Resolve-OutDir -ToolName $toolName -Rid $rid -DefaultRoot $artifactRoot -MultiTool:$multiTool -MultiRuntime:$multiRuntime
        New-Item -ItemType Directory -Force -Path $outDirThis | Out-Null

        $publishDir = $outDirThis
        $stagingDir = $null
        if ($UseStaging) {
            $stagingDir = Join-Path $env:TEMP ($outputName + '.publish.' + [guid]::NewGuid().ToString('N'))
            $publishDir = $stagingDir
            Write-Step "Using staging publish dir -> $publishDir"
            if (Test-Path -LiteralPath $publishDir) {
                Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
        }

        Write-Step "$toolName runtime -> $rid"
        Write-Step "Publishing -> $publishDir"

        $publishArgs = @(
            'publish', $projectPath,
            '-c', $Configuration,
            '-f', $Framework,
            '-r', $rid,
            "--self-contained:$selfContained",
            "/p:PublishSingleFile=$singleFile",
            "/p:PublishReadyToRun=false",
            "/p:PublishTrimmed=false",
            "/p:IncludeAllContentForSelfExtract=$selfExtract",
            "/p:IncludeNativeLibrariesForSelfExtract=$selfExtract",
            "/p:EnableCompressionInSingleFile=$compress",
            "/p:EnableSingleFileAnalyzer=false",
            "/p:DebugType=None",
            "/p:DebugSymbols=false",
            "/p:GenerateDocumentationFile=false",
            "/p:CopyDocumentationFiles=false",
            "/p:ExcludeSymbolsFromSingleFile=true",
            "/p:ErrorOnDuplicatePublishOutputFiles=false",
            "/p:UseAppHost=true",
            "/p:PublishDir=$publishDir"
        )

        if ($ClearOut -and (Test-Path -LiteralPath $outDirThis) -and ($publishDir -eq $outDirThis)) {
            Write-Step "Clearing $outDirThis"
            Remove-DirectoryContents -Path $outDirThis
        }

        dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed for $toolName ($LASTEXITCODE)"
        }

        if (-not $KeepSymbols) {
            Write-Step "Removing symbols (*.pdb)"
            Get-ChildItem -Path $publishDir -Filter *.pdb -File -Recurse -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }

        if (-not $KeepDocs) {
            Write-Step "Removing docs (*.xml, *.pdf)"
            Get-ChildItem -Path $publishDir -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -in @('.xml', '.pdf') } |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }

        $ridIsWindows = $rid -like 'win-*'
        $friendlyBinaryName = if ($ridIsWindows) { $outputName + '.exe' } else { $outputName }
        $friendlyBinary = Join-Path $publishDir $friendlyBinaryName
        foreach ($candidateName in $candidateNames) {
            $candidatePath = Join-Path $publishDir $candidateName
            if (-not (Test-Path -LiteralPath $candidatePath)) {
                continue
            }

            if (Test-Path -LiteralPath $friendlyBinary) {
                Remove-Item -LiteralPath $friendlyBinary -Force -ErrorAction SilentlyContinue
            }

            Move-Item -LiteralPath $candidatePath -Destination $friendlyBinary -Force
            break
        }

        if (-not (Test-Path -LiteralPath $friendlyBinary)) {
            throw "Friendly output binary was not created for $toolName ($rid): $friendlyBinary"
        }

        if ($ClearOut -and (Test-Path -LiteralPath $outDirThis) -and ($publishDir -ne $outDirThis)) {
            Write-Step "Clearing $outDirThis"
            Remove-DirectoryContents -Path $outDirThis
        }

        if ($publishDir -ne $outDirThis) {
            Write-Step "Copying publish output -> $outDirThis"
            Copy-Item -Path (Join-Path $publishDir '*') -Destination $outDirThis -Recurse -Force
        }

        if ($Zip) {
            $zipName = "{0}-{1}-{2}-{3}-{4}.zip" -f $outputName, $version, $Framework, $rid, $Flavor
            $zipPath = Join-Path (Split-Path -Parent $outDirThis) $zipName
            if (Test-Path -LiteralPath $zipPath) {
                Remove-Item -LiteralPath $zipPath -Force
            }
            Write-Step "Create zip -> $zipPath"
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::CreateFromDirectory($outDirThis, $zipPath)
            $publishedAssets[$toolName] += $zipPath
        }

        if ($rid -notlike 'win-*') {
            $lowerAliasPath = Join-Path $outDirThis $lowerAlias
            $friendlyPublishedBinary = Join-Path $outDirThis $outputName
            if ((Test-Path -LiteralPath $friendlyPublishedBinary) -and -not (Test-Path -LiteralPath $lowerAliasPath)) {
                Copy-Item -LiteralPath $friendlyPublishedBinary -Destination $lowerAliasPath -Force
            }
        }

        if ($stagingDir -and (Test-Path -LiteralPath $stagingDir)) {
            Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-Path -LiteralPath $toolOutputRoot)) {
        New-Item -ItemType Directory -Force -Path $toolOutputRoot | Out-Null
    }

    $manifestPath = Join-Path $toolOutputRoot 'release-manifest.json'
    $manifest = [ordered]@{
        Tool = $toolName
        Version = $version
        Framework = $Framework
        Flavor = $Flavor
        Runtimes = $rids
        Assets = @($publishedAssets[$toolName])
    } | ConvertTo-Json -Depth 5
    Set-Content -LiteralPath $manifestPath -Value $manifest
    Write-Ok "$toolName artifacts -> $toolOutputRoot"

    if ($PublishGitHub) {
        Publish-GitHubAssets -ToolName $toolName -Version $version -AssetPaths @($publishedAssets[$toolName]) -Token $gitHubToken
    }
}

if ($multiTool) {
    $root = if ($outDirProvided) { $OutDir } else { Join-Path $repoRoot 'Artifacts' }
    Write-Ok "Built tool artifacts -> $root"
}

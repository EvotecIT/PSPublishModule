[CmdletBinding()]
param(
    [string] $CredentialProviderVersion = '2.0.2',
    [switch] $SkipDownload,
    [switch] $JsonOnly,
    [string] $JsonPath = (Join-Path $PSScriptRoot '../powerforge.artefacts.json'),
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [string] $ModuleVersion = '1.0.X',
    [string] $PreReleaseTag,
    [switch] $EnablePowerShellGalleryPublish,
    [string] $PowerShellGalleryApiKeyPath
)

$moduleRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulesRoot = [System.IO.Path]::GetFullPath((Join-Path $moduleRoot '..'))
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $moduleRoot '../..'))
$artefactRoot = Join-Path $moduleRoot 'Artefacts/AzureArtifactsCredentialProvider'
$packedRoot = Join-Path $moduleRoot 'Artefacts/Packed'
$csproj = Join-Path $repoRoot 'PSPublishModule/PSPublishModule.csproj'
$runtimesText = (dotnet --list-runtimes 2>$null) -join "`n"
$tfm = if ($runtimesText -match '(?m)^Microsoft\.NETCore\.App\s+10\.') { 'net10.0' } else { 'net8.0' }
$binaryModule = Join-Path $repoRoot ("PSPublishModule/bin/{0}/{1}/PSPublishModule.dll" -f $Configuration, $tfm)

function Get-FileSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Save-CredentialProviderPackage {
    param(
        [Parameter(Mandatory)]
        [string] $Runtime,
        [Parameter(Mandatory)]
        [string] $FileName
    )

    $target = Join-Path $artefactRoot $FileName
    if ($SkipDownload -and (Test-Path -LiteralPath $target -PathType Leaf)) {
        return $target
    }

    if ($SkipDownload) {
        throw "Credential provider package '$target' is missing and -SkipDownload was specified."
    }

    $uri = "https://github.com/microsoft/artifacts-credprovider/releases/download/v$CredentialProviderVersion/$FileName"
    Write-Host "Downloading $Runtime Azure Artifacts Credential Provider $CredentialProviderVersion from $uri"
    Invoke-WebRequest -Uri $uri -OutFile $target -UseBasicParsing
    $target
}

function Get-NetCorePackageArchitecture {
    param(
        [Parameter(Mandatory)]
        [string] $FileName
    )

    if ($FileName -like '*win-arm64*') { return 'arm64' }
    if ($FileName -like '*win-x86*') { return 'x86' }
    if ($FileName -like '*win-x64*') { return 'x64' }
    return $null
}

function Write-CredentialProviderManifest {
    param(
        [Parameter(Mandatory)]
        [string[]] $NetCorePackagePath,
        [Parameter(Mandatory)]
        [string] $NetFxPackagePath
    )

    $files = foreach ($path in $NetCorePackagePath) {
        $fileName = [System.IO.Path]::GetFileName($path)
        $entry = [ordered] @{
            runtime = 'netcore'
            path    = $fileName
            sha256  = Get-FileSha256 -Path $path
        }
        $architecture = Get-NetCorePackageArchitecture -FileName $fileName
        if (-not [string]::IsNullOrWhiteSpace($architecture)) {
            $entry.architecture = $architecture
        }
        $entry
    }

    $files += [ordered] @{
        runtime = 'netfx'
        path    = [System.IO.Path]::GetFileName($NetFxPackagePath)
        sha256  = Get-FileSha256 -Path $NetFxPackagePath
    }

    $manifest = [ordered] @{
        name    = 'AzureArtifactsCredentialProvider'
        version = $CredentialProviderVersion
        source  = "https://github.com/microsoft/artifacts-credprovider/releases/tag/v$CredentialProviderVersion"
        license = 'MIT'
        files   = @($files)
    }

    $manifest |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $artefactRoot 'manifest.json') -Encoding UTF8
}

New-Item -ItemType Directory -Path $artefactRoot -Force | Out-Null

if (-not $JsonOnly) {
    $netCorePackages = @(
        Save-CredentialProviderPackage -Runtime 'netcore-x64' -FileName 'Microsoft.win-x64.NuGet.CredentialProvider.zip'
        Save-CredentialProviderPackage -Runtime 'netcore-x86' -FileName 'Microsoft.win-x86.NuGet.CredentialProvider.zip'
        Save-CredentialProviderPackage -Runtime 'netcore-arm64' -FileName 'Microsoft.win-arm64.NuGet.CredentialProvider.zip'
    )
    $netFxPackage = Save-CredentialProviderPackage -Runtime 'netfx' -FileName 'Microsoft.NetFx48.NuGet.CredentialProvider.zip'
    Write-CredentialProviderManifest -NetCorePackagePath $netCorePackages -NetFxPackagePath $netFxPackage
}

if (-not (Test-Path -LiteralPath $binaryModule -PathType Leaf)) {
    if (-not (Test-Path -LiteralPath $csproj -PathType Leaf)) {
        throw "PSPublishModule project was not found at '$csproj'."
    }

    Write-Host "Building PSPublishModule ($Configuration)"
    $buildOutput = & dotnet build $csproj -c $Configuration --nologo --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Out-Host
        throw "dotnet build failed (exit $LASTEXITCODE)."
    }
}

Get-Module -Name 'PSPublishModule' -All -ErrorAction SilentlyContinue | Remove-Module -Force -ErrorAction SilentlyContinue
Import-Module $binaryModule -Force

$invokeModuleBuildCommand = Get-Command Invoke-ModuleBuild -ErrorAction SilentlyContinue
if (-not $invokeModuleBuildCommand -or $invokeModuleBuildCommand.Source -ne 'PSPublishModule') {
    throw 'Invoke-ModuleBuild did not load from the local PSPublishModule build.'
}

$buildParams = @{
    ModuleName         = 'PSPublishModule.Artefacts'
    Path               = $modulesRoot
    ExitCode           = $true
    ExcludeDirectories = @('.git', '.vs', '.vscode', 'bin', 'obj', 'packages', 'node_modules', 'Build', 'Docs', 'Documentation', 'Examples', 'Ignore', 'Publish', 'Tests')
}
if ($JsonOnly) {
    $buildParams.JsonOnly = $true
    $buildParams.JsonPath = $JsonPath
}

Invoke-ModuleBuild @buildParams -Settings {
    $manifest = [ordered] @{
        ModuleVersion        = $ModuleVersion
        CompatiblePSEditions = @('Desktop', 'Core')
        GUID                 = '4f6f72bb-f1ab-4eb9-95a2-38c349e4ac0f'
        Author               = 'Przemyslaw Klys'
        CompanyName          = 'Evotec'
        Copyright            = "(c) 2026 - $((Get-Date).Year) Przemyslaw Klys @ Evotec. All rights reserved."
        Description          = 'Offline artefact carrier for PSPublishModule-managed workstation prerequisites.'
        PowerShellVersion    = '5.1'
        Tags                 = @('PSPublishModule', 'PowerForge', 'AzureArtifacts', 'CredentialProvider', 'Offline')
        ProjectUri           = 'https://github.com/EvotecIT/PSPublishModule'
        LicenseUri           = 'https://github.com/EvotecIT/PSPublishModule/blob/main/LICENSE'
    }
    if (-not [string]::IsNullOrWhiteSpace($PreReleaseTag)) {
        $manifest.PreReleaseTag = $PreReleaseTag
    }
    New-ConfigurationManifest @manifest

    New-ConfigurationBuild -Enable -MergeModuleOnBuild -InstallMissingModules:$false
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'DefaultPSM1', 'OnMergePSD1', 'OnMergePSM1' -EnableFormatting -Sort None
    New-ConfigurationArtefact -Type Packed -Enable -Path $packedRoot -IncludeTagName -ArtefactName 'PSPublishModule.Artefacts.<TagModuleVersionWithPreRelease>.zip'

    if ($EnablePowerShellGalleryPublish) {
        if ([string]::IsNullOrWhiteSpace($PowerShellGalleryApiKeyPath)) {
            throw '-PowerShellGalleryApiKeyPath is required when -EnablePowerShellGalleryPublish is used.'
        }

        New-ConfigurationPublish -Type PowerShellGallery -FilePath $PowerShellGalleryApiKeyPath -Enabled
    }
}

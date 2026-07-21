# This version is for local building
# We need to remove library before we start, as it may contain old files, which will be in use once PSD1 loads
# This is only required for PSPublisModule, as it's the only module that is being built by itself

[CmdletBinding()] param(
    [switch] $JsonOnly,
    [string] $JsonPath,
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto',
    [switch] $NoDotnetBuild,
    [string] $ModuleVersion = '3.0.X',
    [string] $PreReleaseTag,
    [switch] $SignModule,
    [switch] $NoSign,
    [string] $CertificateThumbprint = '92e95fb58effa6a4a75e77a33cdd6bfe6dd30f1a',
    [switch] $SignIncludeBinaries,
    [switch] $SignIncludeInternals,
    [switch] $SignIncludeExe,
    [switch] $NoInteractive,
    [switch] $NoExitCode,
    [Alias('ConfigurationGateMode')]
    [ValidateSet('Manifest', 'Documentation', 'Build', 'Publish')]
    [string] $RunMode = 'Build',
    [switch] $PowerForgeReleaseStage,
    [switch] $PowerForgeUnifiedGitHubRelease,
    [bool] $IncludeProjectPackages = $true,
    [string] $DiagnosticsBaselinePath,
    [switch] $GenerateDiagnosticsBaseline,
    [switch] $UpdateDiagnosticsBaseline,
    [switch] $FailOnNewDiagnostics,
    [ValidateSet('Warning', 'Error')]
    [string] $FailOnDiagnosticsSeverity
)

if ([string]::IsNullOrWhiteSpace($JsonPath)) {
    # Windows PowerShell 5.1 does not populate $PSScriptRoot while parameter
    # default expressions are evaluated. Resolve script-relative defaults only
    # after binding so hosted release builds work in both Windows PowerShell and pwsh.
    $JsonPath = Join-Path $PSScriptRoot '../../powerforge.json'
}

if ($RunMode -eq 'Publish' -and -not $JsonOnly -and -not $PowerForgeReleaseStage) {
    $releaseEntryPoint = Join-Path $PSScriptRoot '../../Build/Build-Module.ps1'
    if (-not (Test-Path -LiteralPath $releaseEntryPoint)) {
        throw "Unified release entry point not found: $releaseEntryPoint"
    }

    $releaseArguments = @{
        RunMode       = 'Publish'
        Framework     = $Framework
        Configuration = $Configuration
    }
    if ($NoDotnetBuild) { $releaseArguments.NoBuild = $true }
    foreach ($parameterName in @(
            'ModuleVersion',
            'PreReleaseTag',
            'SignModule',
            'NoSign',
            'CertificateThumbprint',
            'SignIncludeBinaries',
            'SignIncludeInternals',
            'SignIncludeExe',
            'DiagnosticsBaselinePath',
            'GenerateDiagnosticsBaseline',
            'UpdateDiagnosticsBaseline',
            'FailOnNewDiagnostics',
            'FailOnDiagnosticsSeverity'
        )) {
        if ($PSBoundParameters.ContainsKey($parameterName)) {
            $releaseArguments[$parameterName] = $PSBoundParameters[$parameterName]
        }
    }

    & $releaseEntryPoint @releaseArguments
    exit $LASTEXITCODE
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$moduleRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artefactsRoot = Join-Path $moduleRoot 'Artefacts'
$csproj = Join-Path -Path $repoRoot -ChildPath 'PSPublishModule/PSPublishModule.csproj'
$sourceManifest = Join-Path -Path $moduleRoot -ChildPath 'PSPublishModule.psd1'
$sourceLibRoot = Join-Path -Path $moduleRoot -ChildPath 'Lib'

function Resolve-ImportFramework {
    param([string] $RequestedFramework)

    if ($RequestedFramework -ne 'auto') {
        return $RequestedFramework
    }

    # Choose the binary that this PowerShell host can load, not merely the
    # newest .NET runtime installed for dotnet.exe.
    if ($PSVersionTable.PSEdition -eq 'Core' -and [Environment]::Version.Major -ge 10) {
        return 'net10.0'
    }

    'net8.0'
}

$tfm = Resolve-ImportFramework -RequestedFramework $Framework
$binaryModule = Join-Path -Path $repoRoot -ChildPath ("PSPublishModule/bin/{0}/{1}/PSPublishModule.dll" -f $Configuration, $tfm)

function Invoke-LocalPSPublishModuleBuild {
    if (-not (Test-Path -LiteralPath $csproj)) {
        return
    }

    $i = [char]0x2139 # ℹ
    Write-Host "$i Building PSPublishModule ($Configuration)" -ForegroundColor DarkGray
    $buildOutput = & dotnet build $csproj -c $Configuration --nologo --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Out-Host
        throw "dotnet build failed (exit $LASTEXITCODE)."
    }
}

if (-not $JsonOnly -and -not $NoDotnetBuild) {
    Invoke-LocalPSPublishModuleBuild
}

# Always reload PSPublishModule from this repo for self-builds. Otherwise an older
# installed/imported module in the caller session can shadow the current source changes.
Get-Module -Name 'PSPublishModule' -All -ErrorAction SilentlyContinue | Remove-Module -Force -ErrorAction SilentlyContinue

# Clean the repo Lib payload only after unloading the module; otherwise Windows can keep
# stale PSPublishModule binaries locked and the delete silently fails.
if (-not $JsonOnly) {
    Remove-Item -Path (Join-Path $PSScriptRoot '../Lib') -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $binaryModule) -and -not $NoDotnetBuild) {
    Invoke-LocalPSPublishModuleBuild
}

if (Test-Path -LiteralPath $binaryModule) {
    $importPath = $binaryModule
} elseif (Test-Path -LiteralPath $sourceLibRoot) {
    Write-Warning "Falling back to source manifest import because the compiled PSPublishModule binary was not found: $binaryModule"
    $importPath = $sourceManifest
}

if (-not $importPath) {
    throw "Invoke-ModuleBuild is not available. Ensure PSPublishModule.dll built and importable."
}

Write-Verbose "Importing PSPublishModule from '$importPath'."
Import-Module $importPath -Force

$invokeModuleBuildCommand = Get-Command Invoke-ModuleBuild -ErrorAction SilentlyContinue
if (-not $invokeModuleBuildCommand -or $invokeModuleBuildCommand.Source -ne 'PSPublishModule') {
    throw "Invoke-ModuleBuild did not load from the local PSPublishModule build."
}

$buildParams = @{
    ModuleName = 'PSPublishModule'
}
if (-not $NoExitCode.IsPresent) { $buildParams.ExitCode = $true }
if ($JsonOnly) {
    $buildParams.JsonOnly = $true
    $buildParams.JsonPath = $JsonPath
}
if ($NoInteractive.IsPresent) { $buildParams.NoInteractive = $true }
if ($PSBoundParameters.ContainsKey('DiagnosticsBaselinePath')) { $buildParams.DiagnosticsBaselinePath = $DiagnosticsBaselinePath }
if ($PSBoundParameters.ContainsKey('GenerateDiagnosticsBaseline')) { $buildParams.GenerateDiagnosticsBaseline = $GenerateDiagnosticsBaseline.IsPresent }
if ($PSBoundParameters.ContainsKey('UpdateDiagnosticsBaseline')) { $buildParams.UpdateDiagnosticsBaseline = $UpdateDiagnosticsBaseline.IsPresent }
if ($PSBoundParameters.ContainsKey('FailOnNewDiagnostics')) { $buildParams.FailOnNewDiagnostics = $FailOnNewDiagnostics.IsPresent }
if ($PSBoundParameters.ContainsKey('FailOnDiagnosticsSeverity')) { $buildParams.FailOnDiagnosticsSeverity = $FailOnDiagnosticsSeverity }

Invoke-ModuleBuild @buildParams -Settings {
    # Usual defaults as per standard module
    $Manifest = [ordered] @{
        ModuleVersion          = $ModuleVersion
        CompatiblePSEditions   = @('Desktop', 'Core')
        GUID                   = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
        Author                 = 'Przemyslaw Klys'
        CompanyName            = 'Evotec'
        Copyright              = "(c) 2011 - $((Get-Date).Year) Przemyslaw Klys @ Evotec. All rights reserved."
        Description            = 'Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery'
        PowerShellVersion      = '5.1'
        Prerelease             = $PreReleaseTag
        Tags                   = @('Windows', 'MacOS', 'Linux', 'Build', 'Module')
        IconUri                = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
        ProjectUri             = 'https://github.com/EvotecIT/PSPublishModule'
        DotNetFrameworkVersion = '4.5.2'
    }
    New-ConfigurationManifest @Manifest

    # Keep feature-specific tooling out of the module manifest RequiredModules list.
    # Test workflows install/use Pester on demand, and repository/download workflows
    # probe PSResourceGet first with PowerShellGet fallback when those features run.

    # Do not add inbox Microsoft.PowerShell.* modules as Required/External dependencies.
    # They are part of the runtime and publishing them as gallery dependencies breaks
    # Save-Module / Install-Module resolution for consumers.

    # Add approved modules, that can be used as a dependency, but only when specific function from those modules is used
    # And on that time only that function and dependant functions will be copied over
    # Keep in mind it has it's limits when "copying" functions such as it should not depend on DLLs or other external files
    New-ConfigurationModule -Type ApprovedModule -Name 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'

    New-ConfigurationModuleSkip -IgnoreModuleName 'PKI', 'OpenAuthenticode' -IgnoreFunctionName @(
        # ignore functions from OpenAuthenticode module when used during linux/macos build
        'Set-OpenAuthenticodeSignature'
        'Get-OpenAuthenticodeSignature'
        # ignore functions from Microsoft.PowerShell.Security, as those are not on linux/macos
        'Get-AuthenticodeSignature'
        'Set-AuthenticodeSignature'
        # ignore functions from PKI module when used during linux/macos build
        #'Import-PfxCertificate'
        # seems to be windows only
        'New-FileCatalog'
    )

    $signEnabled = if ($NoSign.IsPresent) { $false } elseif ($SignModule.IsPresent) { $true } else { $Env:COMPUTERNAME -eq 'EVOMAGIC' }
    $newConfigurationProfileSplat = @{
        Profile                       = 'Binary'
        SignModule                    = $signEnabled
        CertificateThumbprint         = $CertificateThumbprint
        SkipBuiltinReplacements       = $true
        DotSourceLibraries            = $true
        DotSourceClasses              = $true
        NETProjectPath                = (Join-Path $repoRoot 'PSPublishModule')
        NETProjectName                = 'PSPublishModule'
        NETConfiguration              = $Configuration
        NETFramework                  = 'net8.0', 'net472'
        NETHandleAssemblyWithSameName = $true
        NETAssemblyLoadContext        = $true
        ResolveBinaryConflicts        = $true
        ResolveBinaryConflictsName    = 'PSPublishModule'
        NETAssemblyTypeAccelerators   = @(
            'PowerForge.ModuleTestFailureAnalysis',
            'PowerForge.ModuleTestSuiteResult',
            'PowerForge.ModuleRepositoryProfileScope',
            'PowerForge.PrivateGalleryBootstrapMode',
            'PowerForge.PrivateGalleryCredentialSource',
            'PowerForge.PublishTool',
            'PowerForge.RepositoryRegistrationTool'
        )
        KillLockersBeforeInstall      = $true
        KillLockersForce              = $true
    }

    if ($PSBoundParameters.ContainsKey('SignIncludeBinaries')) {
        $newConfigurationProfileSplat.SignIncludeBinaries = $SignIncludeBinaries.IsPresent
    }
    if ($PSBoundParameters.ContainsKey('SignIncludeInternals')) {
        $newConfigurationProfileSplat.SignIncludeInternals = $SignIncludeInternals.IsPresent
    }
    if ($PSBoundParameters.ContainsKey('SignIncludeExe')) {
        $newConfigurationProfileSplat.SignIncludeExe = $SignIncludeExe.IsPresent
    }

    New-ConfigurationModuleBuildProfile @newConfigurationProfileSplat

    New-ConfigurationArtefact -Type Unpacked -Enable -Path (Join-Path $artefactsRoot 'Unpacked/<TagModuleVersionWithPreRelease>') -RequiredModulesPath (Join-Path $artefactsRoot 'Unpacked/<TagModuleVersionWithPreRelease>/Modules') -AddRequiredModules -CopyFiles @{
        "Examples\Step01.CreateModuleProject.ps1"     = "Examples\Step01.CreateModuleProject.ps1"
        "Examples\Step02.BuildModuleOver.ps1"         = "Examples\Step02.BuildModuleOver.ps1"
        "Examples\Example.ModuleLifecycleActions.ps1" = "Examples\Example.ModuleLifecycleActions.ps1"
    } -CopyFilesRelative

    New-ConfigurationArtefact -Type Packed -Enable -Path (Join-Path $artefactsRoot 'PackedWithModules') -IncludeTagName -ID 'ToGitHub' -AddRequiredModules -CopyFiles @{
        "Examples\Step01.CreateModuleProject.ps1"     = "Examples\Step01.CreateModuleProject.ps1"
        "Examples\Step02.BuildModuleOver.ps1"         = "Examples\Step02.BuildModuleOver.ps1"
        "Examples\Example.ModuleLifecycleActions.ps1" = "Examples\Example.ModuleLifecycleActions.ps1"
    } -CopyFilesRelative -ArtefactName "PSPublishModule.<TagModuleVersionWithPreRelease>-FullPackage.zip"

    New-ConfigurationArtefact -Type Packed -Enable -Path (Join-Path $artefactsRoot 'Packed') -IncludeTagName -ID 'ToGitHub' -ArtefactName "PSPublishModule.<TagModuleVersionWithPreRelease>.zip"

    if ($RunMode -in @('Build', 'Publish')) {
        if ($IncludeProjectPackages) {
            New-ConfigurationProjectBuild -Name 'PowerForge' -ConfigPath '../Build/release.json' -BuildBeforeModule -PublishNuget
            New-ConfigurationRelease -StageRoot 'Module/Artefacts/UploadReady' -VersionSource Module -BuildOrder 'Packages', 'Module' -PublishOrder 'NuGet', 'PowerShellGallery', 'GitHub'
        } else {
            New-ConfigurationRelease -StageRoot 'Module/Artefacts/UploadReady' -VersionSource Module -BuildOrder 'Module' -PublishOrder 'PowerShellGallery', 'GitHub'
        }
    }

    #New-ConfigurationModuleSkip -IgnoreModuleName 'Microsoft.PowerShell.Utility', 'ActiveDirectory' -IgnoreFunctionName 'Get-ADUser'
    # Disabled because PSPublishModule testing itself after build causes multiple module instances
    # which breaks InModuleScope tests. The module is tested separately via PSPublishModule.Tests.ps1
    #New-ConfigurationTest -TestsPath "$PSScriptRoot\..\Tests" -Enable

    # global options for publishing to github/psgallery
    # you can use FilePath where APIKey are saved in clear text or use APIKey directly
    New-ConfigurationPublish -Type PowerShellGallery -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' -Enabled:$true
    # Suppress the module's legacy GitHub publisher only when the outer workflow
    # will actually publish these artifacts through its unified GitHub release.
    New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:(-not $PowerForgeUnifiedGitHubRelease) -ID 'ToGitHub' -OverwriteTagName '<TagModuleVersionWithPreRelease>' -GenerateReleaseNotes


    # Optional one-time maintainer preflight: installs prerequisites, registers/probes the feed, and primes cached Entra/Azure DevOps auth when needed.
    #Initialize-ModuleRepository -ProfileName EvotecPowerShellGallery -Organization evotecpl -Project PowerShellGallery -Feed PowerShellGalleryFeed -InstallPrerequisites
    # Private feed publish target. This registers/refreshes the Azure Artifacts PSResourceGet repository before version checks and publish.
    #New-ConfigurationPublish -AzureDevOpsOrganization 'evotecpl' -AzureDevOpsProject 'PowerShellGallery' -AzureArtifactsFeed 'PowerShellGalleryFeed' -RepositoryName 'EvotecPowerShellGallery' -Tool PSResourceGet -Enabled:$true

    <#
    Direct PSResourceGet way:
    Initialize-ModuleRepository -ProfileName EvotecPowerShellGallery -Organization evotecpl -Project PowerShellGallery -Feed PowerShellGalleryFeed -InstallPrerequisites
    Install-PSResource -Name PSPublishModule -Repository EvotecPowerShellGallery -TrustRepository
    Update-PSResource -Name PSPublishModule

    PSPublishModule private module management:
    Install-Module PSPublishModule -Scope CurrentUser
    Initialize-ModuleRepository -ProfileName EvotecPowerShellGallery -Organization evotecpl -Project PowerShellGallery -Feed PowerShellGalleryFeed -InstallPrerequisites
    Install-PrivateModule -ProfileName EvotecPowerShellGallery -Name PSPublishModule -InstallPrerequisites
    Update-PrivateModule -ProfileName EvotecPowerShellGallery -Name PSPublishModule -InstallPrerequisites

    Auth behavior:
    If the credential provider has a valid cached Entra/Azure DevOps session, it should just use it.
    If no token exists or the token expired, it should prompt through the Azure Artifacts Credential Provider login flow, usually browser/device-code style.
    After successful login, the provider caches the session, so later install/update runs should not need anything fancy.
    No PAT, username, or password should be needed for normal interactive users.
    #>


    New-ConfigurationGate -Mode $RunMode

    ### FOR TESTING PURPOSES ONLY ###
    ### SHOWING HOW THINGS WORK HERE ###

    #New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed2" -IncludeTagName -ID 'Packed2'
    #New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed1" -IncludeTagName

    # those 2 are only useful for testing purposes
    # New-ConfigurationArtefact -Type Script -Enable -Path "$PSScriptRoot\..\Artefacts\Script" -IncludeTagName {
    #     # Lets test this, this will be added in the bottom of the script
    #     Invoke-ModuleBuilder
    # } -ID 'ToGitHubAsScript'
    # New-ConfigurationArtefact -Type ScriptPacked -Enable -Path "$PSScriptRoot\..\Artefacts\ScriptPacked" -ArtefactName "Script-<ModuleName>-$((Get-Date).ToString('yyyy-MM-dd')).zip" {
    #     Invoke-ModuleBuilder
    # } -PreScriptMerge {
    #     # Lets test this
    #     param (
    #         [int]$Mode
    #     )
    # } -ScriptName 'Invoke-ModuleBuilder.ps1'
    # New-ConfigurationArtefact -Type Script -Enable -Path "$PSScriptRoot\..\Artefacts\Script" {
    #     Invoke-ModuleBuilder
    # } -PreScriptMerge {
    #     # Lets test this
    #     param (
    #         [int]$Mode
    #     )
    # } -ScriptName 'Invoke-ModuleBuilder.ps1'

    #New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHubWithoutModules' -OverwriteTagName 'v1.8.0-Preview1'
    #New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHubAsScript'
}

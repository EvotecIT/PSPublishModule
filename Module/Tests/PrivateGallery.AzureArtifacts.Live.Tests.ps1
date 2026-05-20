$liveEnabled = $env:PSPUBLISHMODULE_AZDO_LIVE -eq '1'

function Add-AzureArtifactsLiveEvidenceItem {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Item
    )

    $path = $env:PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH
    if ([string]::IsNullOrWhiteSpace($path)) {
        return
    }

    $items = @()
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $raw = Get-Content -LiteralPath $path -Raw
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            $items = @($raw | ConvertFrom-Json)
        }
    }

    $items += [pscustomobject] $Item
    $directory = Split-Path -Path $path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $items | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
}

Describe 'Azure Artifacts private gallery live flow' -Tag 'Live', 'AzureArtifacts' {
    BeforeAll {
        if (-not $liveEnabled) {
            return
        }

        $moduleManifest = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) { $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH } else { Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1' }
        $tfm = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'net472' } else { 'net8.0' }
        $binaryModule = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath "../../PSPublishModule/bin/Release/$tfm") -ChildPath 'PSPublishModule.dll'

        if (Test-Path -LiteralPath $binaryModule) {
            Import-Module $binaryModule -Force -ErrorAction Stop
        } else {
            Import-Module $moduleManifest -Force -ErrorAction Stop
        }

        $script:LiveProfileRoot = Join-Path ([IO.Path]::GetTempPath()) ("PSPublishModule.PrivateGallery.Live." + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:LiveProfileRoot -Force | Out-Null
        $script:LiveProfilePath = Join-Path $script:LiveProfileRoot 'profiles.json'
        $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $script:LiveProfilePath
    }

    AfterAll {
        if (-not $liveEnabled) {
            return
        }

        Remove-Item Env:\POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH -ErrorAction SilentlyContinue
        if ($script:LiveProfileRoot -and (Test-Path -LiteralPath $script:LiveProfileRoot)) {
            Remove-Item -LiteralPath $script:LiveProfileRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'connects, builds publish configuration, installs, and updates using an Entra-backed Azure Artifacts profile' -Skip:(-not $liveEnabled) {
        $organization = $env:PSPUBLISHMODULE_AZDO_ORGANIZATION
        $project = $env:PSPUBLISHMODULE_AZDO_PROJECT
        $feed = $env:PSPUBLISHMODULE_AZDO_FEED
        $moduleName = $env:PSPUBLISHMODULE_AZDO_MODULE_NAME
        $profileName = if ($env:PSPUBLISHMODULE_AZDO_PROFILE_NAME) { $env:PSPUBLISHMODULE_AZDO_PROFILE_NAME } else { 'LiveAzureArtifacts' }

        @(
            'PSPUBLISHMODULE_AZDO_ORGANIZATION',
            'PSPUBLISHMODULE_AZDO_FEED',
            'PSPUBLISHMODULE_AZDO_MODULE_NAME'
        ) | ForEach-Object {
            if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))) {
                throw "Set $_ before running the live Azure Artifacts private gallery test."
            }
        }

        $setProfile = @{
            Name                   = $profileName
            AzureDevOpsOrganization = $organization
            AzureArtifactsFeed      = $feed
        }
        if (-not [string]::IsNullOrWhiteSpace($project)) {
            $setProfile.AzureDevOpsProject = $project
        }

        $profile = Set-ModuleRepositoryProfile @setProfile
        $profile.Name | Should -Be $profileName
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'

        $profileFile = Join-Path $script:LiveProfileRoot "$profileName.profile.json"
        Export-ModuleRepositoryProfile -Name $profileName -Path $profileFile -Force | Out-Null
        Remove-ModuleRepositoryProfile -Name $profileName

        $onboarding = Initialize-ModuleRepository -Path $profileFile -ProfileName $profileName -Overwrite -InstallPrerequisites -ErrorAction Stop
        $onboarding.Succeeded | Should -BeTrue
        $onboarding.ProfileWritten | Should -BeTrue
        $onboarding.ConnectAttempted | Should -BeTrue
        $onboarding.Connection | Should -Not -BeNullOrEmpty
        $onboarding.Connection.AccessProbePerformed | Should -BeTrue
        $onboarding.Connection.AccessProbeSucceeded | Should -BeTrue
        $onboarding.Connection.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)

        $publish = New-ConfigurationPublish -ProfileName $profileName -Enabled
        $publish.Configuration.Repository.Uri | Should -Match '^https://pkgs\.dev\.azure\.com/'
        $publish.Configuration.Repository.Credential | Should -BeNullOrEmpty

        $install = Install-PrivateModule -ProfileName $profileName -Name $moduleName -InstallPrerequisites -Force -ErrorAction Stop
        $install | Should -Not -BeNullOrEmpty

        $update = Update-PrivateModule -ProfileName $profileName -Name $moduleName -InstallPrerequisites -ErrorAction Stop
        $update | Should -Not -BeNullOrEmpty

        Add-AzureArtifactsLiveEvidenceItem @{
            Name                             = 'OnboardingInstallUpdate'
            Succeeded                        = $true
            ProfileName                      = $profileName
            ManagedProfileImported           = $onboarding.ProfileWritten
            ConnectAttempted                 = $onboarding.ConnectAttempted
            AccessProbePerformed             = $onboarding.Connection.AccessProbePerformed
            AccessProbeSucceeded             = $onboarding.Connection.AccessProbeSucceeded
            BootstrapModeUsed                = [string] $onboarding.Connection.BootstrapModeUsed
            PublishConfigurationUri          = $publish.Configuration.Repository.Uri
            PublishConfigurationHasCredential = $null -ne $publish.Configuration.Repository.Credential
            InstallResultReturned            = $null -ne $install
            UpdateResultReturned             = $null -ne $update
        }
    }

    It 'publishes a supplied NuGet package using an Entra-backed Azure Artifacts profile' -Skip:(-not $liveEnabled -or $env:PSPUBLISHMODULE_AZDO_PUBLISH_LIVE -ne '1' -or [string]::IsNullOrWhiteSpace($env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH)) {
        $organization = $env:PSPUBLISHMODULE_AZDO_ORGANIZATION
        $project = $env:PSPUBLISHMODULE_AZDO_PROJECT
        $feed = $env:PSPUBLISHMODULE_AZDO_FEED
        $packagePath = $env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH
        $profileName = if ($env:PSPUBLISHMODULE_AZDO_PROFILE_NAME) { $env:PSPUBLISHMODULE_AZDO_PROFILE_NAME } else { 'LiveAzureArtifactsPublish' }

        @(
            'PSPUBLISHMODULE_AZDO_ORGANIZATION',
            'PSPUBLISHMODULE_AZDO_FEED',
            'PSPUBLISHMODULE_AZDO_PACKAGE_PATH'
        ) | ForEach-Object {
            if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))) {
                throw "Set $_ before running the live Azure Artifacts publish test."
            }
        }

        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            throw "Package path '$packagePath' was not found."
        }

        $setProfile = @{
            Name                    = $profileName
            AzureDevOpsOrganization = $organization
            AzureArtifactsFeed      = $feed
        }
        if (-not [string]::IsNullOrWhiteSpace($project)) {
            $setProfile.AzureDevOpsProject = $project
        }

        $packageRoot = Join-Path $script:LiveProfileRoot 'publish'
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $publishPackagePath = Join-Path $packageRoot ([IO.Path]::GetFileName($packagePath))
        Copy-Item -LiteralPath $packagePath -Destination $publishPackagePath -Force

        $onboarding = Initialize-ModuleRepository @setProfile -InstallPrerequisites -ErrorAction Stop
        $onboarding.Succeeded | Should -BeTrue
        $onboarding.Connection.AccessProbeSucceeded | Should -BeTrue

        $result = Publish-NugetPackage -Path $packageRoot -ProfileName $profileName -InstallPrerequisites -SkipDuplicate -ErrorAction Stop

        $result.Success | Should -BeTrue
        $result.ProfileName | Should -Be $profileName
        $result.Source | Should -Match '^https://pkgs\.dev\.azure\.com/'
        $result.Pushed | Should -Contain ([IO.Path]::GetFullPath($publishPackagePath))
        $result.Failed | Should -BeNullOrEmpty

        Add-AzureArtifactsLiveEvidenceItem @{
            Name                 = 'PublishPackage'
            Succeeded            = $true
            ProfileName          = $profileName
            AccessProbeSucceeded = $onboarding.Connection.AccessProbeSucceeded
            PackageName          = [IO.Path]::GetFileName($publishPackagePath)
            Source               = $result.Source
            PushedPackageNames   = @($result.Pushed | ForEach-Object { [IO.Path]::GetFileName($_) })
            FailedCount          = if ($null -eq $result.Failed) { 0 } else { @($result.Failed).Count }
        }
    }
}

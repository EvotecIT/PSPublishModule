Describe 'Private gallery command metadata' {
    BeforeAll {
        $moduleManifest = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) { $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH } else { Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1' }
        $tfm = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'net472' } else { 'net8.0' }
        $binaryModule = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath "../../PSPublishModule/bin/Release/$tfm") -ChildPath 'PSPublishModule.dll'

        if (Test-Path -LiteralPath $binaryModule) {
            try {
                $script:PrivateGalleryTestModule = Import-Module $binaryModule -Force -PassThru -ErrorAction Stop
            } catch {
                $script:PrivateGalleryTestModule = Import-Module $moduleManifest -Force -PassThru -ErrorAction Stop
            }
        } else {
            $loadedModule = Get-Module PSPublishModule -ErrorAction SilentlyContinue
            if ($loadedModule) {
                $script:PrivateGalleryTestModule = $loadedModule
            } else {
                $existingCommand = Get-Command Connect-ModuleRepository -ErrorAction SilentlyContinue
                if ($existingCommand -and $existingCommand.Module) {
                    $script:PrivateGalleryTestModule = $existingCommand.Module
                } else {
                    $script:PrivateGalleryTestModule = Import-Module $moduleManifest -Force -PassThru -ErrorAction Stop
                }
            }
        }

        $command = Get-Command Connect-ModuleRepository -ErrorAction Stop
        $script:PrivateGalleryTestAssembly = if ($script:PrivateGalleryTestModule.ImplementingAssembly) {
            $script:PrivateGalleryTestModule.ImplementingAssembly
        } else {
            $command.ImplementingType.Assembly
        }

        $script:PrivateGalleryProfileRoot = Join-Path ([IO.Path]::GetTempPath()) ("PSPublishModule.PrivateGallery.Tests." + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:PrivateGalleryProfileRoot -Force | Out-Null
        $script:PrivateGalleryProfilePath = Join-Path $script:PrivateGalleryProfileRoot 'profiles.json'
        $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $script:PrivateGalleryProfilePath
    }

    AfterAll {
        Remove-Item Env:\POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH -ErrorAction SilentlyContinue
        if ($script:PrivateGalleryProfileRoot -and (Test-Path -LiteralPath $script:PrivateGalleryProfileRoot)) {
            Remove-Item -LiteralPath $script:PrivateGalleryProfileRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'exposes the private gallery wrapper cmdlets' {
        $module = $script:PrivateGalleryTestModule
        $module.ExportedCmdlets.Keys | Should -Contain 'Connect-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Export-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Register-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Install-PrivateModule'
        $module.ExportedCmdlets.Keys | Should -Contain 'Get-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Import-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Initialize-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Set-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Remove-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Test-ModuleRepositoryProfile'
        $module.ExportedCmdlets.Keys | Should -Contain 'Update-PrivateModule'
        $module.ExportedCmdlets.Keys | Should -Contain 'Update-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Publish-NugetPackage'
    }

    It 'keeps install/update wrapper parameter sets intact' {
        $install = Get-Command Install-PrivateModule -ErrorAction Stop
        $install.DefaultParameterSet | Should -Be 'Repository'
        $install.ParameterSets.Name | Should -Contain 'Repository'
        $install.ParameterSets.Name | Should -Contain 'AzureArtifacts'

        $update = Get-Command Update-PrivateModule -ErrorAction Stop
        $update.DefaultParameterSet | Should -Be 'Repository'
        $update.ParameterSets.Name | Should -Contain 'Repository'
        $update.ParameterSets.Name | Should -Contain 'AzureArtifacts'
        $update.ParameterSets.Name | Should -Contain 'Profile'
    }

    It 'offers onboarding-friendly aliases' {
        $module = $script:PrivateGalleryTestModule
        $connect = $module.ExportedCmdlets['Connect-ModuleRepository']
        $connect.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $connect.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $connect.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $connect.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $connect.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $connect.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $connect.ParameterSets.Name | Should -Contain 'Profile'

        $register = $module.ExportedCmdlets['Register-ModuleRepository']
        $register.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $register.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $register.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $register.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $register.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $register.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $register.ParameterSets.Name | Should -Contain 'Profile'

        $install = $module.ExportedCmdlets['Install-PrivateModule']
        $install.Parameters['Name'].Aliases | Should -Contain 'ModuleName'
        $install.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $install.Parameters['CredentialSecret'].Aliases | Should -Contain 'Token'
        $install.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $install.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $install.ParameterSets.Name | Should -Contain 'Profile'

        $update = $module.ExportedCmdlets['Update-PrivateModule']
        $update.Parameters['Name'].Aliases | Should -Contain 'ModuleName'
        $update.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $update.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $update.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $update.ParameterSets.Name | Should -Contain 'Profile'

        $profile = $module.ExportedCmdlets['Set-ModuleRepositoryProfile']
        $profile.Parameters['Name'].Aliases | Should -Contain 'ProfileName'
        $profile.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $profile.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $profile.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $profile.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'

        $exportProfile = $module.ExportedCmdlets['Export-ModuleRepositoryProfile']
        $exportProfile.Parameters['Name'].Aliases | Should -Contain 'ProfileName'

        $importProfile = $module.ExportedCmdlets['Import-ModuleRepositoryProfile']
        $importProfile.Parameters.Keys | Should -Contain 'Overwrite'

        $initialize = $module.ExportedCmdlets['Initialize-ModuleRepository']
        $initialize.ParameterSets.Name | Should -Contain 'Profile'
        $initialize.ParameterSets.Name | Should -Contain 'Import'
        $initialize.ParameterSets.Name | Should -Contain 'AzureArtifacts'
        $initialize.Parameters['ProfileName'].Aliases | Should -Contain 'Profile'
        $initialize.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $initialize.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $initialize.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $initialize.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $initialize.Parameters.Keys | Should -Contain 'InstallPrerequisites'
        $initialize.Parameters.Keys | Should -Contain 'SkipConnect'

        $testProfile = $module.ExportedCmdlets['Test-ModuleRepositoryProfile']
        $testProfile.Parameters['ProfileName'].Aliases | Should -Contain 'Name'
        $testProfile.Parameters['ProfileName'].Aliases | Should -Contain 'Profile'

        $publishPackage = $module.ExportedCmdlets['Publish-NugetPackage']
        $publishPackage.ParameterSets.Name | Should -Contain 'Profile'
        $publishPackage.Parameters['ProfileName'].Aliases | Should -Contain 'Profile'
    }

    It 'saves Azure Artifacts profiles with Entra-first defaults' {
        $profile = Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules'

        $profile.Name | Should -Be 'Company'
        $profile.RepositoryName | Should -Be 'Modules'
        $profile.Tool | Should -Be ([PowerForge.RepositoryRegistrationTool]::PSResourceGet)
        $profile.BootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
        Test-Path -LiteralPath $script:PrivateGalleryProfilePath | Should -BeTrue
    }

    It 'exports and imports non-secret managed profile files' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'Company.profile.json'

        $exported = Export-ModuleRepositoryProfile -Name 'Company' -Path $exportPath -Force -PassThru
        $json = Get-Content -LiteralPath $exportPath -Raw

        $exported.Name | Should -Be 'Company'
        $json | Should -Match '"Profiles"'
        $json | Should -Not -Match '"Secret"'
        $json | Should -Not -Match '"Password"'
        $json | Should -Not -Match '"Token"'

        Remove-ModuleRepositoryProfile -Name 'Company'
        Get-ModuleRepositoryProfile -Name 'Company' -ErrorAction SilentlyContinue | Should -BeNullOrEmpty

        $imported = Import-ModuleRepositoryProfile -Path $exportPath

        $imported.Name | Should -Be 'Company'
        $profile = Get-ModuleRepositoryProfile -Name 'Company'
        $profile.AzureDevOpsOrganization | Should -Be 'contoso'
        $profile.AzureDevOpsProject | Should -Be 'Platform'
        $profile.AzureArtifactsFeed | Should -Be 'Modules'
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
    }

    It 'requires overwrite when importing an existing managed profile' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'Company.overwrite.profile.json'
        Export-ModuleRepositoryProfile -Name 'Company' -Path $exportPath -Force

        {
            Import-ModuleRepositoryProfile -Path $exportPath
        } | Should -Throw "*already exists*"

        $imported = Import-ModuleRepositoryProfile -Path $exportPath -Overwrite

        $imported.Name | Should -Be 'Company'
    }

    It 'initializes a new Azure Artifacts profile without connecting when requested' {
        $result = Initialize-ModuleRepository -Name 'CompanyInit' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -SkipConnect

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryOnboardingResult'
        $result.ProfileName | Should -Be 'CompanyInit'
        $result.ProfileFound | Should -BeTrue
        $result.ProfileWritten | Should -BeTrue
        $result.ConnectAttempted | Should -BeFalse
        $result.ConnectSkipped | Should -BeTrue
        $result.Succeeded | Should -BeTrue
        $result.Profile.RepositoryName | Should -Be 'Modules'
        $result.Readiness.RepositoryName | Should -Be 'Modules'
        $result.RecommendedInstallCommand | Should -Be "Install-PrivateModule -ProfileName 'CompanyInit' -Name <ModuleName>"
        $result.RecommendedUpdateCommand | Should -Be "Update-PrivateModule -ProfileName 'CompanyInit' -Name <ModuleName>"

        $profile = Get-ModuleRepositoryProfile -Name 'CompanyInit'
        $profile.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
    }

    It 'initializes from a managed profile file in one command without connecting when requested' {
        Set-ModuleRepositoryProfile -Name 'CompanyFile' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'CompanyFile.profile.json'
        Export-ModuleRepositoryProfile -Name 'CompanyFile' -Path $exportPath -Force
        Remove-ModuleRepositoryProfile -Name 'CompanyFile'

        $result = Initialize-ModuleRepository -Path $exportPath -ProfileName 'CompanyFile' -Overwrite -SkipConnect

        $result.ProfileName | Should -Be 'CompanyFile'
        $result.ProfileWritten | Should -BeTrue
        $result.ImportedFromPath | Should -Be $exportPath
        $result.ConnectAttempted | Should -BeFalse
        $result.ConnectSkipped | Should -BeTrue
        $result.Profile.AzureDevOpsProject | Should -Be 'Platform'

        $profile = Get-ModuleRepositoryProfile -Name 'CompanyFile'
        $profile.AzureArtifactsFeed | Should -Be 'Modules'
    }

    It 'initializes a managed profile file with WhatIf without writing or probing' {
        Set-ModuleRepositoryProfile -Name 'CompanyWhatIf' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $exportPath = Join-Path $script:PrivateGalleryProfileRoot 'CompanyWhatIf.profile.json'
        Export-ModuleRepositoryProfile -Name 'CompanyWhatIf' -Path $exportPath -Force
        Remove-ModuleRepositoryProfile -Name 'CompanyWhatIf'

        $result = Initialize-ModuleRepository -Path $exportPath -ProfileName 'CompanyWhatIf' -Overwrite -InstallPrerequisites -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.ProfileName | Should -Be 'CompanyWhatIf'
        $result.ProfileWritten | Should -BeFalse
        $result.ConnectAttempted | Should -BeTrue
        $result.ConnectSkipped | Should -BeTrue
        $result.Succeeded | Should -BeTrue
        $result.Connection | Should -Not -BeNullOrEmpty
        $result.Connection.RegistrationPerformed | Should -BeFalse
        $result.Connection.AccessProbePerformed | Should -BeFalse
        Get-ModuleRepositoryProfile -Name 'CompanyWhatIf' -ErrorAction SilentlyContinue | Should -BeNullOrEmpty
    }

    It 'tests saved profile readiness without registering repositories' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null

        $result = Test-ModuleRepositoryProfile -ProfileName 'Company'

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryProfileReadinessResult'
        $result.Name | Should -Be 'Company'
        $result.ProfileFound | Should -BeTrue
        $result.RepositoryName | Should -Be 'Modules'
        $result.PSResourceGetUri | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json'
        $result.PowerShellGetSourceUri | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2'
        $result.ProfileStorePath | Should -Be $script:PrivateGalleryProfilePath
        $result.AuthenticationMode | Should -Be 'AzureArtifactsCredentialProvider'
        $result.RecommendedConnectCommand | Should -Match "Connect-ModuleRepository -ProfileName 'Company'"
        $result.RecommendedOnboardingCommand | Should -Match "Initialize-ModuleRepository -ProfileName 'Company'"
        $result.RecommendedInstallCommand | Should -Be "Install-PrivateModule -Name <ModuleName> -ProfileName 'Company'"
        $result.ReadinessMessages | Should -Not -BeNullOrEmpty
    }

    It 'returns a non-terminating readiness object for a missing profile' {
        $result = Test-ModuleRepositoryProfile -ProfileName 'MissingCompany'

        $result | Should -Not -BeNullOrEmpty
        $result.Name | Should -Be 'MissingCompany'
        $result.ProfileFound | Should -BeFalse
        $result.IsReady | Should -BeFalse
        $result.ProfileStorePath | Should -Be $script:PrivateGalleryProfilePath
        $result.ReadinessMessages | Should -Contain "Module repository profile 'MissingCompany' was not found. Create or import it with Initialize-ModuleRepository before installing, updating, or publishing."
    }

    It 'treats an ExistingSession profile as not ready when PSResourceGet is below the Entra bootstrap version' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryProfileReadinessResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.ProfileFound = $true
        $result.BootstrapMode = [PowerForge.PrivateGalleryBootstrapMode]::ExistingSession
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.1.1'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $result.PSResourceGetSupportsExistingSessionBootstrap = $false
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $true
        $result.CredentialPromptBootstrapReady = $true

        $result.CredentialPromptBootstrapReady | Should -BeTrue
        $result.ExistingSessionBootstrapReady | Should -BeFalse
        $result.IsReady | Should -BeFalse

        $result.BootstrapMode = [PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt
        $result.IsReady | Should -BeTrue
    }

    It 'uses saved profiles for WhatIf repository connection without prompting' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null

        $result = Connect-ModuleRepository -ProfileName 'Company' -InstallPrerequisites -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.RepositoryName | Should -Be 'Modules'
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $result.RegistrationPerformed | Should -BeFalse
    }

    It 'uses saved profiles for Azure Artifacts publish configuration' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null

        $publish = New-ConfigurationPublish -ProfileName 'Company' -Enabled

        $publish.Configuration.RepositoryName | Should -Be 'Modules'
        $publish.Configuration.Tool | Should -Be ([PowerForge.PublishTool]::PSResourceGet)
        $publish.Configuration.Repository.Uri | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json'
        $publish.Configuration.Repository.Credential | Should -BeNullOrEmpty
    }

    It 'uses saved profiles for Azure Artifacts NuGet package publishing' {
        Set-ModuleRepositoryProfile -Name 'Company' -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' | Out-Null
        $packageRoot = Join-Path $script:PrivateGalleryProfileRoot 'packages'
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        $packagePath = Join-Path $packageRoot 'Company.Tools.1.0.0.nupkg'
        Set-Content -LiteralPath $packagePath -Value 'placeholder' -NoNewline

        $result = Publish-NugetPackage -Path $packageRoot -ProfileName 'Company' -SkipDuplicate -WhatIf

        $result | Should -Not -BeNullOrEmpty
        $result.Success | Should -BeTrue
        $result.ProfileName | Should -Be 'Company'
        $result.RepositoryName | Should -Be 'Modules'
        $result.Source | Should -Be 'https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json'
        $result.Pushed | Should -Contain $packagePath
        $result.Failed | Should -BeNullOrEmpty
    }

    It 'reports readiness information on the registration result type' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureDevOpsProject = 'Platform'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetRegistered = $true
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.2.0'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $true)
        $result.PowerShellGetAvailable = $true
        $result.PowerShellGetVersion = '2.2.5'
        $result.AzureArtifactsCredentialProviderDetected = $true
        $result.AzureArtifactsCredentialProviderVersion = '2.0.312'
        $type.GetProperty('AccessProbePerformed').SetValue($result, $true)
        $type.GetProperty('AccessProbeSucceeded').SetValue($result, $true)
        $type.GetProperty('AccessProbeTool').SetValue($result, 'PSResourceGet')
        $type.GetProperty('AccessProbeMessage').SetValue($result, 'Repository access probe completed successfully via PSResourceGet.')
        $result.InstalledPrerequisites = @('PSResourceGet')
        $result.PrerequisiteInstallMessages = @('PSResourceGet prerequisite handled via PowerShellGet (Installed).')
        $result.ToolRequested = [PowerForge.RepositoryRegistrationTool]::Auto
        $result.ToolUsed = [PowerForge.RepositoryRegistrationTool]::PSResourceGet
        $result.BootstrapModeRequested = [PowerForge.PrivateGalleryBootstrapMode]::Auto
        $result.BootstrapModeUsed = [PowerForge.PrivateGalleryBootstrapMode]::ExistingSession
        $result.CredentialSource = [PowerForge.PrivateGalleryCredentialSource]::None
        $installPrerequisitesRecommended = $type.GetProperty('InstallPrerequisitesRecommended').GetValue($result)

        $result.ExistingSessionBootstrapReady | Should -BeTrue
        $result.CredentialPromptBootstrapReady | Should -BeTrue
        $result.RecommendedBootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $installPrerequisitesRecommended | Should -BeFalse
        $result.RecommendedBootstrapCommand | Should -Be "Register-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -Name 'Company' -BootstrapMode ExistingSession"
        $result.InstallPSResourceReady | Should -BeTrue
        $result.InstallModuleReady | Should -BeFalse
        $result.ReadyCommands | Should -Contain 'Install-PSResource'
        $result.PreferredInstallCommand | Should -Be 'Install-PSResource'
        $result.RecommendedWrapperInstallCommand | Should -Be "Install-PrivateModule -Name <ModuleName> -Repository 'Company'"
        $result.RecommendedNativeInstallCommand | Should -Be "Install-PSResource -Name <ModuleName> -Repository 'Company'"
        $result.ToolRequested | Should -Be ([PowerForge.RepositoryRegistrationTool]::Auto)
        $result.ToolUsed | Should -Be ([PowerForge.RepositoryRegistrationTool]::PSResourceGet)
        $result.PSResourceGetVersion | Should -Be '1.2.0'
        $result.PSResourceGetMeetsMinimumVersion | Should -BeTrue
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').GetValue($result) | Should -BeTrue
        $result.PowerShellGetVersion | Should -Be '2.2.5'
        $result.AzureArtifactsCredentialProviderVersion | Should -Be '2.0.312'
        $result.BootstrapModeRequested | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::Auto)
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::ExistingSession)
        $result.CredentialSource | Should -Be ([PowerForge.PrivateGalleryCredentialSource]::None)
        $type.GetProperty('AccessProbePerformed').GetValue($result) | Should -BeTrue
        $type.GetProperty('AccessProbeSucceeded').GetValue($result) | Should -BeTrue
        $type.GetProperty('AccessProbeTool').GetValue($result) | Should -Be 'PSResourceGet'
        $type.GetProperty('AccessProbeMessage').GetValue($result) | Should -Be 'Repository access probe completed successfully via PSResourceGet.'
        $result.InstalledPrerequisites | Should -Contain 'PSResourceGet'
        $result.PrerequisiteInstallMessages | Should -Contain 'PSResourceGet prerequisite handled via PowerShellGet (Installed).'
    }

    It 'recommends prerequisite installation when bootstrap dependencies are missing or outdated' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureDevOpsProject = 'Platform'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.0.9'
        $result.PSResourceGetMeetsMinimumVersion = $false
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $false)
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $false
        $installPrerequisitesRecommended = $type.GetProperty('InstallPrerequisitesRecommended').GetValue($result)

        $installPrerequisitesRecommended | Should -BeTrue
        $result.RecommendedBootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
        $result.RecommendedBootstrapCommand | Should -Be "Register-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -Name 'Company' -InstallPrerequisites -BootstrapMode CredentialPrompt -Interactive"
    }

    It 'prefers Install-Module when PSResourceGet cannot support ExistingSession bootstrap' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetRegistered = $true
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.1.1'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $false)
        $result.PowerShellGetRegistered = $true
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $true

        $result.ExistingSessionBootstrapReady | Should -BeFalse
        $result.CredentialPromptBootstrapReady | Should -BeTrue
        $result.InstallPSResourceReady | Should -BeFalse
        $result.InstallModuleReady | Should -BeTrue
        $result.PreferredInstallCommand | Should -Be 'Install-Module'
        $result.RecommendedNativeInstallCommand | Should -Be "Install-Module -Name <ModuleName> -Repository 'Company'"
        $result.RecommendedBootstrapMode | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
    }

    It 'recommends prerequisite installation for Entra-first bootstrap when PSResourceGet is too old' {
        $type = $script:PrivateGalleryTestAssembly.GetType('PSPublishModule.ModuleRepositoryRegistrationResult', $true)
        $result = [System.Activator]::CreateInstance($type)
        $result.RepositoryName = 'Company'
        $result.AzureDevOpsOrganization = 'contoso'
        $result.AzureArtifactsFeed = 'Modules'
        $result.PSResourceGetAvailable = $true
        $result.PSResourceGetVersion = '1.1.1'
        $result.PSResourceGetMeetsMinimumVersion = $true
        $type.GetProperty('PSResourceGetSupportsExistingSessionBootstrap').SetValue($result, $false)
        $result.PowerShellGetAvailable = $true
        $result.AzureArtifactsCredentialProviderDetected = $true
        $result.BootstrapModeRequested = [PowerForge.PrivateGalleryBootstrapMode]::ExistingSession

        $result.InstallPrerequisitesRecommended | Should -BeTrue
        $result.RecommendedBootstrapCommand | Should -Match '-InstallPrerequisites'

        $result.BootstrapModeRequested = [PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt
        $result.InstallPrerequisitesRecommended | Should -BeFalse
    }

    It 'rejects PSGallery as an Azure Artifacts repository name' {
        {
            New-ConfigurationPublish -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -RepositoryName 'PSGallery' -Enabled
        } | Should -Throw "*RepositoryName cannot be 'PSGallery' when using the Azure Artifacts preset.*"
    }

    It 'rejects Azure Artifacts feeds that resolve to PSGallery' {
        {
            New-ConfigurationPublish -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'PSGallery' -Enabled
        } | Should -Throw "*RepositoryName cannot be 'PSGallery' when using the Azure Artifacts preset.*"
    }

    It 'returns a registration result for Connect-ModuleRepository -WhatIf' {
        $result = Connect-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -BootstrapMode ExistingSession -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryRegistrationResult'
        $result.RegistrationPerformed | Should -BeFalse
    }

    It 'does not prompt for credentials during Register-ModuleRepository -WhatIf' {
        $result = Register-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -BootstrapMode CredentialPrompt -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryRegistrationResult'
        $result.RegistrationPerformed | Should -BeFalse
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
    }

    It 'does not prompt for credentials during Update-ModuleRepository -WhatIf' {
        $result = Update-ModuleRepository -AzureDevOpsOrganization 'contoso' -AzureArtifactsFeed 'Modules' -BootstrapMode CredentialPrompt -WhatIf -WarningAction SilentlyContinue

        $result | Should -Not -BeNullOrEmpty
        $result.GetType().FullName | Should -Be 'PSPublishModule.ModuleRepositoryRegistrationResult'
        $result.RegistrationPerformed | Should -BeFalse
        $result.BootstrapModeUsed | Should -Be ([PowerForge.PrivateGalleryBootstrapMode]::CredentialPrompt)
    }
}

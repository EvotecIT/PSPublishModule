Describe 'Private gallery command metadata' {
    BeforeAll {
        $existingCommand = Get-Command Connect-ModuleRepository -ErrorAction SilentlyContinue
        $loadedModule = Get-Module PSPublishModule -ErrorAction SilentlyContinue
        $moduleManifest = if ($env:PSPUBLISHMODULE_TEST_MANIFEST_PATH) { $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH } else { Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1' }
        $runtimesText = (dotnet --list-runtimes 2>$null) -join "`n"
        $tfm = if ($runtimesText -match '(?m)^Microsoft\.NETCore\.App\s+10\.') { 'net10.0' } else { 'net8.0' }
        $binaryModule = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath "../../PSPublishModule/bin/Release/$tfm") -ChildPath 'PSPublishModule.dll'

        if ($existingCommand -and $existingCommand.Module) {
            $script:PrivateGalleryTestModule = $existingCommand.Module
        } elseif ($loadedModule) {
            $script:PrivateGalleryTestModule = $loadedModule
        } elseif (Test-Path -LiteralPath $binaryModule) {
            try {
                $script:PrivateGalleryTestModule = Import-Module $binaryModule -Force -PassThru -ErrorAction Stop
            } catch {
                $script:PrivateGalleryTestModule = Import-Module $moduleManifest -Force -PassThru -ErrorAction Stop
            }
        } else {
            $script:PrivateGalleryTestModule = Import-Module $moduleManifest -Force -PassThru -ErrorAction Stop
        }

        $command = Get-Command Connect-ModuleRepository -ErrorAction Stop
        $script:PrivateGalleryTestAssembly = if ($script:PrivateGalleryTestModule.ImplementingAssembly) {
            $script:PrivateGalleryTestModule.ImplementingAssembly
        } else {
            $command.ImplementingType.Assembly
        }
    }

    It 'exposes the private gallery wrapper cmdlets' {
        $module = $script:PrivateGalleryTestModule
        $module.ExportedCmdlets.Keys | Should -Contain 'Connect-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Register-ModuleRepository'
        $module.ExportedCmdlets.Keys | Should -Contain 'Install-PrivateModule'
        $module.ExportedCmdlets.Keys | Should -Contain 'Update-PrivateModule'
        $module.ExportedCmdlets.Keys | Should -Contain 'Update-ModuleRepository'
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

        $register = $module.ExportedCmdlets['Register-ModuleRepository']
        $register.Parameters['AzureDevOpsOrganization'].Aliases | Should -Contain 'Organization'
        $register.Parameters['AzureDevOpsProject'].Aliases | Should -Contain 'Project'
        $register.Parameters['AzureArtifactsFeed'].Aliases | Should -Contain 'Feed'
        $register.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $register.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $register.Parameters.Keys | Should -Contain 'InstallPrerequisites'

        $install = $module.ExportedCmdlets['Install-PrivateModule']
        $install.Parameters['Name'].Aliases | Should -Contain 'ModuleName'
        $install.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $install.Parameters['CredentialSecret'].Aliases | Should -Contain 'Token'
        $install.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $install.Parameters.Keys | Should -Contain 'InstallPrerequisites'

        $update = $module.ExportedCmdlets['Update-PrivateModule']
        $update.Parameters['Name'].Aliases | Should -Contain 'ModuleName'
        $update.Parameters['PromptForCredential'].Aliases | Should -Contain 'Interactive'
        $update.Parameters['BootstrapMode'].Aliases | Should -Contain 'Mode'
        $update.Parameters.Keys | Should -Contain 'InstallPrerequisites'
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

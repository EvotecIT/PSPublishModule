@{
    AliasesToExport        = @('Show-Documentation', 'Set-Documentation', 'Install-Scripts', 'Install-ModuleScripts', 'Install-Documentation', 'Build-Module', 'Connect-Gallery', 'Export-GalleryProfile', 'Get-GalleryProfile', 'Import-GalleryProfile', 'Initialize-Gallery', 'Invoke-ModuleBuilder', 'New-GalleryBootstrap', 'New-PrepareModule', 'Register-Gallery', 'Remove-GalleryProfile', 'Set-GalleryProfile', 'Test-GalleryProfile')
    Author                 = 'Przemyslaw Klys'
    CmdletsToExport        = @('Show-ModuleDocumentation', 'Set-ModuleDocumentation', 'Install-ModuleScript', 'Install-ModuleDocumentation', 'Get-ModuleDocumentation', 'Connect-ModuleRepository', 'Convert-ProjectConsistency', 'Export-CertificateForNuGet', 'Export-ConfigurationProject', 'Export-GalleryProfile', 'Export-ModuleRepositoryProfile', 'Get-GalleryProfile', 'Get-MissingFunctions', 'Get-ModuleInformation', 'Get-ModuleRepositoryProfile', 'Get-ModuleTestFailures', 'Get-PowerShellAssemblyMetadata', 'Get-PowerShellCompatibility', 'Get-ProjectConsistency', 'Get-ProjectVersion', 'Import-ConfigurationProject', 'Import-GalleryProfile', 'Import-ModuleRepositoryProfile', 'Initialize-Gallery', 'Initialize-ModuleRepository', 'Install-PrivateModule', 'Invoke-DotNetPublish', 'Invoke-DotNetReleaseBuild', 'Invoke-DotNetRepositoryRelease', 'Invoke-ModuleBuild', 'Invoke-ModuleTestSuite', 'Invoke-PowerForgeBundlePostProcess', 'Invoke-PowerForgePluginExport', 'Invoke-PowerForgePluginPack', 'Invoke-PowerForgeRelease', 'Invoke-ProjectBuild', 'Invoke-ProjectRelease', 'New-ConfigurationArtefact', 'New-ConfigurationBuild', 'New-ConfigurationCommand', 'New-ConfigurationCompatibility', 'New-ConfigurationDelivery', 'New-ConfigurationDocumentation', 'New-ConfigurationDotNetBenchmarkGate', 'New-ConfigurationDotNetBenchmarkMetric', 'New-ConfigurationDotNetConfigBootstrapRule', 'New-ConfigurationDotNetInstaller', 'New-ConfigurationDotNetMatrix', 'New-ConfigurationDotNetMatrixRule', 'New-ConfigurationDotNetProfile', 'New-ConfigurationDotNetProject', 'New-ConfigurationDotNetPublish', 'New-ConfigurationDotNetService', 'New-ConfigurationDotNetServiceLifecycle', 'New-ConfigurationDotNetServiceRecovery', 'New-ConfigurationDotNetSign', 'New-ConfigurationDotNetState', 'New-ConfigurationDotNetStateRule', 'New-ConfigurationDotNetTarget', 'New-ConfigurationExecute', 'New-ConfigurationFileConsistency', 'New-ConfigurationFormat', 'New-ConfigurationImportModule', 'New-ConfigurationInformation', 'New-ConfigurationManifest', 'New-ConfigurationModule', 'New-ConfigurationModuleSkip', 'New-ConfigurationPlaceHolder', 'New-ConfigurationProject', 'New-ConfigurationProjectInstaller', 'New-ConfigurationProjectOutput', 'New-ConfigurationProjectRelease', 'New-ConfigurationProjectSigning', 'New-ConfigurationProjectTarget', 'New-ConfigurationProjectWorkspace', 'New-ConfigurationPublish', 'New-ConfigurationTest', 'New-ConfigurationValidation', 'New-DotNetPublishConfig', 'New-ModuleAboutTopic', 'New-ModuleRepositoryBootstrap', 'New-PowerForgeReleaseConfig', 'New-ProjectReleaseConfig', 'Publish-GitHubReleaseAsset', 'Publish-NugetPackage', 'Register-Certificate', 'Register-Gallery', 'Register-ModuleRepository', 'Remove-Comments', 'Remove-GalleryProfile', 'Remove-ModuleRepositoryProfile', 'Remove-ProjectFiles', 'Send-GitHubRelease', 'Set-GalleryProfile', 'Set-ModuleRepositoryProfile', 'Set-ProjectVersion', 'Step-Version', 'Test-GalleryProfile', 'Test-ModuleRepositoryProfile', 'Update-ModuleRepository', 'Update-PrivateModule')
    CompanyName            = 'Evotec'
    CompatiblePSEditions   = @('Desktop', 'Core')
    Copyright              = '(c) 2011 - 2026 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description            = 'Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery'
    DotNetFrameworkVersion = '4.5.2'
    FunctionsToExport      = @()
    GUID                   = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
    ModuleVersion          = '3.0.19'
    PowerShellVersion      = '5.1'
    PrivateData            = @{
        PSData = @{
            IconUri                    = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
            ProjectUri                 = 'https://github.com/EvotecIT/PSPublishModule'
            RequireLicenseAcceptance   = $false
            Tags                       = @('Windows', 'MacOS', 'Linux', 'Build', 'Module')
            ExternalModuleDependencies = @()
        }
    }
    RequiredModules        = @(@{
            Guid            = '1d73a601-4a6c-43c5-ba3f-619b18bbb404'
            ModuleName      = 'powershellget'
            ModuleVersion   = '2.2.5'
        }, @{
            Guid            = 'a699dea5-2c73-4616-a270-1f7abb777e71'
            ModuleName      = 'Pester'
            ModuleVersion   = '5.7.1'
        })
    RootModule             = 'PSPublishModule.psm1'
    NestedModules          = @()
    ScriptsToProcess       = @()
}

@{
    AliasesToExport        = @('New-PrepareModule', 'Build-Module', 'Invoke-ModuleBuilder')
    Author                 = 'Przemyslaw Klys'
    CmdletsToExport        = @()
    CompanyName            = 'Evotec'
    CompatiblePSEditions   = @('Desktop', 'Core')
    Copyright              = '(c) 2011 - 2025 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description            = 'Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery'
    DotNetFrameworkVersion = '4.5.2'
    FunctionsToExport      = @('Convert-CommandsToList', 'Convert-ProjectEncoding', 'Convert-ProjectLineEnding', 'Get-MissingFunctions', 'Get-PowerShellAssemblyMetadata', 'Get-PowerShellCompatibility', 'Get-ProjectConsistency', 'Get-ProjectEncoding', 'Get-ProjectLineEnding', 'Get-ProjectVersion', 'Initialize-PortableModule', 'Initialize-PortableScript', 'Initialize-ProjectManager', 'Invoke-DotNetReleaseBuild', 'Invoke-ModuleBuild', 'New-ConfigurationArtefact', 'New-ConfigurationBuild', 'New-ConfigurationCommand', 'New-ConfigurationCompatibility', 'New-ConfigurationDocumentation', 'New-ConfigurationExecute', 'New-ConfigurationFileConsistency', 'New-ConfigurationFormat', 'New-ConfigurationImportModule', 'New-ConfigurationInformation', 'New-ConfigurationManifest', 'New-ConfigurationModule', 'New-ConfigurationModuleSkip', 'New-ConfigurationPlaceHolder', 'New-ConfigurationPublish', 'New-ConfigurationTest', 'Publish-GitHubReleaseAsset', 'Publish-NugetPackage', 'Register-Certificate', 'Remove-Comments', 'Send-GitHubRelease', 'Set-ProjectVersion', 'Test-BasicModule', 'Test-ScriptFile', 'Test-ScriptModule')
    GUID                   = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
    ModuleVersion          = '2.0.22'
    PowerShellVersion      = '5.1'
    PrivateData            = @{
        PSData = @{
            ExternalModuleDependencies = @('Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')
            IconUri                    = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
            ProjectUri                 = 'https://github.com/EvotecIT/PSPublishModule'
            Tags                       = @('Windows', 'MacOS', 'Linux', 'Build', 'Module')
        }
    }
    RequiredModules        = @(@{
            Guid          = '1d73a601-4a6c-43c5-ba3f-619b18bbb404'
            ModuleName    = 'powershellget'
            ModuleVersion = '2.2.5'
        }, @{
            Guid          = 'd6245802-193d-4068-a631-8863a4342a18'
            ModuleName    = 'PSScriptAnalyzer'
            ModuleVersion = '1.24.0'
        }, @{
            Guid          = 'a699dea5-2c73-4616-a270-1f7abb777e71'
            ModuleName    = 'Pester'
            ModuleVersion = '5.7.1'
        }, 'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')
    RootModule             = 'PSPublishModule.psm1'
}
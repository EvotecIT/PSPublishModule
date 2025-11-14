@{
    AliasesToExport        = @('New-PrepareModule', 'Build-Module', 'Invoke-ModuleBuilder')
    Author                 = 'Przemyslaw Klys'
    CmdletsToExport        = @('*')
    CompanyName            = 'Evotec'
    CompatiblePSEditions   = @('Desktop', 'Core')
    Copyright              = '(c) 2011 - 2025 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description            = 'Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery'
    DotNetFrameworkVersion = '4.5.2'
    FunctionsToExport      = @()
    GUID                   = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
    ModuleVersion          = '2.0.27'
    PowerShellVersion      = '5.1'
    PrivateData            = @{
        PSData = @{
            ExternalModuleDependencies = @('Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')
            IconUri                    = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
            ProjectUri                 = 'https://github.com/EvotecIT/PSPublishModule'
            RequireLicenseAcceptance   = $false
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
        }, @{
            Guid          = 'e4e0bda1-0703-44a5-b70d-8fe704cd0643'
            ModuleName    = 'Microsoft.PowerShell.PSResourceGet'
            ModuleVersion = '1.1.1'
        }, 'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')
    RootModule             = 'PSPublishModule.psm1'
    NestedModules          = @('PSPublishModule.dll')
}

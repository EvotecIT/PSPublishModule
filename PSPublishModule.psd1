@{
    AliasesToExport      = ''
    Author               = 'Przemyslaw Klys'
    CompanyName          = 'Evotec'
    CompatiblePSEditions = 'Desktop', 'Core'
    Copyright            = '(c) 2011 - 2020 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description          = 'Simple project allowing preparing, managing and publishing modules to PowerShellGallery'
    FunctionsToExport    = 'Get-GitLog', 'Get-MissingFunctions', 'Initialize-PortableScript', 'New-PrepareModule', 'Register-Certificate', 'Remove-Comments', 'Test-ScriptFile', 'Test-ScriptModule'
    GUID                 = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
    ModuleVersion        = '0.9.21'
    PowerShellVersion    = '5.1'
    PrivateData          = @{
        PSData = @{
            Tags                       = 'Windows', 'MacOS', 'Linux', 'Build', 'Module'
            ProjectUri                 = 'https://github.com/EvotecIT/PSPublishModule'
            ExternalModuleDependencies = 'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security'
            IconUri                    = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
        }
    }
    RequiredModules      = @{
        ModuleVersion = '0.14.0'
        ModuleName    = 'platyps'
        Guid          = '0bdcabef-a4b7-4a6d-bf7e-d879817ebbff'
    }, @{
        ModuleVersion = '2.2.1'
        ModuleName    = 'powershellget'
        Guid          = '1d73a601-4a6c-43c5-ba3f-619b18bbb404'
    }, @{
        ModuleVersion = '1.19.0'
        ModuleName    = 'PSScriptAnalyzer'
        Guid          = 'd6245802-193d-4068-a631-8863a4342a18'
    }, 'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security'
    RootModule           = 'PSPublishModule.psm1'
}
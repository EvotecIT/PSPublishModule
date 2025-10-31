﻿@{
    AliasesToExport      = @('Install-Documentation', 'Set-Documentation', 'Show-Documentation')
    Author               = 'Przemyslaw Klys'
    CmdletsToExport      = @('Get-ModuleDocumentation', 'Install-ModuleDocumentation', 'Set-ModuleDocumentation', 'Show-ModuleDocumentation')
    CompanyName          = 'Evotec'
    CompatiblePSEditions = @('Desktop', 'Core')
    Copyright            = '(c) 2011 - 2025 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description          = 'Simple project PowerGuardian'
    FunctionsToExport    = @()
    GUID                 = 'da587bcf-c954-402b-91d2-04ebd2bc2ea5'
    ModuleVersion        = '1.0.0'
    PowerShellVersion    = '5.1'
    PrivateData          = @{
        PSData = @{
            ProjectUri               = 'https://github.com/EvotecIT/PowerGuardian'
            RequireLicenseAcceptance = $false
            Tags                     = @('Windows', 'MacOS', 'Linux')
        }
    }
    RootModule           = 'PowerGuardian.psm1'
}
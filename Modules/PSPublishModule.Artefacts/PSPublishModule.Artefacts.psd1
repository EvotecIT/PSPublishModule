@{
    RootModule        = 'PSPublishModule.Artefacts.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = '4f6f72bb-f1ab-4eb9-95a2-38c349e4ac0f'
    Author            = 'Przemyslaw Klys'
    CompanyName       = 'Evotec'
    Copyright         = '(c) 2026 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description       = 'Offline artefact carrier for PSPublishModule-managed workstation prerequisites.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-PSPublishModuleArtefact'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            Tags       = @('PSPublishModule', 'PowerForge', 'AzureArtifacts', 'CredentialProvider', 'Offline')
            ProjectUri = 'https://github.com/EvotecIT/PSPublishModule'
            LicenseUri = 'https://github.com/EvotecIT/PSPublishModule/blob/main/LICENSE'
        }
    }
}

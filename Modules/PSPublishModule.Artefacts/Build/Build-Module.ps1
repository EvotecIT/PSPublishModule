[CmdletBinding()]
param(
    [Alias('ConfigurationGateMode')]
    [ValidateSet('Manifest', 'Build', 'Publish')]
    [string] $RunMode = 'Build',
    [string] $CredentialProviderVersion = '2.0.2',
    [switch] $SkipDownload,
    [string] $ModuleVersion = '1.0.X',
    [string] $PreReleaseTag,
    [string] $PowerShellGalleryApiKeyPath = 'C:\Support\Important\PowerShellGalleryAPI.txt'
)

Import-Module PSPublishModule -Force -ErrorAction Stop

Build-Module -ModuleName 'PSPublishModule.Artefacts' {
    $manifest = [ordered] @{
        ModuleVersion        = $ModuleVersion
        CompatiblePSEditions = @('Desktop', 'Core')
        GUID                 = '4f6f72bb-f1ab-4eb9-95a2-38c349e4ac0f'
        Author               = 'Przemyslaw Klys'
        CompanyName          = 'Evotec'
        Copyright            = "(c) 2026 - $((Get-Date).Year) Przemyslaw Klys @ Evotec. All rights reserved."
        Description          = 'Offline artefact carrier for PSPublishModule-managed workstation prerequisites.'
        PowerShellVersion    = '5.1'
        Tags                 = @('PSPublishModule', 'PowerForge', 'AzureArtifacts', 'CredentialProvider', 'Offline')
        ProjectUri           = 'https://github.com/EvotecIT/PSPublishModule'
        LicenseUri           = 'https://github.com/EvotecIT/PSPublishModule/blob/main/LICENSE'
        Prerelease           = $PreReleaseTag
    }

    $credentialProviderBaseUri = "https://github.com/microsoft/artifacts-credprovider/releases/download/v$CredentialProviderVersion"
    $credentialProviderFiles = @(
        New-ConfigurationExternalAssetFile -Runtime 'netcore' -Architecture 'x64' -FileName 'Microsoft.win-x64.NuGet.CredentialProvider.zip' -Uri "$credentialProviderBaseUri/Microsoft.win-x64.NuGet.CredentialProvider.zip"
        New-ConfigurationExternalAssetFile -Runtime 'netcore' -Architecture 'x86' -FileName 'Microsoft.win-x86.NuGet.CredentialProvider.zip' -Uri "$credentialProviderBaseUri/Microsoft.win-x86.NuGet.CredentialProvider.zip"
        New-ConfigurationExternalAssetFile -Runtime 'netcore' -Architecture 'arm64' -FileName 'Microsoft.win-arm64.NuGet.CredentialProvider.zip' -Uri "$credentialProviderBaseUri/Microsoft.win-arm64.NuGet.CredentialProvider.zip"
        New-ConfigurationExternalAssetFile -Runtime 'netfx' -FileName 'Microsoft.NetFx48.NuGet.CredentialProvider.zip' -Uri "$credentialProviderBaseUri/Microsoft.NetFx48.NuGet.CredentialProvider.zip"
    )

    New-ConfigurationManifest @manifest
    New-ConfigurationExternalAsset `
        -Name 'AzureArtifactsCredentialProvider' `
        -Version $CredentialProviderVersion `
        -OutputPath 'Artefacts\AzureArtifactsCredentialProvider' `
        -Source "https://github.com/microsoft/artifacts-credprovider/releases/tag/v$CredentialProviderVersion" `
        -License 'MIT' `
        -SkipDownload:$SkipDownload `
        -Files $credentialProviderFiles
    New-ConfigurationInformation -IncludeAll 'Artefacts'
    New-ConfigurationBuild -Enable -MergeModuleOnBuild -InstallMissingModules:$false
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'DefaultPSM1', 'OnMergePSD1', 'OnMergePSM1' -EnableFormatting -Sort None
    New-ConfigurationArtefact -Type Packed -Enable -Path '..\..\Artefacts\Packed' -IncludeTagName -ArtefactName 'PSPublishModule.Artefacts.<TagModuleVersionWithPreRelease>.zip'
    New-ConfigurationPublish -Type PowerShellGallery -FilePath $PowerShellGalleryApiKeyPath -Enabled:$false
    New-ConfigurationGate -Mode $RunMode
} -ExitCode

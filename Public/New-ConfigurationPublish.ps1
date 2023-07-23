function New-ConfigurationPublish {
    <#
    .SYNOPSIS
    Provide a way to configure publishing to PowerShell Gallery or GitHub

    .DESCRIPTION
    Provide a way to configure publishing to PowerShell Gallery or GitHub
    You can configure publishing to both at the same time
    You can publish to multiple PowerShellGalleries at the same time as well
    You can have multiple GitHub configurations at the same time as well

    .PARAMETER Type
    Choose between PowerShellGallery and GitHub

    .PARAMETER FilePath
    API Key to be used for publishing to GitHub or PowerShell Gallery in clear text in file

    .PARAMETER UserName
    When used for GitHub this parameter is required to know to which repository to publish.
    This parameter is not used for PSGallery publishing

    .PARAMETER RepositoryName
    When used for PowerShellGallery publishing this parameter provides a way to overwrite default PowerShellGallery and publish to a different repository
    When not used, the default PSGallery will be used.
    When used for GitHub publishing this parameter provides a way to overwrite default repository name and publish to a different repository
    When not used, the default repository name will be used, that matches the module name

    .PARAMETER ApiKey
    API Key to be used for publishing to GitHub or PowerShell Gallery in clear text

    .PARAMETER Enabled
    Enable publishing to GitHub or PowerShell Gallery

    .PARAMETER PreReleaseTag
    Allow to publish to GitHub as pre-release. By default it will be published as release

    .PARAMETER OverwriteTagName
    Allow to overwrite tag name when publishing to GitHub. By default "v<ModuleVersion>" will be used.

    .PARAMETER Force
    Allow to publish lower version of module on PowerShell Gallery. By default it will fail if module with higher version already exists.

    .PARAMETER ID
    Optional ID of the artefact. If not specified, the default packed artefact will be used.
    If no packed artefact is specified, the first packed artefact will be used (if enabled)
    If no packed artefact is enabled, the publishing will fail

    .EXAMPLE
    New-ConfigurationPublish -Type PowerShellGallery -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' -Enabled:$true

    .EXAMPLE
    New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHub'

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ParameterSetName = 'ApiKey')]
        [Parameter(Mandatory, ParameterSetName = 'ApiFromFile')]
        [ValidateSet('PowerShellGallery', 'GitHub')][string] $Type,

        [Parameter(Mandatory, ParameterSetName = 'ApiFromFile')][string] $FilePath,

        [Parameter(Mandatory, ParameterSetName = 'ApiKey')][string] $ApiKey,

        [Parameter(ParameterSetName = 'ApiKey')]
        [Parameter(ParameterSetName = 'ApiFromFile')]
        [string] $UserName,

        [Parameter(ParameterSetName = 'ApiKey')]
        [Parameter(ParameterSetName = 'ApiFromFile')]
        [string] $RepositoryName,

        [Parameter(ParameterSetName = 'ApiKey')]
        [Parameter(ParameterSetName = 'ApiFromFile')]
        [switch] $Enabled,

        [Parameter(ParameterSetName = 'ApiKey')]
        [Parameter(ParameterSetName = 'ApiFromFile')]
        [string] $PreReleaseTag,

        [Parameter(ParameterSetName = 'ApiKey')]
        [Parameter(ParameterSetName = 'ApiFromFile')]
        [string] $OverwriteTagName,

        [Parameter(ParameterSetName = 'ApiKey')]
        [Parameter(ParameterSetName = 'ApiFromFile')]
        [switch] $Force,

        [Parameter(ParameterSetName = 'ApiKey')]
        [Parameter(ParameterSetName = 'ApiFromFile')]
        [string] $ID
    )


    if ($FilePath) {
        $ApiKeyToUse = Get-Content -Path $FilePath -ErrorAction Stop -Encoding UTF8
    } else {
        $ApiKeyToUse = $ApiKey
    }

    if ($Type -eq 'PowerShellGallery') {
        $TypeToUse = 'GalleryNuget'
    } elseif ($Type -eq 'GitHub') {
        $TypeToUse = 'GitHubNuget'
        if (-not $UserName) {
            throw 'UserName is required for GitHub. Please fix New-ConfigurationPublish and provide UserName'
        }
    } else {
        return
    }

    $Settings = [ordered] @{
        Type          = $TypeToUse
        Configuration = [ordered] @{
            Type             = $Type
            ApiKey           = $ApiKeyToUse
            ID               = $ID
            Enabled          = $Enabled
            UserName         = $UserName
            RepositoryName   = $RepositoryName
            Force            = $Force.IsPresent
            OverwriteTagName = $OverwriteTagName
            PreReleaseTag    = $PreReleaseTag
            Verbose          = $VerbosePreference
        }
    }
    Remove-EmptyValue -Hashtable $Settings -Recursive 2
    $Settings
}
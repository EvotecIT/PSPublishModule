function New-ConfigurationPublish {
    [CmdletBinding()]
    param(
        [ValidateSet('PowerShellGallery', 'GitHub')]
        [string] $Type,

        [string] $FilePath,

        [string] $UserName,

        [string] $RepositoryName,

        [string] $ApiKey
    )
    $Options = [ordered] @{}
    if ($Type -eq 'PowerShellGallery') {
        $Options['PowerShellGallery'] = [ordered] @{}
        if ($ApiKey) {
            $Options.PowerShellGallery.ApiKey = $ApiKey
            $Options.PowerShellGallery.FromFile = $false
        } else {
            $Options.PowerShellGallery.ApiKey = $FilePath
            $Options.PowerShellGallery.FromFile = $true
        }
    } elseif ($Type -eq 'GitHub') {
        $Options['GitHub'] = [ordered] @{}
        if ($ApiKey) {
            $Options.GitHub.ApiKey = $ApiKey
            $Options.GitHub.FromFile = $false
        } else {
            $Options.GitHub.ApiKey = $FilePath
            $Options.GitHub.FromFile = $true
        }
        if (-not $UserName) {
            throw 'UserName is required for GitHub. Please fix New-ConfigurationPublish'
        }
        $Options.GitHub.UserName = $UserName
        if ($RepositoryName) {
            $Options.GitHub.RepositoryName = $RepositoryName
        }
    }
    [ordered] @{
        Type          = $Type
        Configuration = $Options
    }
}
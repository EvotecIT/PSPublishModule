function New-ConfigurationPublish {
    [CmdletBinding()]
    param(
        [ValidateSet('PowerShellGallery', 'GitHub')]
        [string] $Type,
        [string] $FilePath,
        [string] $UserName,
        [string] $RepositoryName,
        [string] $ApiKey,
        [switch] $Enabled,
        [string] $PreReleaseTag,
        [switch] $Force
    )
    $Options = [ordered] @{}
    $Skip = $false
    if ($Type -eq 'PowerShellGallery') {
        $Options['PowerShellGallery'] = [ordered] @{}
        if ($ApiKey) {
            $Options.PowerShellGallery.ApiKey = $ApiKey
            $Options.PowerShellGallery.FromFile = $false
        } elseif ($FilePath) {
            $Options.PowerShellGallery.ApiKey = $FilePath
            $Options.PowerShellGallery.FromFile = $true
        } else {
            $Skip = $true
        }
        if (-not $Skip) {
            [ordered] @{
                Type          = $Type
                Configuration = $Options
            }
        }
    } elseif ($Type -eq 'GitHub') {
        $Options['GitHub'] = [ordered] @{}
        if ($ApiKey) {
            $Options.GitHub.ApiKey = $ApiKey
            $Options.GitHub.FromFile = $false
        } elseif ($FilePath) {
            $Options.GitHub.ApiKey = $FilePath
            $Options.GitHub.FromFile = $true
        } else {
            $Skip = $true
        }
        # if user did try to set API KEY we would expect other stuff to be set
        # otherwise lets skip it because maybe user wanted something else
        if (-not $Skip) {
            if (-not $UserName) {
                throw 'UserName is required for GitHub. Please fix New-ConfigurationPublish'
            }
            $Options.GitHub.UserName = $UserName
            if ($RepositoryName) {
                $Options.GitHub.RepositoryName = $RepositoryName
            }
            [ordered] @{
                Type          = $Type
                Configuration = $Options
            }
        }
    }



    if ($Type -eq 'PowerShellGallery') {
        if ($Enabled) {
            [ordered] @{
                Type          = 'PowerShellGalleryPublishing'
                PublishModule = [ordered] @{
                    Enabled = $true
                }
            }
        }
        if ($VerbosePreference) {
            [ordered] @{
                Type          = 'PowerShellGalleryPublishing'
                PublishModule = [ordered] @{
                    PSGalleryVerbose = $true
                }
            }
        }
        if ($PreReleaseTag) {
            [ordered] @{
                Type          = 'PowerShellGalleryPublishing'
                PublishModule = [ordered] @{
                    PreRelease = $PreReleaseTag
                }
            }
        }
        if ($Force) {
            [ordered] @{
                Type          = 'PowerShellGalleryPublishing'
                PublishModule = [ordered] @{
                    RequireForce = $Force.IsPresent
                }
            }
        }
    } else {
        if ($Enabled) {
            [ordered] @{
                Type          = 'GitHubPublishing'
                PublishModule = [ordered] @{
                    GitHub = $true
                }
            }
        }
        if ($VerbosePreference) {
            [ordered] @{
                Type          = 'PowerShellGalleryPublishing'
                PublishModule = [ordered] @{
                    GitHubVerbose = $true
                }
            }
        }
    }
}
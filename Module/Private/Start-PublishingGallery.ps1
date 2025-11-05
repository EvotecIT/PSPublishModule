function Start-PublishingGallery {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [System.Collections.IDictionary] $ChosenNuget
    )

    if ($ChosenNuget) {
        $Repository = if ($ChosenNuget.RepositoryName) { $ChosenNuget.RepositoryName } else { 'PSGallery' }
        Write-TextWithTime -Text "Publishing Module to Gallery ($Repository)" {
            if ($ChosenNuget.ApiKey) {
                $publishModuleSplat = @{
                    Name        = $Configuration.Information.ModuleName
                    Repository  = $Repository
                    NuGetApiKey = $ChosenNuget.ApiKey
                    Force       = $ChosenNuget.Force
                    Verbose     = $ChosenNuget.Verbose
                    ErrorAction = 'Stop'
                }
                Publish-Module @publishModuleSplat
            } else {
                return $false
            }
        } -PreAppend Plus
    } elseif ($Configuration.Steps.PublishModule.Enabled) {
        # old way
        Write-TextWithTime -Text "Publishing Module to PowerShellGallery" {
            if ($Configuration.Options.PowerShellGallery.FromFile) {
                $ApiKey = Get-Content -Path $Configuration.Options.PowerShellGallery.ApiKey -ErrorAction Stop -Encoding UTF8
            } else {
                $ApiKey = $Configuration.Options.PowerShellGallery.ApiKey
            }
            $publishModuleSplat = @{
                Name        = $Configuration.Information.ModuleName
                Repository  = 'PSGallery'
                NuGetApiKey = $ApiKey
                Force       = $Configuration.Steps.PublishModule.RequireForce
                Verbose     = if ($Configuration.Steps.PublishModule.PSGalleryVerbose) { $Configuration.Steps.PublishModule.PSGalleryVerbose } else { $false }
                ErrorAction = 'Stop'
            }
            Publish-Module @publishModuleSplat
        } -PreAppend Plus
    }
}
function Start-PublishingGallery {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration
    )
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
function Start-PublishingModule {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration
    )
    Write-TextWithTime -Text "[+] Publishing Module to PowerShellGallery" {
        try {
            if ($Configuration.Options.PowerShellGallery.FromFile) {
                $ApiKey = Get-Content -Path $Configuration.Options.PowerShellGallery.ApiKey -ErrorAction Stop -Encoding UTF8
                #New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $ApiKey -RequireForce $Configuration.Steps.PublishModule.RequireForce
                Publish-Module -Name $Configuration.Information.ModuleName -Repository PSGallery -NuGetApiKey $ApiKey -Force:$Configuration.Steps.PublishModule.RequireForce -Verbose -ErrorAction Stop
            } else {
                #New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $Configuration.Options.PowerShellGallery.ApiKey -RequireForce $Configuration.Steps.PublishModule.RequireForce
                Publish-Module -Name $Configuration.Information.ModuleName -Repository PSGallery -NuGetApiKey $Configuration.Options.PowerShellGallery.ApiKey -Force:$Configuration.Steps.PublishModule.RequireForce -Verbose -ErrorAction Stop
            }
        } catch {
            $ErrorMessage = $_.Exception.Message
            Write-Host # This is to add new line, because the first line was opened up.
            Write-Text "[-] Publishing Module - failed. Error: $ErrorMessage" -Color Red
            return $false
        }
    }
}
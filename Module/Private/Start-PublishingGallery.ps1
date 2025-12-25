function Start-PublishingGallery {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [System.Collections.IDictionary] $ChosenNuget,
        [string] $ModulePath
    )

    if ($ChosenNuget) {
        $Repository = if ($ChosenNuget.RepositoryName) { $ChosenNuget.RepositoryName } else { 'PSGallery' }
        Write-TextWithTime -Text "Publishing Module to Gallery ($Repository)" {
            if ($ChosenNuget.ApiKey) {
                # Prefer PSResourceGet out-of-process publishing (no direct cmdlets exposed).
                if ($ModulePath -and (Test-Path -LiteralPath $ModulePath) -and -not $ChosenNuget.Force) {
                    try {
                        [void][PowerForge.BuildServices]::PublishPSResource(
                            ([string]$ModulePath),
                            ([string]$Repository),
                            ([string]$ChosenNuget.ApiKey),
                            $false,
                            $null,
                            $false,
                            $false,
                            600
                        )
                    } catch {
                        Write-Text "[-] PSResourceGet publishing failed: $($_.Exception.Message)" -Color Red
                        return $false
                    }
                } else {
                    # Fallback to legacy PowerShellGet publishing when -Force is requested.
                    $publishModuleSplat = @{
                        Name        = $Configuration.Information.ModuleName
                        Repository  = $Repository
                        NuGetApiKey = $ChosenNuget.ApiKey
                        Force       = $ChosenNuget.Force
                        Verbose     = $ChosenNuget.Verbose
                        ErrorAction = 'Stop'
                    }
                    Publish-Module @publishModuleSplat
                }
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
            # Prefer PSResourceGet out-of-process publishing (no direct cmdlets exposed).
            if ($ModulePath -and (Test-Path -LiteralPath $ModulePath) -and -not $Configuration.Steps.PublishModule.RequireForce) {
                try {
                    [void][PowerForge.BuildServices]::PublishPSResource(
                        ([string]$ModulePath),
                        'PSGallery',
                        ([string]$ApiKey),
                        $false,
                        $null,
                        $false,
                        $false,
                        600
                    )
                } catch {
                    Write-Text "[-] PSResourceGet publishing failed: $($_.Exception.Message)" -Color Red
                    return $false
                }
            } else {
                # Fallback to legacy PowerShellGet publishing when -Force is requested.
                $publishModuleSplat = @{
                    Name        = $Configuration.Information.ModuleName       
                    Repository  = 'PSGallery'
                    NuGetApiKey = $ApiKey
                    Force       = $Configuration.Steps.PublishModule.RequireForce
                    Verbose     = if ($Configuration.Steps.PublishModule.PSGalleryVerbose) { $Configuration.Steps.PublishModule.PSGalleryVerbose } else { $false }
                    ErrorAction = 'Stop'
                }
                Publish-Module @publishModuleSplat
            }
        } -PreAppend Plus
    }
}

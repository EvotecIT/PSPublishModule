function New-PersonalManifest {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $ManifestPath,
        [switch] $AddScriptsToProcess,
        [switch] $AddUsingsToProcess,
        [string] $ScriptsToProcessLibrary
    )

    $TemporaryManifest = @{ }
    $Manifest = $Configuration.Information.Manifest
    $Manifest.Path = $ManifestPath

    if (-not $AddScriptsToProcess) {
        $Manifest.ScriptsToProcess = @()
    }
    if ($AddUsingsToProcess -and $Configuration.UsingInPlace -and -not $ScriptsToProcessLibrary) {
        $Manifest.ScriptsToProcess = @($Configuration.UsingInPlace)
    } elseif ($AddUsingsToProcess -and $Configuration.UsingInPlace -and $ScriptsToProcessLibrary) {
        $Manifest.ScriptsToProcess = @($Configuration.UsingInPlace, $ScriptsToProcessLibrary)
    } elseif ($ScriptsToProcessLibrary) {
        $Manifest.ScriptsToProcess = @($ScriptsToProcessLibrary)
    }

    if ($Manifest.Contains('ExternalModuleDependencies')) {
        $TemporaryManifest.ExternalModuleDependencies = $Manifest.ExternalModuleDependencies
        $Manifest.Remove('ExternalModuleDependencies')
    }


    if ($Manifest.Contains('RequiredModules')) {
        foreach ($SubModule in $Manifest.RequiredModules) {
            if ($SubModule.ModuleVersion -eq 'Latest') {
                [Array] $AvailableModule = Get-Module -ListAvailable $SubModule.ModuleName
                $SubModule.ModuleVersion = $AvailableModule[0].Version
            }
        }
    }
    New-ModuleManifest @Manifest

    if ($Configuration.Steps.PublishModule.Prerelease -ne '' -or $TemporaryManifest.ExternalModuleDependencies) {
        #$FilePathPSD1 = Get-Item -Path $Configuration.Information.Manifest.Path
        $Data = Import-PowerShellDataFile -Path $Configuration.Information.Manifest.Path
        if ($Data.ScriptsToProcess.Count -eq 0) {
            $Data.Remove('ScriptsToProcess')
        }
        if ($Data.CmdletsToExport.Count -eq 0) {
            $Data.Remove('CmdletsToExport')
        }

        if ($Configuration.Steps.PublishModule.Prerelease) {
            $Data.PrivateData.PSData.Prerelease = $Configuration.Steps.PublishModule.Prerelease
        }
        if ($TemporaryManifest.ExternalModuleDependencies) {
            # Add External Module Dependencies
            $Data.PrivateData.PSData.ExternalModuleDependencies = $TemporaryManifest.ExternalModuleDependencies
            # Make sure Required Modules contains ExternalModuleDependencies
            $Data.RequiredModules = @(
                foreach ($Module in $Manifest.RequiredModules) {
                    $Module
                }
                foreach ($Module in $TemporaryManifest.ExternalModuleDependencies) {
                    $Module
                }
            )
        }
        $Data | Export-PSData -DataFile $Configuration.Information.Manifest.Path -Sort
    }
    Write-TextWithTime -Text "[+] Converting $($Configuration.Information.Manifest.Path) UTF8 without BOM" {
        (Get-Content $Manifest.Path) | Out-FileUtf8NoBom $Manifest.Path
    }
}
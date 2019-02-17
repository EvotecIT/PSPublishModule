function New-PersonalManifest {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $ManifestPath,
        [switch] $AddScriptsToProcess
    )

    $Manifest = $Configuration.Information.Manifest
    $Manifest.Path = $ManifestPath

    if (-not $AddScriptsToProcess) {
        $Manifest.ScriptsToProcess = @()
    }


    New-ModuleManifest @Manifest
    #Update-ModuleManifest @Manifest

    if ($Configuration.Information.Versioning.Prerelease -ne '') {
        #$FilePathPSD1 = Get-Item -Path $Configuration.Information.Manifest.Path
        $Data = Import-PowerShellDataFile -Path $Configuration.Information.Manifest.Path
        $Data.PrivateData.PSData.Prerelease = $Configuration.Information.Versioning.Prerelease
        $Data | Export-PSData -DataFile $Configuration.Information.Manifest.Path

    }

    Write-Verbose "Converting $($Configuration.Manifest.Path)"
    (Get-Content $Manifest.Path) | Out-FileUtf8NoBom $Manifest.Path
}
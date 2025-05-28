function Get-CurrentVersionFromPsd1 {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ManifestFile
    )

    if (!(Test-Path -Path $ManifestFile)) {
        Write-Warning "Module manifest file not found: $ManifestFile"
        return $null
    }

    try {
        $manifest = Import-PowerShellDataFile -Path $ManifestFile
        return $manifest.ModuleVersion
    } catch {
        Write-Warning "Error reading module manifest $ManifestFile`: $_"
        return $null
    }
}
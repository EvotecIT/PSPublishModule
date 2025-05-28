function Get-CurrentVersionFromCsProj {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile
    )

    if (!(Test-Path -Path $ProjectFile)) {
        Write-Warning "Project file not found: $ProjectFile"
        return $null
    }

    try {
        $content = Get-Content -Path $ProjectFile -Raw
        if ($content -match '<VersionPrefix>([\d\.]+)<\/VersionPrefix>') {
            return $matches[1]
        }
        return $null
    } catch {
        Write-Warning "Error reading project file $ProjectFile`: $_"
        return $null
    }
}
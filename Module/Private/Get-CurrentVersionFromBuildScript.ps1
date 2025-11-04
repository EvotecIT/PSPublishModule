function Get-CurrentVersionFromBuildScript {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ScriptFile
    )

    if (!(Test-Path -Path $ScriptFile)) {
        Write-Warning "Build script file not found: $ScriptFile"
        return $null
    }

    try {
        $content = Get-Content -Path $ScriptFile -Raw
        if ($content -match 'ModuleVersion\s*=\s*[''"\"]?([\d\.]+)[''"\"]?') {
            return $matches[1]
        }
        return $null
    } catch {
        Write-Warning "Error reading build script $ScriptFile`: $_"
        return $null
    }
}
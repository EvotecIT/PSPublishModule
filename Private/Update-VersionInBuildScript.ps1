function Update-VersionInBuildScript {
    <#
    .SYNOPSIS
    Updates the version in the Build-Module.ps1 script.

    .DESCRIPTION
    Modifies the ModuleVersion entry in the Build-Module.ps1 script with the new version.

    .PARAMETER ScriptFile
    Path to the Build-Module.ps1 file.

    .PARAMETER Version
    The new version string to set.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ScriptFile,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if (!(Test-Path -Path $ScriptFile)) {
        Write-Warning "Build script file not found: $ScriptFile"
        return $false
    }

    try {
        $content = Get-Content -Path $ScriptFile -Raw
        $newContent = $content -replace "ModuleVersion\s*=\s*['""][\d\.]+['""]", "ModuleVersion        = '$Version'"

        if ($content -eq $newContent) {
            Write-Verbose "No version change needed for $ScriptFile"
            return $true
        }
        Write-Verbose -Message "Updating version in $ScriptFile to $Version"

        if ($PSCmdlet.ShouldProcess("Build script $ScriptFile", "Update version to $Version")) {
            $newContent | Set-Content -Path $ScriptFile -NoNewline
            Write-Host "Updated version in $ScriptFile to $Version" -ForegroundColor Green
        }
        return $true
    } catch {
        Write-Error "Error updating build script $ScriptFile`: $_"
        return $false
    }
}

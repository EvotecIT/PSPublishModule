function Update-VersionInCsProj {
    <#
    .SYNOPSIS
    Updates the version in a .csproj file.

    .DESCRIPTION
    Updates VersionPrefix, AssemblyVersion, and FileVersion elements in a .csproj file with the new version (when present).

    .PARAMETER ProjectFile
    Path to the .csproj file.

    .PARAMETER Version
    The new version string to set.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [System.Collections.IDictionary] $CurrentVersionHash
    )

    if (!(Test-Path -Path $ProjectFile)) {
        Write-Warning "Project file not found: $ProjectFile"
        return $false
    }

    $CurrentFileVersion = $CurrentVersionHash[$ProjectFile]

    try {
        $content = Get-Content -Path $ProjectFile -Raw
        $newContent = $content
        # Update VersionPrefix, AssemblyVersion and FileVersion if present
        $newContent = $newContent -replace '<VersionPrefix>[\d\.]+<\/VersionPrefix>', "<VersionPrefix>$Version</VersionPrefix>"
        $newContent = $newContent -replace '<AssemblyVersion>[\d\.]+<\/AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>"
        $newContent = $newContent -replace '<FileVersion>[\d\.]+<\/FileVersion>', "<FileVersion>$Version</FileVersion>"

        if ($content -eq $newContent) {
            Write-Verbose "No version change needed for $ProjectFile"
            return $true
        }
        Write-Verbose -Message "Updating version in $ProjectFile from '$CurrentFileVersion' to '$Version'"
        if ($PSCmdlet.ShouldProcess("Project file $ProjectFile", "Update version from '$CurrentFileVersion' to '$Version'")) {
            $newContent | Set-Content -Path $ProjectFile -NoNewline
            Write-Host "Updated version in $ProjectFile to $Version" -ForegroundColor Green
        }
        return $true
    } catch {
        Write-Error "Error updating project file $ProjectFile`: $_"
        return $false
    }
}

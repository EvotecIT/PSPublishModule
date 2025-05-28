function Update-VersionInCsProj {
    <#
    .SYNOPSIS
    Updates the version in a .csproj file.

    .DESCRIPTION
    Modifies the VersionPrefix element in a .csproj file with the new version.

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
        [string]$Version
    )

    if (!(Test-Path -Path $ProjectFile)) {
        Write-Warning "Project file not found: $ProjectFile"
        return $false
    }

    try {
        $content = Get-Content -Path $ProjectFile -Raw
        $newContent = $content -replace '<VersionPrefix>[\d\.]+<\/VersionPrefix>', "<VersionPrefix>$Version</VersionPrefix>"

        if ($content -eq $newContent) {
            Write-Verbose "No version change needed for $ProjectFile"
            return $true
        }
        Write-Verbose -Message "Updating version in $ProjectFile to $Version"
        if ($PSCmdlet.ShouldProcess("Project file $ProjectFile", "Update version to $Version")) {
            $newContent | Set-Content -Path $ProjectFile -NoNewline
            Write-Host "Updated version in $ProjectFile to $Version" -ForegroundColor Green
        }
        return $true
    } catch {
        Write-Error "Error updating project file $ProjectFile`: $_"
        return $false
    }
}
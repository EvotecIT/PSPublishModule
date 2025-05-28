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

    .PARAMETER DryRun
        If specified, shows what would be changed without making actual changes.
    #>
    param (
        [Parameter(Mandatory = $true)]
        [string]$ProjectFile,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [switch]$DryRun
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

        if (-not $DryRun) {
            $newContent | Set-Content -Path $ProjectFile -NoNewline
            Write-Host "Updated version in $ProjectFile to $Version" -ForegroundColor Green
        } else {
            Write-Host "[DRY RUN] Would update version in $ProjectFile to $Version" -ForegroundColor Yellow
        }
        return $true
    } catch {
        Write-Error "Error updating project file $ProjectFile`: $_"
        return $false
    }
}
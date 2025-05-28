function Update-VersionInPsd1 {
    <#
    .SYNOPSIS
        Updates the version in a PowerShell module manifest (.psd1) file.

    .DESCRIPTION
        Modifies the ModuleVersion entry in a module manifest with the new version.

    .PARAMETER ManifestFile
        Path to the .psd1 file.

    .PARAMETER Version
        The new version string to set.

    .PARAMETER DryRun
        If specified, shows what would be changed without making actual changes.
    #>
    param (
        [Parameter(Mandatory = $true)]
        [string]$ManifestFile,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [switch]$DryRun
    )

    if (!(Test-Path -Path $ManifestFile)) {
        Write-Warning "Module manifest file not found: $ManifestFile"
        return $false
    }

    try {
        # Read the content first to avoid Update-ModuleManifest mangling the format
        $content = Get-Content -Path $ManifestFile -Raw
        $newContent = $content -replace "ModuleVersion\s*=\s*['""][\d\.]+['""]", "ModuleVersion        = '$Version'"

        if ($content -eq $newContent) {
            Write-Verbose "No version change needed for $ManifestFile"
            return $true
        }

        if (-not $DryRun) {
            $newContent | Set-Content -Path $ManifestFile -NoNewline
            Write-Host "Updated version in $ManifestFile to $Version" -ForegroundColor Green
        } else {
            Write-Host "[DRY RUN] Would update version in $ManifestFile to $Version" -ForegroundColor Yellow
        }
        return $true
    } catch {
        Write-Error "Error updating module manifest $ManifestFile`: $_"
        return $false
    }
}
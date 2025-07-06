function Set-ProjectVersion {
    <#
    .SYNOPSIS
    Updates version numbers across multiple project files.

    .DESCRIPTION
    Updates version numbers in C# projects (.csproj), PowerShell modules (.psd1),
    and PowerShell build scripts that contain 'Invoke-ModuleBuild'. Can increment
    version components or set a specific version.

    .PARAMETER VersionType
    The type of version increment: Major, Minor, Build, or Revision.

    .PARAMETER NewVersion
    Specific version number to set (format: x.x.x or x.x.x.x).

    .PARAMETER ModuleName
    Optional module name to filter updates to specific projects/modules.

    .PARAMETER Path
    The root path to search for project files. Defaults to current location.

    .PARAMETER ExcludeFolders
    Array of folder names to exclude from the search (in addition to default 'obj' and 'bin').

    .PARAMETER PassThru
    Returns the update results when specified.

    .OUTPUTS
    PSCustomObject[]
    When PassThru is specified, returns update results for each modified file.

    .EXAMPLE
    Set-ProjectVersion -VersionType Minor
    Increments the minor version in all project files.

    .EXAMPLE
    Set-ProjectVersion -NewVersion "2.1.0" -ModuleName "MyModule"
    Sets the version to 2.1.0 for the specific module.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter()]
        [ValidateSet('Major', 'Minor', 'Build', 'Revision')]
        [string]$VersionType = '',
        [Parameter()]
        [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
        [string]$NewVersion = '',
        [Parameter()]
        [string]$ModuleName = '',
        [Parameter()]
        [string]$Path = (Get-Location).Path,
        [Parameter()]
        [string[]]$ExcludeFolders = @(),
        [switch] $PassThru
    )

    $RepoRoot = $Path
    $DefaultExcludes = @('obj', 'bin')
    $AllExcludes = $DefaultExcludes + $ExcludeFolders | Select-Object -Unique

    $CsprojFiles = Get-ChildItem -Path $RepoRoot -Filter "*.csproj" -Recurse | Where-Object {
        $file = $_
        ($AllExcludes.Count -eq 0 -or -not ($AllExcludes | Where-Object {
                $_ -and $_.Trim() -ne '' -and $file.FullName -and $file.FullName.ToLower().Contains($_.ToLower())
            }))
    }
    $PsdFiles = Get-ChildItem -Path $RepoRoot -Filter "*.psd1" -Recurse | Where-Object {
        $file = $_
        ($AllExcludes.Count -eq 0 -or -not ($AllExcludes | Where-Object {
                $_ -and $_.Trim() -ne '' -and $file.FullName -and $file.FullName.ToLower().Contains($_.ToLower())
            }))
    }
    # Find PowerShell scripts that contain Invoke-ModuleBuild or Build-Module
    $BuildScriptFiles = Get-ChildItem -Path $RepoRoot -Filter "*.ps1" -Recurse | Where-Object {
        $file = $_
        # First apply exclusion filter
        $isExcluded = ($AllExcludes.Count -gt 0 -and ($AllExcludes | Where-Object {
                    $_ -and $_.Trim() -ne '' -and $file.FullName -and $file.FullName.ToLower().Contains($_.ToLower())
                }))
        if ($isExcluded) {
            return $false
        }
        # Then check if file contains Invoke-ModuleBuild or Build-Module
        try {
            $content = Get-Content -Path $file.FullName -Raw -ErrorAction SilentlyContinue
            return $content -match 'Invoke-ModuleBuild|Build-Module'
        } catch {
            return $false
        }
    }

    # Filter csproj files by ModuleName if provided
    $targetCsprojFiles = $CsprojFiles
    if ($ModuleName) {
        $targetCsprojFiles = $CsprojFiles | Where-Object { $_.BaseName -eq $ModuleName }
    }

    # Filter psd1 files by ModuleName if provided
    $targetPsdFiles = $PsdFiles
    if ($ModuleName) {
        $targetPsdFiles = $PsdFiles | Where-Object { $_.BaseName -eq $ModuleName }
    }

    # Determine current version from the first available file that has a version
    $currentVersion = $null

    # Try to get version from csproj files
    foreach ($csProj in $targetCsprojFiles) {
        $version = Get-CurrentVersionFromCsProj -ProjectFile $csProj.FullName
        if ($version) {
            $currentVersion = $version
            break
        }
    }

    # If no version found in csproj files, try psd1 files
    if (-not $currentVersion) {
        foreach ($psd1 in $targetPsdFiles) {
            $version = Get-CurrentVersionFromPsd1 -ManifestFile $psd1.FullName
            if ($version) {
                $currentVersion = $version
                break
            }
        }
    }

    # If no version found in psd1 files, try build script files
    if (-not $currentVersion) {
        foreach ($buildScript in $BuildScriptFiles) {
            $version = Get-CurrentVersionFromBuildScript -ScriptFile $buildScript.FullName
            if ($version) {
                $currentVersion = $version
                break
            }
        }
    }

    if (-not $currentVersion) {
        Write-Error "Could not determine current version from any project files."
        return
    }
    if (-not [string]::IsNullOrWhiteSpace($NewVersion)) {
        $newVersion = $NewVersion
    } else {
        $newVersion = Update-VersionNumber -Version $currentVersion -Type $VersionType
    }

    $CurrentVersions = Get-ProjectVersion -Path $RepoRoot -ExcludeFolders $AllExcludes
    $CurrentVersionHash = @{}
    foreach ($C in $CurrentVersions) {
        $CurrentVersionHash[$C.Source] = $C.Version
    }
    $Output = @(
        foreach ($csProj in $targetCsprojFiles) {
            Update-VersionInCsProj -ProjectFile $csProj.FullName -Version $newVersion -WhatIf:$WhatIfPreference -CurrentVersionHash $CurrentVersionHash
        }
        foreach ($psd1 in $targetPsdFiles) {
            Update-VersionInPsd1 -ManifestFile $psd1.FullName -Version $newVersion -WhatIf:$WhatIfPreference -CurrentVersionHash $CurrentVersionHash
        }
        foreach ($buildScript in $BuildScriptFiles) {
            Update-VersionInBuildScript -ScriptFile $buildScript.FullName -Version $newVersion -WhatIf:$WhatIfPreference -CurrentVersionHash $CurrentVersionHash
        }
    )
    if ($PassThru) {
        $Output
    }
}
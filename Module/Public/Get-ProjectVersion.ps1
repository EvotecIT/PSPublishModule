function Get-ProjectVersion {
    <#
    .SYNOPSIS
    Retrieves project version information from various project files.

    .DESCRIPTION
    Scans the specified path for C# projects (.csproj), PowerShell modules (.psd1),
    and PowerShell build scripts that contain 'Invoke-ModuleBuild' to extract version information.

    .PARAMETER ModuleName
    Optional module name to filter results to specific projects/modules.

    .PARAMETER Path
    The root path to search for project files. Defaults to current location.

    .PARAMETER ExcludeFolders
    Array of folder names to exclude from the search (in addition to default 'obj' and 'bin').

    .OUTPUTS
    PSCustomObject[]
    Returns objects with Version, Source, and Type properties for each found project file.

    .EXAMPLE
    Get-ProjectVersion
    Gets version information from all project files in the current directory.

    .EXAMPLE
    Get-ProjectVersion -ModuleName "MyModule" -Path "C:\Projects"
    Gets version information for the specific module from the specified path.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$ModuleName,
        [Parameter()]
        [string]$Path = (Get-Location).Path,
        [Parameter()]
        [string[]]$ExcludeFolders = @()
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

    foreach ($csProj in $targetCsprojFiles) {
        $version = Get-CurrentVersionFromCsProj -ProjectFile $csProj.FullName
        if ($version) {
            [PSCustomObject]@{
                Version = $version
                Source  = $csProj.FullName
                Type    = "C# Project"
            }
        }
    }

    # Filter psd1 files by ModuleName if provided
    $targetPsdFiles = $PsdFiles
    if ($ModuleName) {
        $targetPsdFiles = $PsdFiles | Where-Object { $_.BaseName -eq $ModuleName }
    }

    foreach ($psd1 in $targetPsdFiles) {
        $version = Get-CurrentVersionFromPsd1 -ManifestFile $psd1.FullName
        if ($version) {
            [PSCustomObject]@{
                Version = $version
                Source  = $psd1.FullName
                Type    = "PowerShell Module"
            }
        }
    }

    foreach ($buildScript in $BuildScriptFiles) {
        $version = Get-CurrentVersionFromBuildScript -ScriptFile $buildScript.FullName
        if ($version) {
            [PSCustomObject]@{
                Version = $version
                Source  = $buildScript.FullName
                Type    = "Build Script"
            }
        }
    }
}
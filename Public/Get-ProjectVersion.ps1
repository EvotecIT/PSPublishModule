function Get-ProjectVersion {
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
    $DefaultExcludes = @('obj','bin')
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
    $BuildScriptFiles = Get-ChildItem -Path $RepoRoot -Filter "Build-Module.ps1" -Recurse | Where-Object {
        $file = $_
        ($AllExcludes.Count -eq 0 -or -not ($AllExcludes | Where-Object {
            $_ -and $_.Trim() -ne '' -and $file.FullName -and $file.FullName.ToLower().Contains($_.ToLower())
        }))
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
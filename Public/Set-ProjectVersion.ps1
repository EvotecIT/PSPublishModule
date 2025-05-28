function Set-ProjectVersion {
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

    # Filter psd1 files by ModuleName if provided
    $targetPsdFiles = $PsdFiles
    if ($ModuleName) {
        $targetPsdFiles = $PsdFiles | Where-Object { $_.BaseName -eq $ModuleName }
    }

    # Determine current version from the first available file
    $currentVersion = $null
    if ($targetCsprojFiles.Count -gt 0) {
        $currentVersion = Get-CurrentVersionFromCsProj -ProjectFile $targetCsprojFiles[0].FullName
    } elseif ($targetPsdFiles.Count -gt 0) {
        $currentVersion = Get-CurrentVersionFromPsd1 -ManifestFile $targetPsdFiles[0].FullName
    } elseif ($BuildScriptFiles.Count -gt 0) {
        $currentVersion = Get-CurrentVersionFromBuildScript -ScriptFile $BuildScriptFiles[0].FullName
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
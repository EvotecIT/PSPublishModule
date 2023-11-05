function Copy-ArtefactToScript {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $ModuleVersion,
        [switch] $Enabled,
        [nullable[bool]] $IncludeTagName,
        [string] $ModuleName,
        [string] $Destination,
        [string] $PreScriptMerge,
        [string] $PostScriptMerge,
        [string] $ScriptName
    )
    if (-not $Enabled) {
        return
    }

    if ($PSVersionTable.PSVersion.Major -gt 5) {
        $Encoding = 'UTF8BOM'
    } else {
        $Encoding = 'UTF8'
    }

    if ($IncludeTagName) {
        $NameOfDestination = [io.path]::Combine($Destination, $TagName)
    } else {
        $NameOfDestination = [io.path]::Combine($Destination)
    }
    $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($NameOfDestination)
    Write-TextWithTime -PreAppend Addition -Text "Copying main module to $ResolvedDestination" -Color Yellow {
        if (Test-Path -Path $NameOfDestination) {
            Remove-ItemAlternative -LiteralPath $NameOfDestination -ErrorAction Stop
        }
        $null = New-Item -ItemType Directory -Path $Destination -Force

        if ($DestinationPaths.Desktop) {
            $DestinationToUse = [System.IO.Path]::Combine($DestinationPaths.Desktop, "*")
            Copy-Item -Path $DestinationToUse -Recurse -Destination "$ResolvedDestination" -Force
        } elseif ($DestinationPaths.Core) {
            $DestinationToUse = [System.IO.Path]::Combine($DestinationPaths.Core, "*")
            Copy-Item -Path $DestinationToUse -Recurse -Destination "$ResolvedDestination" -Force
        }
    } -SpacesBefore '         '
    Write-TextWithTime -PreAppend Addition -Text "Cleaning up main module" -Color Yellow {
        $PSD1 = [io.path]::Combine($ResolvedDestination, "$ModuleName.psd1")
        Remove-Item -LiteralPath $PSD1 -Force -ErrorAction Stop
        $PSM1 = [io.path]::Combine($ResolvedDestination, "$ModuleName.psm1")
        if ($ScriptName) {
            # This is useful for replacing names in script
            $TagName = "v$($ModuleVersion)"
            if ($Configuration.CurrentSettings.PreRelease) {
                $ModuleVersionWithPreRelease = "$($ModuleVersion)-$($Configuration.CurrentSettings.PreRelease)"
                $TagModuleVersionWithPreRelease = "v$($ModuleVersionWithPreRelease)"
            } else {
                $ModuleVersionWithPreRelease = $ModuleVersion
                $TagModuleVersionWithPreRelease = "v$($ModuleVersion)"
            }

            $ScriptName = $ScriptName.Replace('{ModuleName}', $ModuleName)
            $ScriptName = $ScriptName.Replace('<ModuleName>', $ModuleName)
            $ScriptName = $ScriptName.Replace('{ModuleVersion}', $ModuleVersion)
            $ScriptName = $ScriptName.Replace('<ModuleVersion>', $ModuleVersion)
            $ScriptName = $ScriptName.Replace('{ModuleVersionWithPreRelease}', $ModuleVersionWithPreRelease)
            $ScriptName = $ScriptName.Replace('<ModuleVersionWithPreRelease>', $ModuleVersionWithPreRelease)
            $ScriptName = $ScriptName.Replace('{TagModuleVersionWithPreRelease}', $TagModuleVersionWithPreRelease)
            $ScriptName = $ScriptName.Replace('<TagModuleVersionWithPreRelease>', $TagModuleVersionWithPreRelease)
            $ScriptName = $ScriptName.Replace('{TagName}', $TagName)
            $ScriptName = $ScriptName.Replace('<TagName>', $TagName)

            if ($ScriptName.EndsWith(".ps1")) {
                $PS1 = [io.path]::Combine($ResolvedDestination, "$ScriptName")
                Rename-Item -LiteralPath $PSM1 -NewName "$ScriptName" -Force -ErrorAction Stop
                #Move-Item -LiteralPath $PSM1 -Destination $PS1 -Force -ErrorAction Stop
            } else {
                $PS1 = [io.path]::Combine($ResolvedDestination, "$ScriptName.ps1")
                Rename-Item -LiteralPath $PSM1 -NewName "$ScriptName.ps1" -Force -ErrorAction Stop
                #Move-Item -LiteralPath $PSM1 -Destination $PS1 -Force -ErrorAction Stop
            }
        } else {
            $PS1 = [io.path]::Combine($ResolvedDestination, "$ModuleName.ps1")
            #Move-Item -LiteralPath $PSM1 -Destination $PS1 -Force -ErrorAction Stop
            Rename-Item -LiteralPath $PSM1 -NewName "$ModuleName.ps1" -Force -ErrorAction Stop
            #$PS1 = [io.path]::Combine($ResolvedDestination, "$ModuleName.ps1")
        }
        $Content = Get-Content -LiteralPath $PS1 -ErrorAction Stop -Encoding UTF8

        # Find the index of the line that contains "# Export functions and aliases as required" starting from the bottom of the file
        $index = ($Content | Select-String -Pattern "# Export functions and aliases as required" -SimpleMatch | Select-Object -Last 1).LineNumber

        # Remove all lines below the index, including that line
        $Content = $Content[0..($index - 2)]

        if ($PreScriptMerge) {
            $Content = @(
                $PreScriptMerge.Trim()
                $Content
            )
        }
        if ($PostScriptMerge) {
            $Content = @(
                $Content
                $PostScriptMerge.Trim()
            )
        }

        # Output the updated content
        Set-Content -LiteralPath $PS1 -Value $Content -Force -ErrorAction Stop -Encoding $Encoding

    } -SpacesBefore '   '
}
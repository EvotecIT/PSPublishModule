function Copy-ArtefactToScript {
    [CmdletBinding()]
    param(
        [switch] $Enabled,
        [nullable[bool]] $IncludeTagName,
        [string] $ModuleName,
        [string] $Destination,
        [string] $ScriptMerge
    )
    if (-not $Enabled) {
        return
    }
    if ($IncludeTagName) {
        $NameOfDestination = [io.path]::Combine($Destination, $ModuleName, $TagName)
    } else {
        $NameOfDestination = [io.path]::Combine($Destination, $ModuleName)
    }
    $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($NameOfDestination)
    Write-TextWithTime -PreAppend Addition -Text "Copying main module to $ResolvedDestination" -Color Yellow {
        if (Test-Path -Path $NameOfDestination) {
            Remove-ItemAlternative -LiteralPath $NameOfDestination -ErrorAction Stop
        }
        $null = New-Item -ItemType Directory -Path $Destination -Force

        if ($DestinationPaths.Desktop) {
            Copy-Item -LiteralPath $DestinationPaths.Desktop -Recurse -Destination $ResolvedDestination -Force
        } elseif ($DestinationPaths.Core) {
            Copy-Item -LiteralPath $DestinationPaths.Core -Recurse -Destination $ResolvedDestination -Force
        }
    } -SpacesBefore '         '
    Write-TextWithTime -PreAppend Addition -Text "Cleaning up main module" -Color Yellow {
        $PSD1 = [io.path]::Combine($ResolvedDestination, "$ModuleName.psd1")
        Remove-Item -LiteralPath $PSD1 -Force -ErrorAction Stop
        $PSM1 = [io.path]::Combine($ResolvedDestination, "$ModuleName.psm1")
        Rename-Item -LiteralPath $PSM1 -NewName "$ModuleName.ps1" -Force -ErrorAction Stop
        $PS1 = [io.path]::Combine($ResolvedDestination, "$ModuleName.ps1")
        $Content = Get-Content -LiteralPath $PS1 -ErrorAction Stop

        # Find the index of the line that contains "# Export functions and aliases as required" starting from the bottom of the file
        $index = ($content | Select-String -Pattern "# Export functions and aliases as required" -SimpleMatch | Select-Object -Last 1).LineNumber

        # Remove all lines below the index, including that line
        $content = $content[0..($index - 2)]

        if ($ScriptMerge) {
            $content += $ScriptMerge
        }

        # Output the updated content
        Set-Content -LiteralPath $PS1 -Value $content -Force -ErrorAction Stop -Encoding UTF8

    } -SpacesBefore '   '
}
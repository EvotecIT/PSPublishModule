function New-PSMFile {
    [cmdletbinding()]
    param(
        [string] $Path,
        [string[]] $FunctionNames,
        [string[]] $FunctionAliaes,
        [Array] $LibrariesCore,
        [Array] $LibrariesDefault
    )
    if ($FunctionNames.Count -gt 0) {
        $Functions = ($FunctionNames | Sort-Object -Unique) -join "','"
        $Functions = "'$Functions'"
    } else {
        $Functions = @()
    }

    if ($FunctionAliaes.Count -gt 0) {
        $Aliases = ($FunctionAliaes | Sort-Object -Unique) -join "','"
        $Aliases = "'$Aliases'"
    } else {
        $Aliases = @()
    }
    "" | Add-Content -Path $Path

    if ($LibrariesCore.Count -gt 0 -and $LibrariesDefault.Count -gt 0) {

        'if ($PSEdition -eq ''Core'') {' | Add-Content -Path $Path
        foreach ($File in $LibrariesCore) {
            $Output = 'Add-Type -Path $PSScriptRoot\' + $File
            $Output | Add-Content -Path $Path
        }
        '} else {' | Add-Content -Path $Path
        foreach ($File in $LibrariesDefault) {
            $Output = 'Add-Type -Path $PSScriptRoot\' + $File
            $Output | Add-Content -Path $Path
        }
        '}' | Add-Content -Path $Path

    } elseif ($LibrariesCore.Count -gt 0) {
        foreach ($File in $LibrariesCore) {
            $Output = 'Add-Type -Path $PSScriptRoot\' + $File
            $Output | Add-Content -Path $Path
        }
    } elseif ($LibrariesDefault.Count -gt 0) {
        foreach ($File in $LibrariesDefault) {
            $Output = 'Add-Type -Path $PSScriptRoot\' + $File
            $Output | Add-Content -Path $Path
        }
    }

    @"

Export-ModuleMember ``
    -Function @($Functions) ``
    -Alias @($Aliases)
"@ | Add-Content -Path $Path

}
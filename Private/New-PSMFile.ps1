function New-PSMFile {
    param(
        [string] $Path,
        [string[]] $FunctionNames,
        [string[]] $FunctionAliaes,
        [Array] $LibrariesCore,
        [Array] $LibrariesDefault
    )
    $Functions = ($FunctionNames | Sort-Object -Unique) -join "','"
    $Aliases = ($FunctionAliaes | Sort-Object -Unique) -join "','"

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
    -Function @('$Functions') ``
    -Alias @('$Aliases')
"@ | Add-Content -Path $Path

}
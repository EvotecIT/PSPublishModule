function New-PSMFile {
    param(
        [string] $Path,
        [string[]] $FunctionNames,
        [string[]] $FunctionAliaes
    )
    $Functions = ($FunctionNames | Sort-Object -Unique) -join "','"
    $Aliases = ($FunctionAliaes | Sort-Object -Unique) -join "','"
@"

Export-ModuleMember ``
    -Function @('$Functions') ``
    -Alias @('$Aliases')
"@ | Add-Content -Path $Path

}
#$Path = 'C:\Support\GitHub\PSSharedGoods\Public\Objects\Format-Stream.ps1'
#Get-FunctionAliases -Path $path
function Get-FunctionAliases {
    param(
        [string] $Path
    )
    Import-Module $Path -Force

    $Names = Get-FunctionNames -Path $Path
    $Aliases = foreach ($Name in $Names) {
       Get-Alias | Where-Object {$_.Definition -eq $Name}
    }
    return $Aliases.Name
}

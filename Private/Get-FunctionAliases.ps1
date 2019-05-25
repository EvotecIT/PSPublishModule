#$Path = 'C:\Support\GitHub\PSSharedGoods\Public\Objects\Format-Stream.ps1'
#Get-FunctionAliases -Path $path
function Get-FunctionAliases {
    [cmdletbinding()]
    param(
        [string] $Path
    )
    Import-Module $Path -Force -Verbose:$False

    $Names = Get-FunctionNames -Path $Path
    $Aliases = foreach ($Name in $Names) {
        Get-Alias | Where-Object {$_.Definition -eq $Name}
    }
    #$MyAliases = foreach ($Alias in $Aliases) {
    #    if ($Alias -ne '') {
    #        $Alias.Name
    #    }
    #}
    return $Aliases
}

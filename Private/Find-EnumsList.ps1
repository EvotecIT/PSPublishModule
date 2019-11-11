function Find-EnumsList {
    [CmdletBinding()]
    param (
        [string] $ProjectPath
    )
    if ($PSEdition -eq 'Core') {
        $Enums = Get-ChildItem -Path $ProjectPath\Enums\*.ps1 -ErrorAction SilentlyContinue  -FollowSymlink
    } else {
        $Enums = Get-ChildItem -Path $ProjectPath\Enums\*.ps1 -ErrorAction SilentlyContinue
    }
    $EnumsList = @(
        $Files = Foreach ($import in @($Enums)) {
            "'Enums\$($import.Name)'"
        }
        $Files -join ','
    )
    return [string] "@($EnumsList)"
}
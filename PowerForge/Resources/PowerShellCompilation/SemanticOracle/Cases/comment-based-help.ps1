function Get-DocumentedValue {
    <#
    .SYNOPSIS
    Returns the minimized semantic value.
    #>
    42
}
$Help = Get-Help Get-DocumentedValue
if ($Help.Name -cne 'Get-DocumentedValue' -or $Help.Synopsis -cne 'Returns the minimized semantic value.') {
    return -1
}
Get-DocumentedValue

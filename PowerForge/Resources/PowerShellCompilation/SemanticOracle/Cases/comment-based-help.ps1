function Get-DocumentedValue {
    <#
    .SYNOPSIS
    Returns the minimized semantic value.
    #>
    42
}
$Help = Get-Help Get-DocumentedValue
if ($Help.Synopsis -notlike 'Returns the minimized semantic value.*') {
    throw 'The comment-based help synopsis was not discovered.'
}
Get-DocumentedValue

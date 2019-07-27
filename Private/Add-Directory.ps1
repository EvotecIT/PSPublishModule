function Add-Directory {
    [CmdletBinding()]
    param(
        $dir
    )
    $exists = Test-Path -Path $dir
    if ($exists -eq $false) {
        $null = mkdir $dir
    }
}
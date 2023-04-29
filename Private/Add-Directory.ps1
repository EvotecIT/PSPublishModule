function Add-Directory {
    [CmdletBinding()]
    param(
        [string] $Directory
    )
    $exists = Test-Path -Path $Directory
    if ($exists -eq $false) {
        $null = New-Item -Path $Directory -ItemType Directory -Force
    }
}
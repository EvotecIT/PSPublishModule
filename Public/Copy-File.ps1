function Copy-File {
    [CmdletBinding()]
    param (
        $Source,
        $Destination
    )
    if ((Test-Path $Source) -and !(Test-Path $Destination)) {
        Copy-Item -Path $Source -Destination $Destination
    }
}
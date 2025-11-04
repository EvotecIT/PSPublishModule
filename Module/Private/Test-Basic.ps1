function Test-BasicModule {
    [cmdletBinding()]
    param(
        [string] $Path,
        [string] $Type
    )
    if ($Type -contains 'Encoding') {
        Get-ChildItem -LiteralPath $Path -Recurse -Filter '*.ps1' | Get-FileEncoding
    }
}
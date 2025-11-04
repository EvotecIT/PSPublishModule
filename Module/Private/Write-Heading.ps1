function Write-Heading {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Text)
    Write-Host ('=' * $Text.Length) -ForegroundColor DarkGray
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ('=' * $Text.Length) -ForegroundColor DarkGray
}


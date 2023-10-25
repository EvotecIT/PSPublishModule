function Register-DataForInitialModule {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(Mandatory)][string] $ModuleName,
        [Parameter(Mandatory)][string] $Guid
    )

    if ($PSVersionTable.PSVersion.Major -gt 5) {
        $Encoding = 'UTF8BOM'
    } else {
        $Encoding = 'UTF8'
    }

    $BuildModule = Get-Content -Path $FilePath -Raw
    $BuildModule = $BuildModule -replace "\`$GUID", $Guid
    $BuildModule = $BuildModule -replace "\`$ModuleName", $ModuleName
    Set-Content -Path $FilePath -Value $BuildModule -Encoding $Encoding
}
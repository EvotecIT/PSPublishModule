function Register-DataForInitialModule {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(Mandatory)][string] $ModuleName,
        [Parameter(Mandatory)][string] $Guid
    )

    $BuildModule = Get-Content -Path $FilePath -Raw
    $BuildModule = $BuildModule -replace "\`$GUID", $Guid
    $BuildModule = $BuildModule -replace "\`$ModuleName", $ModuleName
    Set-Content -Path $FilePath -Value $BuildModule -Encoding utf8
}
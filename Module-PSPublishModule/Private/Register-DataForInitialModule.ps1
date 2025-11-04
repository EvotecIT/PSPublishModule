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
    try {
        $BuildModule = Get-Content -Path $FilePath -Raw -ErrorAction Stop
    } catch {
        Write-Text -Text "[-] Couldn't read $FilePath, error: $($_.Exception.Message)" -Color Red
        return $false
    }
    $BuildModule = $BuildModule -replace "\`$GUID", $Guid
    $BuildModule = $BuildModule -replace "\`$ModuleName", $ModuleName
    try {
        Set-Content -Path $FilePath -Value $BuildModule -Encoding $Encoding -ErrorAction Stop
    } catch {
        Write-Text -Text "[-] Couldn't save $FilePath, error: $($_.Exception.Message)" -Color Red
        return $false
    }
}
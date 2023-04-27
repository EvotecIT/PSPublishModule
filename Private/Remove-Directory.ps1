function Remove-Directory {
    [CmdletBinding()]
    param (
        [string] $Directory
    )
    if ($Directory) {
        $Exists = Test-Path -LiteralPath $Directory
        if ($Exists) {
            try {
                Remove-Item -Path $Directory -Confirm:$false -Recurse -Force -ErrorAction Stop
            } catch {
                $ErrorMessage = $_.Exception.Message
                Write-Text "[e] Can't delete folder $Directory. Fix error before continuing: $ErrorMessage" -Color Red
                return $false
            }
        }
    }
}
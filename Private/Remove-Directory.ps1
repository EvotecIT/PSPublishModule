function Remove-Directory {
    [CmdletBinding()]
    param (
        [string] $Directory,
        [string] $SpacesBefore
    )
    if ($Directory) {
        $Exists = Test-Path -LiteralPath $Directory
        if ($Exists) {
            try {
                Remove-Item -Path $Directory -ErrorAction Stop -Force -Recurse -Confirm:$false
            } catch {
                $ErrorMessage = $_.Exception.Message
                Write-Text "Can't delete folder $Directory. Fix error before continuing: $ErrorMessage" -PreAppend Error -SpacesBefore $SpacesBefore
                return $false
            }
        }
    }
}
function Remove-Directory {
    [CmdletBinding()]
    param (
        [string] $Directory
    )
    if ($Directory) {
        $exists = Test-Path -Path $Directory
        if ($exists) {
            #Write-Color 'Removing directory ', $dir -Color White, Yellow
            try {
                Remove-Item -Path $Directory -Confirm:$false -Recurse -Force -ErrorAction Stop
            } catch {
                $ErrorMessage = $_.Exception.Message
                Write-Text "[-] Can't delete folder $Directory. Fix error before contiuing: $ErrorMessage" -Color Red
                Exit
            }
        } else {
            #Write-Color 'Removing directory ', $dir, ' skipped.' -Color White, Yellow, Red
        }
    }
}
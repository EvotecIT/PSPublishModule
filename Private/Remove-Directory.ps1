function Remove-Directory {
    [CmdletBinding()]
    param (
        [string] $Directory
    )
    if ($Directory) {
        $exists = Test-Path -Path $Directory
        if ($exists) {
            #Write-Color 'Removing directory ', $dir -Color White, Yellow
            Remove-Item -Path $Directory -Confirm:$false -Recurse
        } else {
            #Write-Color 'Removing directory ', $dir, ' skipped.' -Color White, Yellow, Red
        }
    }
}
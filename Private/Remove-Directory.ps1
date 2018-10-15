function Remove-Directory {
    [CmdletBinding()]
    param (
        $dir
    )

    $exists = Test-Path $dir
    if ($exists) {
        #Write-Color 'Removing directory ', $dir -Color White, Yellow
        Remove-Item $dir -Confirm:$false -Recurse
    } else {
        #Write-Color 'Removing directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}
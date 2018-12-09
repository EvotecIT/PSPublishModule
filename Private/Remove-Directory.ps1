function Remove-Directory {
    [CmdletBinding()]
    param (
        [string] $Dir
    )
    if (-not [string]::IsNullOrWhiteSpace($Dir)) {
        $exists = Test-Path -Path $Dir
        if ($exists) {
            #Write-Color 'Removing directory ', $dir -Color White, Yellow
            Remove-Item $dir -Confirm:$false -Recurse
        } else {
            #Write-Color 'Removing directory ', $dir, ' skipped.' -Color White, Yellow, Red
        }
    }
}
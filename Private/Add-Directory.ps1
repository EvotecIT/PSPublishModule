function Add-Directory {
    [CmdletBinding()]
    param(
        $dir
    )

    $exists = Test-Path -Path $dir
    if ($exists -eq $false) {
        #Write-Color 'Creating directory ', $dir -Color White, Yellow
        $createdDirectory = mkdir $dir
    } else {
        #Write-Color 'Creating directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}
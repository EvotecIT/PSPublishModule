function Add-FilesWithFolders {
    [CmdletBinding()]
    param ($file, $FullProjectPath, $directory)
    $LinkPrivatePublicFiles = @()
    #$path = $file.FullName.Replace("$FullProjectPath\", '')
    $path = $file
    foreach ($dir in $directory) {
        if ($path -like "$dir*") {
            $LinkPrivatePublicFiles += $path
            Write-Verbose "Adding file to linking list of files $path"
            # Write-Color 'Adding file to ', 'linking list', ' of files ', $path -Color White, Yellow, White, Yellow

        }
    }
    return $LinkPrivatePublicFiles
}
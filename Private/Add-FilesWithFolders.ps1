function Add-FilesWithFolders {
    [CmdletBinding()]
    param ($file, $FullProjectPath, $directory)
    $LinkPrivatePublicFiles = foreach ($dir in $directory) {
        if ($file -like "$dir*") {
            $file
            Write-Verbose "Adding file to linking list of files $file"
            # Write-Color 'Adding file to ', 'linking list', ' of files ', $path -Color White, Yellow, White, Yellow
        }
    }
    $LinkPrivatePublicFiles
}
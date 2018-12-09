function Add-FilesWithFoldersNew {
    [CmdletBinding()]
    param($File, $FullProjectPath, $directory)

    <#
    $LinkPrivatePublicFiles = @()
    $path = $file.FullName.Replace("$FullProjectPath\", '')
    foreach ($dir in $directory) {
        if ($path.StartsWith($dir)) {
            $LinkPrivatePublicFiles += $path
            Write-Color 'Adding file to ', 'linking list', ' of files ', $path -Color White, Yellow, White, Yellow

        }
    }
    return $LinkPrivatePublicFiles

    #>
}

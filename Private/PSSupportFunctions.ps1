function Add-ObjectTo($Object, $Type) {
    Write-Color $Object.Name -Color Green
    return $Object.Name
}
function Add-FilesWithFolders($file, $FullProjectPath, $directory) {
    $LinkPrivatePublicFiles = @()
    $path = $file.FullName.Replace("$FullProjectPath\", '')
    foreach ($dir in $directory) {
        if ($path.StartsWith($dir)) {
            $LinkPrivatePublicFiles += $path
            Write-Color 'Adding file to ', 'linking list', ' of files ', $path -Color White, Yellow, White, Yellow

        }
    }
    return $LinkPrivatePublicFiles
}
function Set-LinkedFiles($LinkFiles, $FullModulePath, $FullProjectPath) {
    foreach ($file in $LinkFiles) {
        cmd /c mklink $FullModulePath\$file $FullProjectPath\$file
    }
}
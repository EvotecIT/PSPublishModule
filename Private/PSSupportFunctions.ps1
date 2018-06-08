function Add-ObjectTo($Object, $Type) {
    Write-Color 'Adding ', $Object.Name, ' to ', $Type -Color White, Green, White, Yellow
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
function Add-Directory {
    param(
        $dir
    )

    $exists = Test-Path -Path $dir
    if ($exists -eq $false) {
        Write-Color 'Creating directory ', $dir -Color White, Yellow
        $createdDirectory = mkdir $dir
    } else {
        Write-Color 'Creating directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}
function Remove-Directory {
    param (
        $dir
    )

    $exists = Test-Path $dir
    if ($exists) {
        Write-Color 'Removing directory ', $dir -Color White, Yellow
        Remove-Item $dir -Confirm:$false -Recurse
    } else {
        Write-Color 'Removing directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}
function Set-LinkedFiles {
    [CmdletBinding()]
    param(
        $LinkFiles,
        $FullModulePath,
        $FullProjectPath,
        [switch] $Delete
    )

    foreach ($file in $LinkFiles) {
        $Path = "$FullModulePath\$file"
        $Path2 = "$FullProjectPath\$file"

        if ($Delete) {
            if (Test-ReparsePoint -path $Path) {
                #  Write-Color 'Removing symlink first ', $path  -Color White, Yellow
                Write-Verbose "Removing symlink first $path"
                Remove-Item $Path -Confirm:$false
            }

        }
        Write-Verbose "Creating symlink from $path2 (source) to $path (target)"
        #Write-Color 'Creating symlink from ', $path2, ' (source) to ', $path, ' (target)' -Color White, Yellow, White, Yellow, White
        $linkingFiles = cmd /c mklink $path $path2
    }
}
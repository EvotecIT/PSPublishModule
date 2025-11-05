function Copy-InternalFiles {
    [CmdletBinding()]
    param(
        [string[]] $LinkFiles,
        [string] $FullModulePath,
        [string] $FullProjectPath,
        [switch] $Delete
    )

    foreach ($File in $LinkFiles) {
        [string] $Path = [System.IO.Path]::Combine($FullModulePath, $File)
        [string] $Path2 = [System.IO.Path]::Combine($FullProjectPath, $File)

        if ($Delete) {
            if (Test-ReparsePoint -path $Path) {
                #  Write-Color 'Removing symlink first ', $path  -Color White, Yellow
                #Write-Verbose "Removing symlink first $path"
                Remove-Item $Path -Confirm:$false
            }
        }
        #Write-Verbose "Creating symlink from $path2 (source) to $path (target)"
        #Write-Color 'Creating symlink from ', $path2, ' (source) to ', $path, ' (target)' -Color White, Yellow, White, Yellow, White
        Copy-Item -Path $Path2 -Destination $Path -Force -Recurse -Confirm:$false
        #$null = cmd /c mklink $path $path2
    }
}
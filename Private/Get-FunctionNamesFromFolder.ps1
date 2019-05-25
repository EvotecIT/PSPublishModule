function Get-FunctionNamesFromFolder {
    [cmdletbinding()]
    param(
        [string] $FullProjectPath,
        [string[]] $Folder
    )

    foreach ($F in $Folder) {
        $Path = [IO.Path]::Combine($FullProjectPath, $F)
        if ($PSEdition -eq 'Core') {
            $Files = Get-ChildItem -Path $Path -File -Recurse -FollowSymlink
        } else {
            $Files = Get-ChildItem -Path $Path -File -Recurse
        }
        $FunctionToExport = foreach ($file in $Files) {
            Get-FunctionNames -Path $File.FullName
        }
        $FunctionToExport
    }
}

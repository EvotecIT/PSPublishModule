function Get-FunctionNamesFromFolder {
    [cmdletbinding()]
    param(
        [string] $FullProjectPath,
        [string[]] $Folder
    )

    $Files = foreach ($F in $Folder) {
        $Path = [IO.Path]::Combine($FullProjectPath, $F)
        if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $Path -File -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $Path -File -Recurse
        }
    }
    $Files = $Files | Sort-Object -Unique
    $FunctionToExport = foreach ($file in $Files) {
        Get-FunctionNames -Path $File.FullName
    }
    $FunctionToExport

}

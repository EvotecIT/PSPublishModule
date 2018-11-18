function Get-FunctionNamesFromFolder {
    param(
        [string] $FullProjectPath,
        [string[]] $Folder
    )

    foreach ($F in $Folder) {
        $Path = [IO.Path]::Combine($FullProjectPath, $F)
        $Files = Get-ChildItem -Path $Path -File -Recurse

        $FunctionToExport = foreach ($file in $Files) {
            Get-FunctionNames -Path $File.FullName
        }
        $FunctionToExport
    }
}

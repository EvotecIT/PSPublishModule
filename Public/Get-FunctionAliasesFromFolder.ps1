function Get-FunctionAliasesFromFolder {
    param(
        [string] $FullProjectPath,
        [string[]] $Folder
    )

    foreach ($F in $Folder) {
        $Path = [IO.Path]::Combine($FullProjectPath, $F)
        $Files = Get-ChildItem -Path $Path -File -Recurse

        $AliasesToExport = foreach ($file in $Files) {
            Get-FunctionAliases -Path $File.FullName

        }
        $AliasesToExport
    }
}
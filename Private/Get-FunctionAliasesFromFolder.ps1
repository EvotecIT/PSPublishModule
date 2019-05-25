function Get-FunctionAliasesFromFolder {
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


        $AliasesToExport = foreach ($file in $Files) {
            #Get-FunctionAliases -Path $File.FullName
            Get-AliasTarget -Path $File.FullName | Select-Object -ExpandProperty Alias
        }
        $AliasesToExport
    }
}
function Get-FunctionAliasesFromFolder {
    [cmdletbinding()]
    param(
        [string] $FullProjectPath,
        [string[]] $Folder,
        [Array] $Files
    )
    $FilesPS1 = foreach ($File in $Files) {
        $Path = [io.path]::Combine("*", 'Public', '*')
        if ($file.FullName -like $Path) {
            if ($File.Extension -eq '.ps1' -or $File.Extension -eq '*.psm1') {
                $File
            }
        }
    }
    [Array] $Content = foreach ($File in $FilesPS1) {
        ''
        Get-Content -LiteralPath $File.FullName -Raw -Encoding UTF8
    }
    $Code = $Content -join [System.Environment]::NewLine

    $AliasesToExport = Get-FunctionAliases -Content $Code -AsHashtable
    $AliasesToExport
}
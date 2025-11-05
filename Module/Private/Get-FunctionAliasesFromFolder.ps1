function Get-FunctionAliasesFromFolder {
    [cmdletbinding()]
    param(
        [string] $FullProjectPath,
        [string[]] $Folder,
        [Array] $Files,
        [string] $FunctionsToExport,
        [string] $AliasesToExport
    )
    [Array] $FilesPS1 = foreach ($File in $Files) {
        if ($FunctionsToExport) {
            $PathFunctions = [io.path]::Combine($FullProjectPath, $FunctionsToExport, '*')
            if ($File.FullName -like $PathFunctions) {
                if ($File.Extension -eq '.ps1' -or $File.Extension -eq '*.psm1') {
                    $File
                }
            }
        }
        if ($AliasesToExport -and $AliasesToExport -ne $FunctionsToExport) {
            $PathAliases = [io.path]::Combine($FullProjectPath, $AliasesToExport, '*')
            if ($File.FullName -like $PathAliases) {
                if ($File.Extension -eq '.ps1' -or $File.Extension -eq '*.psm1') {
                    $File
                }
            }
        }
    }
    [Array] $Content = foreach ($File in $FilesPS1 | Sort-Object -Unique) {
        ''
        Get-Content -LiteralPath $File.FullName -Raw -Encoding UTF8
    }
    $Code = $Content -join [System.Environment]::NewLine

    $OutputAliasesToExport = Get-FunctionAliases -Content $Code -AsHashtable
    $OutputAliasesToExport
}
function Get-FunctionAliasesFromFolder {
    [cmdletbinding()]
    param(
        [string] $FullProjectPath,
        [string[]] $Folder,
        [Array] $Files
    )

    <#
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
    #>
    $FilesPS1 = foreach ($File in $Files) {
        if ($file.FullName -like "*\Public\*") {
            if ($File.Extension -eq '.ps1' -or $File.Extension -eq '*.psm1') {
                $File
            }
        }
    }
    $AliasesToExport = foreach ($file in $FilesPS1) {
        #Get-FunctionAliases -Path $File.FullName

        #Write-TextWithTime -Text "Alias $($File.FullName)" {
        Get-AliasTarget -Path $File.FullName | Select-Object -ExpandProperty Alias
        # }
    }
    $AliasesToExport
}
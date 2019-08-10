function Get-FunctionNamesFromFolder {
    [cmdletbinding()]
    param(
        [string] $FullProjectPath,
        [string[]] $Folder,
        [Array] $Files
    )

    <#
    $Files = foreach ($F in $Folder) {
        $Path = [IO.Path]::Combine($FullProjectPath, $F)
        if ($PSEdition -eq 'Core') {
            Get-ChildItem -Path $Path -File -Recurse -FollowSymlink
        } else {
            Get-ChildItem -Path $Path -File -Recurse
        }
    }
    $Files = $Files | Sort-Object -Unique
    #>
    $FilesPS1 = foreach ($File in $Files) {
        if ($file.FullName -like "*\Public\*") {
            if ($File.Extension -eq '.ps1' -or $File.Extension -eq '*.psm1') {
                $File
            }
        }
    }
    $FunctionToExport = foreach ($file in $FilesPS1) {
        Write-TextWithTime -Text "Function $($File.FullName)" {
            Get-FunctionNames -Path $File.FullName
        }
    }
    $FunctionToExport

}

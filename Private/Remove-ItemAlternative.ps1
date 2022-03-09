function Remove-ItemAlternative {
    <#
    .SYNOPSIS
    Removes all files and folders within given path

    .DESCRIPTION
    Removes all files and folders within given path. Workaround for Access to the cloud file is denied issue

    .PARAMETER Path
    Path to file/folder

    .PARAMETER SkipFolder
    Do not delete top level folder

    .EXAMPLE
    Remove-ItemAlternative -Path "C:\Support\GitHub\GpoZaurr\Docs"

    .EXAMPLE
    Remove-ItemAlternative -Path "C:\Support\GitHub\GpoZaurr\Docs"

    .NOTES
    General notes
    #>
    [cmdletbinding()]
    param(
        [alias('LiteralPath')][string] $Path,
        [switch] $SkipFolder
    )
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        $Items = Get-ChildItem -LiteralPath $Path -Recurse -Force -File
        foreach ($Item in $Items) {
            try {
                $Item.Delete()
            } catch {
                Write-Warning "Remove-ItemAlternative - Couldn't delete $($Item.FullName), error: $($_.Exception.Message)"
            }
        }
        $Items = Get-ChildItem -LiteralPath $Path -Recurse -Force | Sort-Object -Descending -Property 'FullName'
        foreach ($Item in $Items) {
            try {
                $Item.Delete()
            } catch {
                Write-Warning "Remove-ItemAlternative - Couldn't delete $($Item.FullName), error: $($_.Exception.Message)"
            }
        }
        if (-not $SkipFolder) {
            $Item = Get-Item -LiteralPath $Path
            try {
                $Item.Delete($true)
            } catch {
                Write-Warning "Remove-ItemAlternative - Couldn't delete $($Item.FullName), error: $($_.Exception.Message)"
            }
        }
    } else {
        Write-Warning "Remove-ItemAlternative - Path $Path doesn't exists. Skipping. "
    }
}
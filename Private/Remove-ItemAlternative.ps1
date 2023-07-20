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

    .PARAMETER Exclude
    Skip files/folders matching given pattern

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
        [switch] $SkipFolder,
        [string[]] $Exclude
    )
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        $getChildItemSplat = @{
            Path    = $Path
            Recurse = $true
            Force   = $true
            File    = $true
            Exclude = $Exclude
        }
        Remove-EmptyValue -Hashtable $getChildItemSplat
        $Items = Get-ChildItem @getChildItemSplat
        foreach ($Item in $Items) {
            try {
                $Item.Delete()
            } catch {
                Write-Warning "Remove-ItemAlternative - Couldn't delete $($Item.FullName), error: $($_.Exception.Message)"
            }
        }
        $getChildItemSplat = @{
            Path    = $Path
            Recurse = $true
            Force   = $true
            Exclude = $Exclude
        }
        Remove-EmptyValue -Hashtable $getChildItemSplat
        $Items = Get-ChildItem @getChildItemSplat | Sort-Object -Descending -Property 'FullName'
        foreach ($Item in $Items) {
            try {
                $Item.Delete()
            } catch {
                Write-Warning "Remove-ItemAlternative - Couldn't delete $($Item.FullName), error: $($_.Exception.Message)"
            }
        }
        if (-not $SkipFolder.IsPresent) {
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
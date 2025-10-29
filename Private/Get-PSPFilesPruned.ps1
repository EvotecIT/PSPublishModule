function Get-PSPFilesPruned {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.IO.DirectoryInfo[]] $Directories,
        [switch] $FollowSymlink
    )
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($dir in $Directories) {
        if (-not $FollowSymlink -and ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
        try { $localFiles = Get-ChildItem -LiteralPath $dir.FullName -File -ErrorAction Stop } catch { $localFiles = @() }
        foreach ($f in $localFiles) { $files.Add($f) | Out-Null }
    }
    $files.ToArray()
}


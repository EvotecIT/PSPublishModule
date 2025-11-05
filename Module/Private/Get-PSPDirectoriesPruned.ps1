function Get-PSPDirectoriesPruned {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $BasePath,
        [Parameter(Mandatory)][string[]] $ScanRelativeDirs,
        [string[]] $ExcludeNames = @(),
        [string[]] $PruneNames = @('.git','obj','bin','.vs','node_modules','dist','out','Ignore'),
        [switch] $FollowSymlink
    )
    $list = New-Object System.Collections.Generic.List[System.IO.DirectoryInfo]
    foreach ($rel in ($ScanRelativeDirs | Where-Object { $_ -and -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)) {
        $root = Join-Path $BasePath $rel
        if (-not (Test-Path -LiteralPath $root)) { continue }
        try { $rootDir = Get-Item -LiteralPath $root -ErrorAction Stop } catch { continue }
        if (-not $rootDir.PSIsContainer) { continue }

        $stack = New-Object System.Collections.Stack
        $stack.Push($rootDir)
        while ($stack.Count -gt 0) {
            $dir = [System.IO.DirectoryInfo]$stack.Pop()
            if ($null -eq $dir) { continue }
            $name = $dir.Name
            if ($name -like '.*') { continue }
            if ($PruneNames -contains $name) { continue }
            if ($ExcludeNames -contains $name) { continue }
            if (-not $FollowSymlink -and ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
            $list.Add($dir) | Out-Null
            try { $children = Get-ChildItem -LiteralPath $dir.FullName -Directory -ErrorAction Stop } catch { continue }
            foreach ($child in $children) { $stack.Push($child) }
        }
    }
    $list.ToArray()
}

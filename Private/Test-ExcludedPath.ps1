function Test-ExcludePath {
    [cmdletbinding()]
    param(
        $Path,
        $ExcludeDirs,
        $SpecialExcludeFolders
    )

    $pathParts = $Path.Split([System.IO.Path]::DirectorySeparatorChar) | Where-Object { $_ -ne '' }

    # Check standard exclude directories
    foreach ($excludeDir in $ExcludeDirs) {
        if ($pathParts -contains $excludeDir) {
            return $true
        }
    }

    # Check special exclude folders (like Assets, Docs for HTML cleanup)
    foreach ($excludeFolder in $SpecialExcludeFolders) {
        if ($pathParts -contains $excludeFolder) {
            return $true
        }
    }

    return $false
}
function Get-ProjectItemsToRemove {
    <#
    .SYNOPSIS
    Collects all files and folders that match cleanup patterns.

    .DESCRIPTION
    Scans the project directory for files and folders matching the specified cleanup patterns,
    applying exclusion rules and filters.

    .PARAMETER ProjectPath
    Root path of the project to scan.

    .PARAMETER CleanupPatterns
    Hashtable containing Folders, Files, and ExcludeFolders arrays.

    .PARAMETER ExcludePatterns
    Patterns to exclude from removal.

    .PARAMETER ExcludeDirectories
    Directory names to completely exclude from scanning.

    .PARAMETER Recurse
    Whether to scan subdirectories recursively.

    .PARAMETER MaxDepth
    Maximum recursion depth.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath,

        [Parameter(Mandatory)]
        [hashtable] $CleanupPatterns,

        [string[]] $ExcludePatterns = @(),

        [string[]] $ExcludeDirectories = @(),

        [bool] $Recurse = $true,

        [int] $MaxDepth = -1
    )

    $itemsList = [System.Collections.Generic.List[PSObject]]::new()
    $processedPaths = @{}

     # Process folder patterns
    foreach ($pattern in $CleanupPatterns.Folders) {
        $params = @{
            Path = $ProjectPath
            Filter = $pattern
            Directory = $true
            Recurse = $Recurse
            Force = $true
            ErrorAction = 'SilentlyContinue'
        }

        if ($MaxDepth -gt 0) {
            $params.Depth = $MaxDepth
        }

        try {
            $folders = Get-ChildItem @params | Where-Object {
                $folderPath = $_.FullName
                $relativePath = $folderPath.Substring($ProjectPath.Length)

                # Skip if already processed or excluded
                if ($processedPaths.ContainsKey($folderPath)) { return $false }
                if (Test-ExcludePath -Path $relativePath -ExcludeDirs $ExcludeDirectories -SpecialExcludeFolders $CleanupPatterns.ExcludeFolders) { return $false }

                # Check exclude patterns
                $excluded = $false
                foreach ($excludePattern in $ExcludePatterns) {
                    if ($_.Name -like $excludePattern) {
                        $excluded = $true
                        break
                    }
                }
                -not $excluded
            }

            foreach ($folder in $folders) {
                $processedPaths[$folder.FullName] = $true
                $itemsList.Add([PSCustomObject]@{
                    FullPath = $folder.FullName
                    RelativePath = $folder.FullName.Substring($ProjectPath.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
                    Type = 'Folder'
                    Pattern = $pattern
                    Size = 0
                    Item = $folder
                })
            }
        } catch {
            Write-Warning "Error processing folder pattern '$pattern': $_"
        }
    }

    # Process file patterns
    foreach ($pattern in $CleanupPatterns.Files) {
        $params = @{
            Path = $ProjectPath
            Filter = $pattern
            File = $true
            Recurse = $Recurse
            Force = $true
            ErrorAction = 'SilentlyContinue'
        }

        if ($MaxDepth -gt 0) {
            $params.Depth = $MaxDepth
        }

        try {
            $files = Get-ChildItem @params | Where-Object {
                $filePath = $_.FullName
                $relativePath = $filePath.Substring($ProjectPath.Length)

                # Skip if already processed or excluded
                if ($processedPaths.ContainsKey($filePath)) { return $false }
                if (Test-ExcludePath -Path $relativePath -ExcludeDirs $ExcludeDirectories -SpecialExcludeFolders $CleanupPatterns.ExcludeFolders) { return $false }

                # Check exclude patterns
                $excluded = $false
                foreach ($excludePattern in $ExcludePatterns) {
                    if ($_.Name -like $excludePattern) {
                        $excluded = $true
                        break
                    }
                }
                -not $excluded
            }

            foreach ($file in $files) {
                $processedPaths[$file.FullName] = $true
                $itemsList.Add([PSCustomObject]@{
                    FullPath = $file.FullName
                    RelativePath = $file.FullName.Substring($ProjectPath.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
                    Type = 'File'
                    Pattern = $pattern
                    Size = $file.Length
                    Item = $file
                })
            }
        } catch {
            Write-Warning "Error processing file pattern '$pattern': $_"
        }
    }

    # Sort items by depth (deepest first for safe deletion)
    $sortedItems = $itemsList.ToArray() | Sort-Object { $_.RelativePath.Split([System.IO.Path]::DirectorySeparatorChar).Count } -Descending

    return $sortedItems
}

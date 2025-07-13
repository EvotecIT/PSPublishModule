# Parameters for script execution
param(
    [string]$ProjectPath,
    [string[]]$ExcludeFolders,
    [switch]$WhatIf,
    [switch]$ShowDetails
)

function Remove-ProjectHtmlFiles {
    <#
    .SYNOPSIS
    Cleans up HTML files from project directories that are left behind by AI or development processes.

    .DESCRIPTION
    This function removes HTML files from the project root and HtmlForgeX.* directories,
    while allowing specific folders to be excluded from cleanup (like Assets, Module, Examples).

    .PARAMETER ProjectPath
    The root path of the project. Defaults to current directory.

    .PARAMETER ExcludeFolders
    Array of folder names to exclude from cleanup. Defaults to common folders that should keep HTML files.

    .PARAMETER WhatIf
    Shows what files would be deleted without actually deleting them.    .PARAMETER ShowDetails
    Provides detailed output of the cleanup process.
    
    .EXAMPLE
    Remove-ProjectHtmlFiles -WhatIf
    Shows what HTML files would be deleted
    
    .EXAMPLE
    Remove-ProjectHtmlFiles -ExcludeFolders @("Assets", "Module", "Examples", "Docs")
    Cleans HTML files while excluding specific folders
    
    .EXAMPLE
    Remove-ProjectHtmlFiles -ShowDetails
    Runs cleanup with detailed output showing full paths of deleted files
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string]$ProjectPath = (Get-Location),
        [string[]]$ExcludeFolders = @("Assets", "Module", "Examples", "Docs", "bin", "obj", ".git", ".vs", ".vscode", "TestResults"),
        [switch]$ShowDetails
    )

    if (-not (Test-Path $ProjectPath)) {
        Write-Error "Project path '$ProjectPath' does not exist."
        return
    }

    Write-Host "Starting HTML cleanup in: $ProjectPath" -ForegroundColor Green

    # Find all HTML files in project root (excluding those in excluded folders)
    $rootHtmlFiles = Get-ChildItem -Path $ProjectPath -Filter "*.html" -File | Where-Object { 
        $isInRoot = $_.DirectoryName -eq $ProjectPath
        $isInRoot
    }

    # Find all HTML files in HtmlForgeX.* directories
    $projectDirs = Get-ChildItem -Path $ProjectPath -Filter "HtmlForgeX*" -Directory
    
    $projectHtmlFiles = @()
    foreach ($dir in $projectDirs) {
        if ($ShowDetails) {
            Write-Host "  Checking directory: $($dir.Name)" -ForegroundColor Gray
        }
        try {
            $htmlFiles = Get-ChildItem -Path $dir.FullName -Filter "*.html" -File -ErrorAction SilentlyContinue | Where-Object {
                $relativePath = $_.FullName.Substring($ProjectPath.Length)
                $pathParts = $relativePath -split [System.IO.Path]::DirectorySeparatorChar | Where-Object { $_ -ne '' }
                $shouldExclude = $false
                foreach ($exclude in $ExcludeFolders) {
                    if ($pathParts -contains $exclude) {
                        $shouldExclude = $true
                        break
                    }
                }
                -not $shouldExclude
            }
            
            # Also check subdirectories but limit depth to avoid issues
            $subHtmlFiles = Get-ChildItem -Path $dir.FullName -Filter "*.html" -Recurse -Depth 3 -File -ErrorAction SilentlyContinue | Where-Object {
                $relativePath = $_.FullName.Substring($ProjectPath.Length)
                $pathParts = $relativePath -split [System.IO.Path]::DirectorySeparatorChar | Where-Object { $_ -ne '' }
                $shouldExclude = $false
                foreach ($exclude in $ExcludeFolders) {
                    if ($pathParts -contains $exclude) {
                        $shouldExclude = $true
                        break
                    }
                }
                -not $shouldExclude
            }
            
            $allDirFiles = @($htmlFiles) + @($subHtmlFiles) | Sort-Object FullName -Unique
            $projectHtmlFiles += $allDirFiles
            if ($ShowDetails) {
                Write-Host "    Found $($allDirFiles.Count) HTML files" -ForegroundColor Gray
            }
        } catch {
            Write-Host "    Error searching in $($dir.Name): $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    # Combine all HTML files to process
    $allHtmlFiles = @($rootHtmlFiles) + @($projectHtmlFiles)

    if ($allHtmlFiles.Count -eq 0) {
        Write-Host "No HTML files found to clean up." -ForegroundColor Yellow
        return
    }

    Write-Host "Found $($allHtmlFiles.Count) HTML file(s) to process:" -ForegroundColor Cyan

    foreach ($file in $allHtmlFiles) {
        $relativePath = $file.FullName.Substring($ProjectPath.Length + 1)

        if ($WhatIfPreference) {
            Write-Host "  [WOULD DELETE] $relativePath" -ForegroundColor Yellow
        } else {
            try {
                Remove-Item -Path $file.FullName -Force
                Write-Host "  [DELETED] $relativePath" -ForegroundColor Red
                if ($ShowDetails) {
                    Write-Host "    Successfully removed: $($file.FullName)" -ForegroundColor DarkGray
                }
            } catch {
                Write-Error "Failed to delete '$relativePath': $($_.Exception.Message)"
            }
        }
    }

    if ($WhatIfPreference) {
        Write-Host "`nRun without -WhatIf to actually delete these files." -ForegroundColor Cyan
    } else {
        Write-Host "`nHTML cleanup completed successfully!" -ForegroundColor Green
    }
}

# Main execution - only run if script is executed directly, not dot-sourced
if ($MyInvocation.InvocationName -ne '.') {
    # Set defaults if not provided
    if (-not $ProjectPath) { $ProjectPath = Get-Location }
    if (-not $ExcludeFolders) { $ExcludeFolders = @("Assets", "Module", "Examples", "Docs", "bin", "obj", ".git", ".vs", ".vscode", "TestResults") }
    
    Remove-ProjectHtmlFiles -ProjectPath $ProjectPath -ExcludeFolders $ExcludeFolders -ShowDetails:$ShowDetails -WhatIf:$WhatIf
}
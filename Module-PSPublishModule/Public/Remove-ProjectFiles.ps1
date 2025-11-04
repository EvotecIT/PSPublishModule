function Remove-ProjectFiles {
    <#
    .SYNOPSIS
    Removes specific files and folders from a project directory with comprehensive safety features.

    .DESCRIPTION
    Recursively removes files and folders matching specified patterns from a project directory.
    Includes comprehensive safety features: WhatIf support, automatic backups, detailed reporting,
    and multiple deletion methods. Designed for cleaning up build artifacts, temporary files,
    logs, and other unwanted files from development projects.

    .PARAMETER ProjectPath
    Path to the project directory to clean.

    .PARAMETER ProjectType
    Type of project cleanup to perform. Determines default patterns and behaviors.
    Valid values: 'Build', 'Logs', 'Html', 'Temp', 'All', 'Custom'

    .PARAMETER IncludePatterns
    File/folder patterns to include for deletion when ProjectType is 'Custom'.
    Example: @('*.html', '*.log', 'bin', 'obj', 'temp*')

    .PARAMETER ExcludePatterns
    File/folder patterns to exclude from deletion.
    Example: @('*.config', 'packages.config')

    .PARAMETER ExcludeDirectories
    Directory names to completely exclude from processing (e.g., '.git', '.vs').

    .PARAMETER DeleteMethod
    Method to use for file deletion.
    Valid values: 'RemoveItem', 'DotNetDelete', 'RecycleBin'
    - RemoveItem: Standard Remove-Item cmdlet
    - DotNetDelete: Uses .NET Delete() method for cloud file issues
    - RecycleBin: Moves files to Recycle Bin

    .PARAMETER CreateBackups
    Create backup files before deletion for additional safety.

    .PARAMETER BackupDirectory
    Directory to store backup files. If not specified, backups are created in a temp location.

    .PARAMETER Retries
    Number of retry attempts for file deletion. Default is 3.

    .PARAMETER Recurse
    Process subdirectories recursively. Default is true for most cleanup types.

    .PARAMETER MaxDepth
    Maximum recursion depth. Default is unlimited (-1).

    .PARAMETER ShowProgress
    Display progress information during cleanup.

    .PARAMETER PassThru
    Return detailed results for each processed file/folder.

    .PARAMETER Internal
    Suppress console output and use verbose/warning streams instead.

    .EXAMPLE
    Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType Build -WhatIf
    Preview what build artifacts would be removed from the project.

    .EXAMPLE
    Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType Html -DeleteMethod DotNetDelete -ShowProgress
    Remove all HTML files using .NET deletion method with progress display.

    .EXAMPLE
    Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType Custom -IncludePatterns @('*.log', 'temp*', 'bin', 'obj') -CreateBackups
    Custom cleanup of log files and build directories with backup creation.

    .EXAMPLE
    Remove-ProjectFiles -ProjectPath 'C:\MyProject' -ProjectType All -ExcludePatterns @('*.config') -DeleteMethod RecycleBin
    Remove all cleanup targets except config files, moving them to Recycle Bin.

    .NOTES
    Cleanup type mappings:
    - Build: bin, obj, packages, .vs, .vscode, TestResults, BenchmarkDotNet.Artifacts
    - Logs: *.log, *.tmp, *.temp, logs folder
    - Html: *.html, *.htm (except in Assets, Docs, Examples folders)
    - Temp: temp*, tmp*, cache*, *.cache, *.tmp
    - All: Combination of all above types

    Safety Features:
    - WhatIf support for preview
    - Backup creation before deletion
    - Multiple deletion methods with retry logic
    - Comprehensive error handling
    - Detailed reporting
    #>
    [CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'ProjectType')]
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath,

        [Parameter(ParameterSetName = 'ProjectType')]
        [ValidateSet('Build', 'Logs', 'Html', 'Temp', 'All')]
        [string] $ProjectType = 'Build',

        [Parameter(ParameterSetName = 'Custom', Mandatory)]
        [string[]] $IncludePatterns,

        [string[]] $ExcludePatterns,

        [string[]] $ExcludeDirectories = @('.git', '.svn', '.hg', 'node_modules'),

        [ValidateSet('RemoveItem', 'DotNetDelete', 'RecycleBin')]
        [string] $DeleteMethod = 'RemoveItem',

        [switch] $CreateBackups,

        [string] $BackupDirectory,

        [int] $Retries = 3,

        [switch] $Recurse,

        [int] $MaxDepth = -1,

        [switch] $ShowProgress,

        [switch] $PassThru,

        [switch] $Internal
    )

    # Validate project path
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) {
        throw "Project path '$ProjectPath' not found or is not a directory"
    }

    # Set default for Recurse if not specified
    if (-not $PSBoundParameters.ContainsKey('Recurse')) {
        $Recurse = $true
    }

    # Get cleanup patterns based on project type
    if ($PSCmdlet.ParameterSetName -eq 'Custom') {
        $cleanupPatterns = Get-ProjectCleanupPatterns -ProjectType 'Custom' -IncludePatterns $IncludePatterns
    } else {
        $cleanupPatterns = Get-ProjectCleanupPatterns -ProjectType $ProjectType
    }

    if ($Internal) {
        Write-Verbose "Processing project cleanup for: $ProjectPath"
        if ($PSCmdlet.ParameterSetName -eq 'Custom') {
            Write-Verbose "Project type: Custom with patterns: $($IncludePatterns -join ', ')"
        } else {
            Write-Verbose "Project type: $ProjectType"
        }
        Write-Verbose "Folder patterns: $($cleanupPatterns.Folders -join ', ')"
        Write-Verbose "File patterns: $($cleanupPatterns.Files -join ', ')"
    } else {
        Write-Host "Processing project cleanup for: $ProjectPath" -ForegroundColor Cyan
        if ($PSCmdlet.ParameterSetName -eq 'Custom') {
            Write-Host "Project type: Custom with patterns: $($IncludePatterns -join ', ')" -ForegroundColor White
        } else {
            Write-Host "Project type: $ProjectType" -ForegroundColor White
        }
    }

    # Prepare backup directory if specified
    if ($CreateBackups) {
        if (-not $BackupDirectory) {
            $BackupDirectory = Join-Path $env:TEMP "PSPublishModule_Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        }

        if (-not (Test-Path -LiteralPath $BackupDirectory)) {
            New-Item -Path $BackupDirectory -ItemType Directory -Force | Out-Null
            if ($Internal) {
                Write-Verbose "Created backup directory: $BackupDirectory"
            } else {
                Write-Host "Created backup directory: $BackupDirectory" -ForegroundColor Green
            }
        }
    }

    # Collect all items to process
    if ($Internal) {
        Write-Verbose "Scanning for files to remove..."
    } else {
        Write-Host "Scanning for files to remove..." -ForegroundColor Cyan
    }

    $itemsToProcess = Get-ProjectItemsToRemove -ProjectPath $ProjectPath -CleanupPatterns $cleanupPatterns -ExcludePatterns $ExcludePatterns -ExcludeDirectories $ExcludeDirectories -Recurse $Recurse -MaxDepth $MaxDepth

    if ($itemsToProcess.Count -eq 0) {
        if ($Internal) {
            Write-Verbose "No files or folders found matching the specified criteria."
        } else {
            Write-Host "No files or folders found matching the specified criteria." -ForegroundColor Yellow
        }
        return
    }

    # Calculate total size
    $totalSize = ($itemsToProcess | Where-Object Type -EQ 'File' | Measure-Object -Property Size -Sum).Sum
    $totalSizeMB = [math]::Round($totalSize / 1MB, 2)

    if ($Internal) {
        Write-Verbose "Found $($itemsToProcess.Count) items to remove"
        Write-Verbose "Files: $(($itemsToProcess | Where-Object Type -EQ 'File').Count)"
        Write-Verbose "Folders: $(($itemsToProcess | Where-Object Type -EQ 'Folder').Count)"
        Write-Verbose "Total size: $totalSizeMB MB"
    } else {
        Write-Host "Found $($itemsToProcess.Count) items to remove:" -ForegroundColor Green
        Write-Host "  Files: $(($itemsToProcess | Where-Object Type -EQ 'File').Count)" -ForegroundColor White
        Write-Host "  Folders: $(($itemsToProcess | Where-Object Type -EQ 'Folder').Count)" -ForegroundColor White
        Write-Host "  Total size: $totalSizeMB MB" -ForegroundColor White
    }

    if ($ShowProgress -and -not $Internal) {
        Write-Host "`nItems to be removed:" -ForegroundColor Cyan
        foreach ($item in $itemsToProcess | Select-Object -First 10) {
            $sizeInfo = if ($item.Type -eq 'File') { " ($([math]::Round($item.Size / 1KB, 1)) KB)" } else { '' }
            Write-Host "  [$($item.Type)] $($item.RelativePath)$sizeInfo" -ForegroundColor Gray
        }
        if ($itemsToProcess.Count -gt 10) {
            Write-Host "  ... and $($itemsToProcess.Count - 10) more items" -ForegroundColor Gray
        }
    }

    # Create backups if requested
    if ($CreateBackups) {
        New-ProjectItemBackups -ItemsToProcess $itemsToProcess -BackupDirectory $BackupDirectory -ProjectPath $ProjectPath -Internal $Internal
    }

    # Process items for removal
    $results = Remove-ProjectItemsWithMethod -ItemsToProcess $itemsToProcess -DeleteMethod $DeleteMethod -Retries $Retries -ShowProgress $ShowProgress -Internal $Internal

    # Calculate summary statistics
    $removed = ($results | Where-Object Status -EQ 'Removed').Count
    $errors = ($results | Where-Object Status -EQ 'Error').Count
    $freedSpace = ($results | Where-Object Status -EQ 'Removed' | Measure-Object -Property Size -Sum).Sum
    $freedSpaceMB = [math]::Round($freedSpace / 1MB, 2)

    # Display summary
    $displayProjectType = if ($PSCmdlet.ParameterSetName -eq 'Custom') { "Custom" } else { $ProjectType }

    if ($Internal) {
        Write-Verbose "Cleanup Summary: Project path: $ProjectPath"
        Write-Verbose "Cleanup type: $displayProjectType"
        Write-Verbose "Total items processed: $($itemsToProcess.Count)"

        if ($WhatIfPreference) {
            Write-Verbose "Would remove: $($itemsToProcess.Count) items"
            Write-Verbose "Would free: $totalSizeMB MB"
        } else {
            Write-Verbose "Successfully removed: $removed"
            Write-Verbose "Errors: $errors"
            Write-Verbose "Space freed: $freedSpaceMB MB"

            if ($CreateBackups) {
                Write-Verbose "Backups created in: $BackupDirectory"
            }
        }
    } else {
        Write-Host "`nCleanup Summary:" -ForegroundColor Cyan
        Write-Host "  Project path: $ProjectPath" -ForegroundColor White
        Write-Host "  Cleanup type: $displayProjectType" -ForegroundColor White
        Write-Host "  Total items processed: $($itemsToProcess.Count)" -ForegroundColor White

        if ($WhatIfPreference) {
            Write-Host "  Would remove: $($itemsToProcess.Count) items" -ForegroundColor Yellow
            Write-Host "  Would free: $totalSizeMB MB" -ForegroundColor Yellow
            Write-Host "`nRun without -WhatIf to actually remove these items." -ForegroundColor Cyan
        } else {
            Write-Host "  Successfully removed: $removed" -ForegroundColor Green
            Write-Host "  Errors: $errors" -ForegroundColor Red
            Write-Host "  Space freed: $freedSpaceMB MB" -ForegroundColor Green

            if ($CreateBackups) {
                Write-Host "  Backups created in: $BackupDirectory" -ForegroundColor Blue
            }
        }
    }

    if ($PassThru) {
        [PSCustomObject]@{
            Summary = @{
                ProjectPath     = $ProjectPath
                ProjectType     = $displayProjectType
                TotalItems      = $itemsToProcess.Count
                Removed         = $removed
                Errors          = $errors
                SpaceFreed      = $freedSpace
                SpaceFreedMB    = $freedSpaceMB
                BackupDirectory = if ($CreateBackups) { $BackupDirectory } else { $null }
                DeleteMethod    = $DeleteMethod
            }
            Results = $results
        }
    }
}

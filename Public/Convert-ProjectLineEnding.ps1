function Convert-ProjectLineEnding {
    <#
    .SYNOPSIS
    Converts line endings for all source files in a project directory with comprehensive safety features.

    .DESCRIPTION
    Recursively converts line endings for PowerShell, C#, and other source code files in a project directory.
    Includes comprehensive safety features: WhatIf support, automatic backups, rollback protection,
    and detailed reporting. Can convert between CRLF (Windows), LF (Unix/Linux), and fix mixed line endings.

    .PARAMETER Path
    Path to the project directory to process.

    .PARAMETER ProjectType
    Type of project to process. Determines which file extensions are included.
    Valid values: 'PowerShell', 'CSharp', 'Mixed', 'All', 'Custom'

    .PARAMETER CustomExtensions
    Custom file extensions to process when ProjectType is 'Custom'.
    Example: @('*.ps1', '*.psm1', '*.cs', '*.vb')

    .PARAMETER TargetLineEnding
    Target line ending style. Valid values: 'CRLF', 'LF'

    .PARAMETER ExcludeDirectories
    Directory names to exclude from processing (e.g., '.git', 'bin', 'obj').

    .PARAMETER CreateBackups
    Create backup files before conversion for additional safety.

    .PARAMETER BackupDirectory
    Directory to store backup files. If not specified, backups are created alongside original files.

    .PARAMETER Force
    Convert all files regardless of current line ending type.

    .PARAMETER EnsureFinalNewline
    Ensure all files end with a newline character (POSIX compliance).

    .PARAMETER OnlyMissingNewline
    Only process files that are missing final newlines, leave others unchanged.

    .PARAMETER PassThru
    Return detailed results for each processed file.

    .EXAMPLE
    Convert-ProjectLineEnding -Path 'C:\MyProject' -ProjectType PowerShell -TargetLineEnding CRLF -WhatIf
    Preview what files would be converted to Windows-style line endings.

    .EXAMPLE
    Convert-ProjectLineEnding -Path 'C:\MyProject' -ProjectType Mixed -TargetLineEnding LF -CreateBackups
    Convert a mixed project to Unix-style line endings with backups.

    .EXAMPLE
    Convert-ProjectLineEnding -Path 'C:\MyProject' -ProjectType All -OnlyMixed -PassThru
    Fix only files with mixed line endings and return detailed results.

    .NOTES
    This function modifies files in place. Always use -WhatIf first or -CreateBackups for safety.
    Line ending types:
    - CRLF: Windows style (\\r\\n)
    - LF: Unix/Linux style (\\n)
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [ValidateSet('PowerShell', 'CSharp', 'Mixed', 'All', 'Custom')]
        [string] $ProjectType = 'Mixed',

        [string[]] $CustomExtensions,

        [Parameter(Mandatory)]
        [ValidateSet('CRLF', 'LF')]
        [string] $TargetLineEnding,

        [string[]] $ExcludeDirectories = @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode'),

        [switch] $CreateBackups,
        [string] $BackupDirectory,
        [switch] $Force,
        [switch] $OnlyMixed,
        [switch] $EnsureFinalNewline,
        [switch] $OnlyMissingNewline,
        [switch] $PassThru
    )

    # Validate path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Project path '$Path' not found or is not a directory"
    }

    # Define file extension mappings
    $extensionMappings = @{
        'PowerShell' = @('*.ps1', '*.psm1', '*.psd1', '*.ps1xml')
        'CSharp'     = @('*.cs', '*.csx', '*.csproj', '*.sln', '*.config', '*.json', '*.xml', '*.resx')
        'Mixed'      = @('*.ps1', '*.psm1', '*.psd1', '*.ps1xml', '*.cs', '*.csx', '*.csproj', '*.sln', '*.config', '*.json', '*.xml')
        'All'        = @('*.ps1', '*.psm1', '*.psd1', '*.ps1xml', '*.cs', '*.csx', '*.csproj', '*.sln', '*.config', '*.json', '*.xml', '*.js', '*.ts', '*.py', '*.rb', '*.java', '*.cpp', '*.h', '*.hpp', '*.sql', '*.md', '*.txt', '*.yaml', '*.yml')
    }

    # Determine file patterns to process
    if ($ProjectType -eq 'Custom' -and $CustomExtensions) {
        $filePatterns = $CustomExtensions
    } else {
        $filePatterns = $extensionMappings[$ProjectType]
    }

    Write-Verbose "Processing project type: $ProjectType with patterns: $($filePatterns -join ', ')"    # Helper function to detect current line endings and final newline

    # Prepare backup directory if specified
    if ($CreateBackups -and $BackupDirectory) {
        if (-not (Test-Path -LiteralPath $BackupDirectory)) {
            New-Item -Path $BackupDirectory -ItemType Directory -Force | Out-Null
            Write-Verbose "Created backup directory: $BackupDirectory"
        }
    }

    # Collect all files to process
    $allFiles = @()

    foreach ($pattern in $filePatterns) {
        $params = @{
            Path    = $Path
            Filter  = $pattern
            File    = $true
            Recurse = $true
        }

        $files = Get-ChildItem @params | Where-Object {
            $file = $_
            $excluded = $false

            foreach ($excludeDir in $ExcludeDirectories) {
                if ($file.DirectoryName -like "*\$excludeDir" -or $file.DirectoryName -like "*\$excludeDir\*") {
                    $excluded = $true
                    break
                }
            }

            -not $excluded
        }

        $allFiles += $files
    }

    # Remove duplicates
    $uniqueFiles = $allFiles | Sort-Object FullName | Get-Unique -AsString

    Write-Host "Found $($uniqueFiles.Count) files to process" -ForegroundColor Green

    if ($uniqueFiles.Count -eq 0) {
        Write-Warning "No files found matching the specified criteria"
        return
    }

    # Process files
    $results = @()
    $converted = 0
    $skipped = 0
    $errors = 0

    foreach ($file in $uniqueFiles) {
        try {
            $currentInfo = Get-CurrentLineEnding -FilePath $file.FullName
            $currentLineEnding = $currentInfo.LineEnding
            $hasFinalNewline = $currentInfo.HasFinalNewline
            $relativePath = [System.IO.Path]::GetRelativePath($Path, $file.FullName)

            # Determine if file should be processed
            $shouldProcess = $false
            $skipReason = ""

            if ($currentLineEnding -eq 'Error') {
                $skipReason = "Could not detect line endings"
            } elseif ($currentLineEnding -eq 'None') {
                $skipReason = "Empty file or no line endings"
            } elseif ($OnlyMixed -and $currentLineEnding -ne 'Mixed') {
                $skipReason = "Not mixed line endings (OnlyMixed specified)"
            } elseif ($OnlyMissingNewline -and $hasFinalNewline) {
                $skipReason = "Already has final newline (OnlyMissingNewline specified)"
            } elseif (-not $Force -and $currentLineEnding -eq $TargetLineEnding -and ($hasFinalNewline -or -not $EnsureFinalNewline)) {
                $skipReason = "Already compliant with target settings"
            } else {
                $shouldProcess = $true
            }

            if (-not $shouldProcess) {
                $result = @{
                    FilePath          = $relativePath
                    FullPath          = $file.FullName
                    Status            = 'Skipped'
                    Reason            = $skipReason
                    CurrentLineEnding = $currentLineEnding
                    TargetLineEnding  = $TargetLineEnding
                    HasFinalNewline   = $hasFinalNewline
                }
                $results += [PSCustomObject]$result
                $skipped++
                Write-Verbose "Skipped $relativePath`: $skipReason"
                continue
            }

            if ($PSCmdlet.ShouldProcess($relativePath, "Convert line endings from $currentLineEnding to $TargetLineEnding$(if ($EnsureFinalNewline) { ' and ensure final newline' })")) {
                $conversionResult = Convert-LineEndingSingle -FilePath $file.FullName -TargetLineEnding $TargetLineEnding -CurrentInfo $currentInfo -CreateBackup $CreateBackups -EnsureFinalNewline $EnsureFinalNewline

                $result = @{
                    FilePath          = $relativePath
                    FullPath          = $file.FullName
                    Status            = $conversionResult.Status
                    Reason            = $conversionResult.Reason
                    CurrentLineEnding = $currentLineEnding
                    TargetLineEnding  = $TargetLineEnding
                    HasFinalNewline   = $hasFinalNewline
                    BackupPath        = $conversionResult.BackupPath
                }

                # Move backup to specified directory if requested
                if ($CreateBackups -and $BackupDirectory -and $conversionResult.BackupPath -and (Test-Path $conversionResult.BackupPath)) {
                    $backupTargetPath = Join-Path $BackupDirectory $relativePath
                    $backupTargetDir = Split-Path $backupTargetPath -Parent

                    if (-not (Test-Path $backupTargetDir)) {
                        New-Item -Path $backupTargetDir -ItemType Directory -Force | Out-Null
                    }

                    Move-Item -Path $conversionResult.BackupPath -Destination $backupTargetPath -Force
                    $result.BackupPath = $backupTargetPath
                }

                $results += [PSCustomObject]$result

                switch ($conversionResult.Status) {
                    'Converted' {
                        $converted++
                        Write-Verbose "Converted $relativePath from $currentLineEnding to $TargetLineEnding"
                    }
                    'Error' {
                        $errors++
                        Write-Warning "Failed to convert $relativePath`: $($conversionResult.Reason)"
                    }
                    default { $skipped++ }
                }
            }
        } catch {
            Write-Warning "Unexpected error processing $($file.FullName): $_"
            $errors++
        }
    }

    # Summary report
    $summary = @{
        TotalFiles       = $uniqueFiles.Count
        Converted        = $converted
        Skipped          = $skipped
        Errors           = $errors
        TargetLineEnding = $TargetLineEnding
        ProjectPath      = $Path
        ProjectType      = if ($ProjectType -eq 'Custom') { "Custom ($($CustomExtensions -join ', '))" } else { $ProjectType }
    }

    Write-Host "`nLine Ending Conversion Summary:" -ForegroundColor Cyan
    Write-Host "  Total files processed: $($summary.TotalFiles)" -ForegroundColor White
    Write-Host "  Successfully converted: $($summary.Converted)" -ForegroundColor Green
    Write-Host "  Skipped: $($summary.Skipped)" -ForegroundColor Yellow
    Write-Host "  Errors: $($summary.Errors)" -ForegroundColor Red
    Write-Host "  Target line ending: $($summary.TargetLineEnding)" -ForegroundColor White

    if ($PassThru) {
        [PSCustomObject]@{
            Summary = $summary
            Results = $results
        }
    }
}

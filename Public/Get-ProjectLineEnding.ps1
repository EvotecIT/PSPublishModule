function Get-ProjectLineEnding {
    <#
    .SYNOPSIS
    Analyzes line ending consistency across all files in a project directory.

    .DESCRIPTION
    Scans all relevant files in a project directory and provides a comprehensive report on line endings.
    Identifies inconsistencies between CRLF (Windows), LF (Unix/Linux), and mixed line endings.
    Helps ensure consistency across development environments and prevent Git issues.

    .PARAMETER Path
    Path to the project directory to analyze.

    .PARAMETER ProjectType
    Type of project to analyze. Determines which file extensions are included.
    Valid values: 'PowerShell', 'CSharp', 'Mixed', 'All', 'Custom'

    .PARAMETER CustomExtensions
    Custom file extensions to analyze when ProjectType is 'Custom'.
    Example: @('*.ps1', '*.psm1', '*.cs', '*.vb')

    .PARAMETER ExcludeDirectories
    Directory names to exclude from analysis (e.g., '.git', 'bin', 'obj').

    .PARAMETER GroupByLineEnding
    Group results by line ending type for easier analysis.

    .PARAMETER ShowFiles
    Include individual file details in the report.

    .PARAMETER CheckMixed
    Additionally check for files with mixed line endings (both CRLF and LF in same file).

    .PARAMETER ExportPath
    Export the detailed report to a CSV file at the specified path.

    .EXAMPLE
    Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType PowerShell
    Analyze line ending consistency in a PowerShell project.

    .EXAMPLE
    Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType Mixed -CheckMixed -ShowFiles
    Get detailed line ending report including mixed line ending detection.

    .EXAMPLE
    Get-ProjectLineEnding -Path 'C:\MyProject' -ProjectType All -ExportPath 'C:\Reports\lineending-report.csv'
    Analyze all file types and export detailed report to CSV.

    .NOTES
    Line ending types:
    - CRLF: Windows style (\\r\\n)
    - LF: Unix/Linux style (\\n)
    - CR: Classic Mac style (\\r) - rarely used
    - Mixed: File contains multiple line ending types
    - None: Empty file or single line without line ending
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [ValidateSet('PowerShell', 'CSharp', 'Mixed', 'All', 'Custom')]
        [string] $ProjectType = 'Mixed',

        [string[]] $CustomExtensions,

        [string[]] $ExcludeDirectories = @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode'),

        [switch] $GroupByLineEnding,
        [switch] $ShowFiles,
        [switch] $CheckMixed,
        [string] $ExportPath
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

    # Determine file patterns to analyze
    if ($ProjectType -eq 'Custom' -and $CustomExtensions) {
        $filePatterns = $CustomExtensions
    } else {
        $filePatterns = $extensionMappings[$ProjectType]
    }

    Write-Host "Analyzing project line endings..." -ForegroundColor Cyan
    Write-Verbose "Project type: $ProjectType with patterns: $($filePatterns -join ', ')"    # Helper function to detect line endings and final newline

    # Collect all files to analyze
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

    if ($uniqueFiles.Count -eq 0) {
        Write-Warning "No files found matching the specified criteria"
        return
    }

    Write-Host "Analyzing $($uniqueFiles.Count) files..." -ForegroundColor Green

    # Analyze each file
    $fileDetails = @()
    $lineEndingStats = @{}
    $extensionStats = @{}
    $problemFiles = @()
    $filesWithoutFinalNewline = @()

    foreach ($file in $uniqueFiles) {
        try {
            $lineEndingInfo = Get-LineEndingType -FilePath $file.FullName
            $lineEndingType = $lineEndingInfo.LineEnding
            $hasFinalNewline = $lineEndingInfo.HasFinalNewline
            $extension = $file.Extension.ToLower()
            $relativePath = [System.IO.Path]::GetRelativePath($Path, $file.FullName)

            $fileDetail = [PSCustomObject]@{
                RelativePath    = $relativePath
                FullPath        = $file.FullName
                Extension       = $extension
                LineEnding      = $lineEndingType
                HasFinalNewline = $hasFinalNewline
                Size            = $file.Length
                LastModified    = $file.LastWriteTime
                Directory       = $file.DirectoryName
            }

            $fileDetails += $fileDetail

            # Track problem files
            if ($lineEndingType -eq 'Mixed' -or ($CheckMixed -and $lineEndingType -eq 'Mixed')) {
                $problemFiles += $fileDetail
            }

            # Track files without final newlines (excluding empty files and certain types)
            if (-not $hasFinalNewline -and $file.Length -gt 0 -and $extension -in @('.ps1', '.psm1', '.psd1', '.cs', '.js', '.py', '.rb', '.java', '.cpp', '.h', '.hpp', '.sql', '.md', '.txt', '.yaml', '.yml')) {
                $filesWithoutFinalNewline += $fileDetail
            }

            # Update line ending statistics
            if (-not $lineEndingStats.ContainsKey($lineEndingType)) {
                $lineEndingStats[$lineEndingType] = 0
            }
            $lineEndingStats[$lineEndingType]++

            # Update extension statistics
            if (-not $extensionStats.ContainsKey($extension)) {
                $extensionStats[$extension] = @{}
            }
            if (-not $extensionStats[$extension].ContainsKey($lineEndingType)) {
                $extensionStats[$extension][$lineEndingType] = 0
            }
            $extensionStats[$extension][$lineEndingType]++

        } catch {
            Write-Warning "Failed to analyze $($file.FullName): $_"
        }
    }

    # Generate summary statistics
    $totalFiles = $fileDetails.Count
    $uniqueLineEndings = $lineEndingStats.Keys | Sort-Object
    $mostCommonLineEnding = ($lineEndingStats.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key
    $inconsistentExtensions = @()

    # Find extensions with mixed line endings
    foreach ($ext in $extensionStats.Keys) {
        if ($extensionStats[$ext].Count -gt 1) {
            $inconsistentExtensions += $ext
        }
    }

    # Create summary report
    $summary = [PSCustomObject]@{
        ProjectPath              = $Path
        ProjectType              = $ProjectType
        TotalFiles               = $totalFiles
        UniqueLineEndings        = $uniqueLineEndings
        LineEndingCount          = $uniqueLineEndings.Count
        MostCommonLineEnding     = $mostCommonLineEnding
        InconsistentExtensions   = $inconsistentExtensions
        ProblemFiles             = $problemFiles.Count
        FilesWithoutFinalNewline = $filesWithoutFinalNewline.Count
        LineEndingDistribution   = $lineEndingStats
        ExtensionLineEndingMap   = $extensionStats
        AnalysisDate             = Get-Date
    }

    # Display summary
    Write-Host "`nLine Ending Analysis Summary:" -ForegroundColor Cyan
    Write-Host "  Total files analyzed: $totalFiles" -ForegroundColor White
    Write-Host "  Unique line endings found: $($uniqueLineEndings.Count)" -ForegroundColor White
    Write-Host "  Most common line ending: $mostCommonLineEnding ($($lineEndingStats[$mostCommonLineEnding]) files)" -ForegroundColor Green

    if ($problemFiles.Count -gt 0) {
        Write-Host "  ⚠️  Files with mixed line endings: $($problemFiles.Count)" -ForegroundColor Red
    }

    if ($filesWithoutFinalNewline.Count -gt 0) {
        Write-Host "  ⚠️  Files without final newline: $($filesWithoutFinalNewline.Count)" -ForegroundColor Yellow
    } else {
        Write-Host "  ✅ All files end with proper newlines" -ForegroundColor Green
    }

    if ($inconsistentExtensions.Count -gt 0) {
        Write-Host "  ⚠️  Extensions with mixed line endings: $($inconsistentExtensions -join ', ')" -ForegroundColor Yellow
    } else {
        Write-Host "  ✅ All file extensions have consistent line endings" -ForegroundColor Green
    }

    Write-Host "`nLine Ending Distribution:" -ForegroundColor Cyan
    foreach ($lineEnding in ($lineEndingStats.GetEnumerator() | Sort-Object Value -Descending)) {
        $percentage = [math]::Round(($lineEnding.Value / $totalFiles) * 100, 1)
        $color = switch ($lineEnding.Key) {
            'CRLF' { 'Green' }
            'LF' { 'Green' }
            'Mixed' { 'Red' }
            'CR' { 'Yellow' }
            'None' { 'Gray' }
            'Error' { 'Red' }
            default { 'White' }
        }
        Write-Host "  $($lineEnding.Key): $($lineEnding.Value) files ($percentage%)" -ForegroundColor $color
    }

    if ($problemFiles.Count -gt 0) {
        Write-Host "`nFiles with Mixed Line Endings:" -ForegroundColor Red
        foreach ($problemFile in $problemFiles | Select-Object -First 10) {
            Write-Host "  $($problemFile.RelativePath)" -ForegroundColor Yellow
        }
        if ($problemFiles.Count -gt 10) {
            Write-Host "  ... and $($problemFiles.Count - 10) more files" -ForegroundColor Yellow
        }
    }

    if ($filesWithoutFinalNewline.Count -gt 0) {
        Write-Host "`nFiles Missing Final Newline:" -ForegroundColor Yellow
        foreach ($missingFile in $filesWithoutFinalNewline | Select-Object -First 10) {
            Write-Host "  $($missingFile.RelativePath)" -ForegroundColor Yellow
        }
        if ($filesWithoutFinalNewline.Count -gt 10) {
            Write-Host "  ... and $($filesWithoutFinalNewline.Count - 10) more files" -ForegroundColor Yellow
        }
    }

    if ($inconsistentExtensions.Count -gt 0) {
        Write-Host "`nExtensions with Mixed Line Endings:" -ForegroundColor Yellow
        foreach ($ext in $inconsistentExtensions) {
            Write-Host "  ${ext}:" -ForegroundColor Yellow
            foreach ($lineEnding in ($extensionStats[$ext].GetEnumerator() | Sort-Object Value -Descending)) {
                Write-Host "    $($lineEnding.Key): $($lineEnding.Value) files" -ForegroundColor White
            }
        }
    }

    # Prepare return object
    $report = [PSCustomObject]@{
        Summary             = $summary
        Files               = if ($ShowFiles) { $fileDetails } else { $null }
        ProblemFiles        = $problemFiles
        GroupedByLineEnding = if ($GroupByLineEnding) {
            $grouped = @{}
            foreach ($lineEnding in $uniqueLineEndings) {
                $grouped[$lineEnding] = $fileDetails | Where-Object { $_.LineEnding -eq $lineEnding }
            }
            $grouped
        } else { $null }
    }

    # Export to CSV if requested
    if ($ExportPath) {
        try {
            $fileDetails | Export-Csv -Path $ExportPath -NoTypeInformation -Encoding UTF8
            Write-Host "`nDetailed report exported to: $ExportPath" -ForegroundColor Green
        } catch {
            Write-Warning "Failed to export report to $ExportPath`: $_"
        }
    }

    return $report
}

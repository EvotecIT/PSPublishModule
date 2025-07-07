function Get-ProjectConsistency {
    <#
    .SYNOPSIS
    Provides comprehensive analysis of encoding and line ending consistency across a project.

    .DESCRIPTION
    Combines encoding and line ending analysis to provide a complete picture of file consistency
    across a project. Identifies issues and provides recommendations for standardization.
    This is the main analysis function that should be run before any bulk conversions.

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

    .PARAMETER RecommendedEncoding
    The encoding standard you want to achieve.
    Default is 'UTF8BOM' for PowerShell projects (PS 5.1 compatibility), 'UTF8' for others.

    .PARAMETER RecommendedLineEnding
    The line ending standard you want to achieve. Default is 'CRLF' on Windows, 'LF' on Unix.

    .PARAMETER ShowDetails
    Include detailed file-by-file analysis in the output.

    .PARAMETER ExportPath
    Export the detailed report to a CSV file at the specified path.

    .EXAMPLE
    Get-ProjectConsistencyReport -Path 'C:\MyProject' -ProjectType PowerShell
    Analyze consistency in a PowerShell project with UTF8BOM encoding (PS 5.1 compatible).

    .EXAMPLE
    Get-ProjectConsistencyReport -Path 'C:\MyProject' -ProjectType Mixed -RecommendedEncoding UTF8BOM -RecommendedLineEnding LF -ShowDetails
    Analyze a mixed project with specific recommendations and detailed output.

    .EXAMPLE
    Get-ProjectConsistencyReport -Path 'C:\MyProject' -ProjectType CSharp -RecommendedEncoding UTF8 -ExportPath 'C:\Reports\consistency-report.csv'
    Analyze a C# project (UTF8 without BOM is fine) with CSV export.

    .NOTES
    This function combines the analysis from Get-ProjectEncoding and Get-ProjectLineEnding
    to provide a unified view of project file consistency. Use this before running conversion functions.

    Encoding Recommendations:
    - PowerShell: UTF8BOM (required for PS 5.1 compatibility with special characters)
    - C#: UTF8 (BOM not needed, Visual Studio handles UTF8 correctly)
    - Mixed: UTF8BOM (safest for cross-platform PowerShell compatibility)

    PowerShell 5.1 Compatibility:
    UTF8 without BOM can cause PowerShell 5.1 to misinterpret files as ASCII, leading to:
    - Broken special characters and accented letters
    - Module import failures
    - Incorrect string processing
    UTF8BOM ensures proper encoding detection across all PowerShell versions.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [ValidateSet('PowerShell', 'CSharp', 'Mixed', 'All', 'Custom')]
        [string] $ProjectType = 'Mixed',

        [string[]] $CustomExtensions,

        [string[]] $ExcludeDirectories = @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode'),

        [ValidateSet('Ascii', 'BigEndianUnicode', 'Unicode', 'UTF7', 'UTF8', 'UTF8BOM', 'UTF32', 'Default', 'OEM')]
        [string] $RecommendedEncoding = $(
            if ($ProjectType -eq 'PowerShell') { 'UTF8BOM' }
            elseif ($ProjectType -eq 'Mixed') { 'UTF8BOM' }  # Default to PowerShell-safe for mixed projects
            else { 'UTF8' }
        ),

        [ValidateSet('CRLF', 'LF')]
        [string] $RecommendedLineEnding = $(if ($IsWindows) { 'CRLF' } else { 'LF' }),

        [switch] $ShowDetails,
        [string] $ExportPath
    )

    Write-Host "🔍 Analyzing project consistency..." -ForegroundColor Cyan
    Write-Host "Project: $Path" -ForegroundColor White
    Write-Host "Type: $ProjectType" -ForegroundColor White
    Write-Host "Target encoding: $RecommendedEncoding" -ForegroundColor White
    Write-Host "Target line ending: $RecommendedLineEnding" -ForegroundColor White

    # Get encoding analysis
    Write-Host "`n📝 Analyzing file encodings..." -ForegroundColor Yellow
    $encodingParams = @{
        Path = $Path
        ProjectType = $ProjectType
        ExcludeDirectories = $ExcludeDirectories
        ShowFiles = $true
    }
    if ($ProjectType -eq 'Custom' -and $CustomExtensions) {
        $encodingParams.CustomExtensions = $CustomExtensions
    }

    $encodingReport = Get-ProjectEncoding @encodingParams

    # Get line ending analysis
    Write-Host "`n📏 Analyzing line endings..." -ForegroundColor Yellow
    $lineEndingParams = @{
        Path = $Path
        ProjectType = $ProjectType
        ExcludeDirectories = $ExcludeDirectories
        ShowFiles = $true
        CheckMixed = $true
    }
    if ($ProjectType -eq 'Custom' -and $CustomExtensions) {
        $lineEndingParams.CustomExtensions = $CustomExtensions
    }

    $lineEndingReport = Get-ProjectLineEnding @lineEndingParams

    # Combine analysis
    Write-Host "`n🔄 Combining analysis..." -ForegroundColor Yellow

    # Create comprehensive file details
    $allFiles = @()
    foreach ($encFile in $encodingReport.Files) {
        $leFile = $lineEndingReport.Files | Where-Object { $_.FullPath -eq $encFile.FullPath }

        if ($leFile) {            $needsEncodingConversion = $encFile.Encoding -ne $RecommendedEncoding
            $needsLineEndingConversion = $leFile.LineEnding -ne $RecommendedLineEnding -and $leFile.LineEnding -ne 'None'
            $hasMixedLineEndings = $leFile.LineEnding -eq 'Mixed'
            $missingFinalNewline = -not $leFile.HasFinalNewline -and $encFile.Size -gt 0 -and $encFile.Extension -in @('.ps1', '.psm1', '.psd1', '.cs', '.js', '.py', '.rb', '.java', '.cpp', '.h', '.hpp', '.sql', '.md', '.txt', '.yaml', '.yml')

            $fileDetail = [PSCustomObject]@{
                RelativePath = $encFile.RelativePath
                FullPath = $encFile.FullPath
                Extension = $encFile.Extension
                CurrentEncoding = $encFile.Encoding
                CurrentLineEnding = $leFile.LineEnding
                RecommendedEncoding = $RecommendedEncoding
                RecommendedLineEnding = $RecommendedLineEnding
                NeedsEncodingConversion = $needsEncodingConversion
                NeedsLineEndingConversion = $needsLineEndingConversion
                HasMixedLineEndings = $hasMixedLineEndings
                MissingFinalNewline = $missingFinalNewline
                HasIssues = $needsEncodingConversion -or $needsLineEndingConversion -or $hasMixedLineEndings -or $missingFinalNewline
                Size = $encFile.Size
                LastModified = $encFile.LastModified
                Directory = $encFile.Directory
            }

            $allFiles += $fileDetail
        }
    }

    # Calculate comprehensive statistics
    $totalFiles = $allFiles.Count
    $filesNeedingEncodingConversion = ($allFiles | Where-Object { $_.NeedsEncodingConversion }).Count
    $filesNeedingLineEndingConversion = ($allFiles | Where-Object { $_.NeedsLineEndingConversion }).Count
    $filesWithMixedLineEndings = ($allFiles | Where-Object { $_.HasMixedLineEndings }).Count
    $filesMissingFinalNewline = ($allFiles | Where-Object { $_.MissingFinalNewline }).Count
    $filesWithIssues = ($allFiles | Where-Object { $_.HasIssues }).Count
    $filesCompliant = $totalFiles - $filesWithIssues

    # Identify problematic extensions
    $extensionIssues = @{}
    foreach ($file in ($allFiles | Where-Object { $_.HasIssues })) {
        if (-not $extensionIssues.ContainsKey($file.Extension)) {
            $extensionIssues[$file.Extension] = @{
                Total = 0
                EncodingIssues = 0
                LineEndingIssues = 0
                MixedLineEndings = 0
            }
        }
        $extensionIssues[$file.Extension].Total++
        if ($file.NeedsEncodingConversion) { $extensionIssues[$file.Extension].EncodingIssues++ }
        if ($file.NeedsLineEndingConversion) { $extensionIssues[$file.Extension].LineEndingIssues++ }
        if ($file.HasMixedLineEndings) { $extensionIssues[$file.Extension].MixedLineEndings++ }
    }

    # Create comprehensive summary
    $summary = [PSCustomObject]@{
        ProjectPath = $Path
        ProjectType = $ProjectType
        AnalysisDate = Get-Date

        # File counts
        TotalFiles = $totalFiles
        FilesCompliant = $filesCompliant
        FilesWithIssues = $filesWithIssues
        CompliancePercentage = [math]::Round(($filesCompliant / $totalFiles) * 100, 1)

        # Encoding statistics
        CurrentEncodingDistribution = $encodingReport.Summary.EncodingDistribution
        FilesNeedingEncodingConversion = $filesNeedingEncodingConversion
        RecommendedEncoding = $RecommendedEncoding

        # Line ending statistics
        CurrentLineEndingDistribution = $lineEndingReport.Summary.LineEndingDistribution
        FilesNeedingLineEndingConversion = $filesNeedingLineEndingConversion
        FilesWithMixedLineEndings = $filesWithMixedLineEndings
        FilesMissingFinalNewline = $filesMissingFinalNewline
        RecommendedLineEnding = $RecommendedLineEnding

        # Issues by extension
        ExtensionIssues = $extensionIssues
    }

    # Display comprehensive summary
    Write-Host "`n📊 Project Consistency Summary:" -ForegroundColor Cyan
    Write-Host "  Total files analyzed: $totalFiles" -ForegroundColor White
    Write-Host "  Files compliant with standards: $filesCompliant ($($summary.CompliancePercentage)%)" -ForegroundColor $(if ($summary.CompliancePercentage -ge 90) { 'Green' } elseif ($summary.CompliancePercentage -ge 70) { 'Yellow' } else { 'Red' })
    Write-Host "  Files needing attention: $filesWithIssues" -ForegroundColor $(if ($filesWithIssues -eq 0) { 'Green' } else { 'Red' })

    Write-Host "`n📝 Encoding Issues:" -ForegroundColor Cyan
    Write-Host "  Files needing encoding conversion: $filesNeedingEncodingConversion" -ForegroundColor $(if ($filesNeedingEncodingConversion -eq 0) { 'Green' } else { 'Yellow' })
    Write-Host "  Target encoding: $RecommendedEncoding" -ForegroundColor White

    Write-Host "`n📏 Line Ending Issues:" -ForegroundColor Cyan
    Write-Host "  Files needing line ending conversion: $filesNeedingLineEndingConversion" -ForegroundColor $(if ($filesNeedingLineEndingConversion -eq 0) { 'Green' } else { 'Yellow' })
    Write-Host "  Files with mixed line endings: $filesWithMixedLineEndings" -ForegroundColor $(if ($filesWithMixedLineEndings -eq 0) { 'Green' } else { 'Red' })
    Write-Host "  Files missing final newline: $filesMissingFinalNewline" -ForegroundColor $(if ($filesMissingFinalNewline -eq 0) { 'Green' } else { 'Yellow' })
    Write-Host "  Target line ending: $RecommendedLineEnding" -ForegroundColor White

    if ($extensionIssues.Count -gt 0) {
        Write-Host "`n⚠️  Extensions with Issues:" -ForegroundColor Yellow
        foreach ($ext in ($extensionIssues.GetEnumerator() | Sort-Object { $_.Value.Total } -Descending)) {
            Write-Host "  ${ext.Key}: $($ext.Value.Total) files" -ForegroundColor White
            if ($ext.Value.EncodingIssues -gt 0) {
                Write-Host "    - Encoding issues: $($ext.Value.EncodingIssues)" -ForegroundColor Yellow
            }
            if ($ext.Value.LineEndingIssues -gt 0) {
                Write-Host "    - Line ending issues: $($ext.Value.LineEndingIssues)" -ForegroundColor Yellow
            }
            if ($ext.Value.MixedLineEndings -gt 0) {
                Write-Host "    - Mixed line endings: $($ext.Value.MixedLineEndings)" -ForegroundColor Red
            }
        }
    }

    # Recommendations
    Write-Host "`n💡 Recommendations:" -ForegroundColor Green
    if ($filesWithIssues -eq 0) {
        Write-Host "  ✅ Your project is fully compliant! No action needed." -ForegroundColor Green
    } else {
        if ($filesWithMixedLineEndings -gt 0) {
            Write-Host "  🔴 Priority 1: Fix mixed line endings first" -ForegroundColor Red
            Write-Host "     Convert-ProjectLineEnding -Path '$Path' -ProjectType $ProjectType -TargetLineEnding $RecommendedLineEnding -OnlyMixed -CreateBackups" -ForegroundColor Gray
        }
        if ($filesNeedingEncodingConversion -gt 0) {
            Write-Host "  🟡 Priority 2: Standardize encoding" -ForegroundColor Yellow
            Write-Host "     Convert-ProjectEncoding -Path '$Path' -ProjectType $ProjectType -TargetEncoding $RecommendedEncoding -CreateBackups" -ForegroundColor Gray
        }
        if ($filesNeedingLineEndingConversion -gt 0) {
            Write-Host "  🟡 Priority 3: Standardize line endings" -ForegroundColor Yellow
            Write-Host "     Convert-ProjectLineEnding -Path '$Path' -ProjectType $ProjectType -TargetLineEnding $RecommendedLineEnding -CreateBackups" -ForegroundColor Gray
        }
        if ($filesMissingFinalNewline -gt 0) {
            Write-Host "  🟡 Priority 4: Add missing final newlines" -ForegroundColor Yellow
            Write-Host "     Convert-ProjectLineEnding -Path '$Path' -ProjectType $ProjectType -TargetLineEnding $RecommendedLineEnding -EnsureFinalNewline -OnlyMissingNewline -CreateBackups" -ForegroundColor Gray
        }
        Write-Host "  💾 Always use -WhatIf first and -CreateBackups for safety!" -ForegroundColor Cyan
    }

    # Prepare return object
    $report = [PSCustomObject]@{
        Summary = $summary
        EncodingReport = $encodingReport
        LineEndingReport = $lineEndingReport
        Files = if ($ShowDetails) { $allFiles } else { $null }
        ProblematicFiles = $allFiles | Where-Object { $_.HasIssues }
    }

    # Export to CSV if requested
    if ($ExportPath) {
        try {
            $allFiles | Export-Csv -Path $ExportPath -NoTypeInformation -Encoding UTF8
            Write-Host "`nDetailed report exported to: $ExportPath" -ForegroundColor Green
        } catch {
            Write-Warning "Failed to export report to $ExportPath`: $_"
        }
    }

    return $report
}

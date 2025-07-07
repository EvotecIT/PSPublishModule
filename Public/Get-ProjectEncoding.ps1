function Get-ProjectEncoding {
    <#
    .SYNOPSIS
    Analyzes encoding consistency across all files in a project directory.

    .DESCRIPTION
    Scans all relevant files in a project directory and provides a comprehensive report on file encodings.
    Identifies inconsistencies, potential issues, and provides recommendations for standardization.
    Useful for auditing projects before performing encoding conversions.

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

    .PARAMETER GroupByEncoding
    Group results by encoding type for easier analysis.

    .PARAMETER ShowFiles
    Include individual file details in the report.

    .PARAMETER ExportPath
    Export the detailed report to a CSV file at the specified path.

    .EXAMPLE
    Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType PowerShell
    Analyze encoding consistency in a PowerShell project.

    .EXAMPLE
    Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType Mixed -GroupByEncoding -ShowFiles
    Get detailed encoding report grouped by encoding type with individual file listings.

    .EXAMPLE
    Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType All -ExportPath 'C:\Reports\encoding-report.csv'
    Analyze all file types and export detailed report to CSV.

    .NOTES
    This function is read-only and does not modify any files. Use Convert-ProjectEncoding to standardize encodings.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [ValidateSet('PowerShell', 'CSharp', 'Mixed', 'All', 'Custom')]
        [string] $ProjectType = 'Mixed',

        [string[]] $CustomExtensions,

        [string[]] $ExcludeDirectories = @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode'),

        [switch] $GroupByEncoding,
        [switch] $ShowFiles,
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

    Write-Host "Analyzing project encoding..." -ForegroundColor Cyan
    Write-Verbose "Project type: $ProjectType with patterns: $($filePatterns -join ', ')"

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
    $encodingStats = @{}
    $extensionStats = @{}

    foreach ($file in $uniqueFiles) {
        try {
            $encodingInfo = Get-FileEncoding -Path $file.FullName -AsObject
            $extension = $file.Extension.ToLower()
            $relativePath = Get-RelativePath -From $Path -To $file.FullName

            $fileDetail = [PSCustomObject]@{
                RelativePath = $relativePath
                FullPath = $file.FullName
                Extension = $extension
                Encoding = $encodingInfo.EncodingName
                Size = $file.Length
                LastModified = $file.LastWriteTime
                Directory = $file.DirectoryName
            }

            $fileDetails += $fileDetail

            # Update encoding statistics
            if (-not $encodingStats.ContainsKey($encodingInfo.EncodingName)) {
                $encodingStats[$encodingInfo.EncodingName] = 0
            }
            $encodingStats[$encodingInfo.EncodingName]++

            # Update extension statistics
            if (-not $extensionStats.ContainsKey($extension)) {
                $extensionStats[$extension] = @{}
            }
            if (-not $extensionStats[$extension].ContainsKey($encodingInfo.EncodingName)) {
                $extensionStats[$extension][$encodingInfo.EncodingName] = 0
            }
            $extensionStats[$extension][$encodingInfo.EncodingName]++

        } catch {
            Write-Warning "Failed to analyze $($file.FullName): $_"
        }
    }

    # Generate summary statistics
    $totalFiles = $fileDetails.Count
    $uniqueEncodings = $encodingStats.Keys | Sort-Object
    $mostCommonEncoding = ($encodingStats.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key
    $inconsistentExtensions = @()

    # Find extensions with mixed encodings
    foreach ($ext in $extensionStats.Keys) {
        if ($extensionStats[$ext].Count -gt 1) {
            $inconsistentExtensions += $ext
        }
    }

    # Create summary report
    $summary = [PSCustomObject]@{
        ProjectPath = $Path
        ProjectType = $ProjectType
        TotalFiles = $totalFiles
        UniqueEncodings = $uniqueEncodings
        EncodingCount = $uniqueEncodings.Count
        MostCommonEncoding = $mostCommonEncoding
        InconsistentExtensions = $inconsistentExtensions
        EncodingDistribution = $encodingStats
        ExtensionEncodingMap = $extensionStats
        AnalysisDate = Get-Date
    }

    # Display summary
    Write-Host "`nEncoding Analysis Summary:" -ForegroundColor Cyan
    Write-Host "  Total files analyzed: $totalFiles" -ForegroundColor White
    Write-Host "  Unique encodings found: $($uniqueEncodings.Count)" -ForegroundColor White

    if ($totalFiles -gt 0 -and $mostCommonEncoding) {
        Write-Host "  Most common encoding: $mostCommonEncoding ($($encodingStats[$mostCommonEncoding]) files)" -ForegroundColor Green
    } elseif ($totalFiles -eq 0) {
        Write-Host "  No files found for analysis" -ForegroundColor Yellow
        return $result
    } else {
        Write-Host "  No encoding information available" -ForegroundColor Yellow
    }

    if ($inconsistentExtensions.Count -gt 0) {
        Write-Host "  ⚠️  Extensions with mixed encodings: $($inconsistentExtensions -join ', ')" -ForegroundColor Yellow
    } else {
        Write-Host "  ✅ All file extensions have consistent encodings" -ForegroundColor Green
    }

    Write-Host "`nEncoding Distribution:" -ForegroundColor Cyan
    foreach ($encoding in ($encodingStats.GetEnumerator() | Sort-Object Value -Descending)) {
        $percentage = [math]::Round(($encoding.Value / $totalFiles) * 100, 1)
        Write-Host "  $($encoding.Key): $($encoding.Value) files ($percentage%)" -ForegroundColor White
    }

    if ($inconsistentExtensions.Count -gt 0) {
        Write-Host "`nExtensions with Mixed Encodings:" -ForegroundColor Yellow
        foreach ($ext in $inconsistentExtensions) {
            Write-Host "  ${ext}:" -ForegroundColor Yellow
            foreach ($encoding in ($extensionStats[$ext].GetEnumerator() | Sort-Object Value -Descending)) {
                Write-Host "    $($encoding.Key): $($encoding.Value) files" -ForegroundColor White
            }
        }
    }

    # Prepare return object
    $report = [PSCustomObject]@{
        Summary = $summary
        Files = if ($ShowFiles) { $fileDetails } else { $null }
        GroupedByEncoding = if ($GroupByEncoding) {
            $grouped = @{}
            foreach ($encoding in $uniqueEncodings) {
                $grouped[$encoding] = $fileDetails | Where-Object { $_.Encoding -eq $encoding }
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

function Get-PowerShellCompatibility {
    <#
    .SYNOPSIS
    Analyzes PowerShell files and folders to determine compatibility with PowerShell 5.1 and PowerShell 7.

    .DESCRIPTION
    Scans PowerShell files to detect features, cmdlets, and patterns that are specific to PowerShell 5.1 (Windows PowerShell)
    or PowerShell 7 (PowerShell Core). Identifies potential compatibility issues and provides recommendations for cross-version support.

    .PARAMETER Path
    Path to the file or directory to analyze for PowerShell compatibility.

    .PARAMETER Recurse
    When analyzing a directory, recursively analyze all subdirectories.

    .PARAMETER ExcludeDirectories
    Directory names to exclude from analysis (e.g., '.git', 'bin', 'obj', 'Artefacts').

    .PARAMETER ShowDetails
    Include detailed analysis of each file with specific compatibility issues found.

    .PARAMETER ExportPath
    Export the detailed report to a CSV file at the specified path.

    .EXAMPLE
    Get-PowerShellCompatibility -Path 'C:\MyModule'
    Analyzes all PowerShell files in the specified directory for compatibility issues.

    .EXAMPLE
    Get-PowerShellCompatibility -Path 'C:\MyModule' -Recurse -ShowDetails
    Recursively analyzes all PowerShell files with detailed compatibility information.

    .EXAMPLE
    Get-PowerShellCompatibility -Path 'C:\MyModule\MyScript.ps1' -ShowDetails
    Analyzes a specific PowerShell file for compatibility issues.

    .EXAMPLE
    Get-PowerShellCompatibility -Path 'C:\MyModule' -ExportPath 'C:\Reports\compatibility.csv'
    Analyzes compatibility and exports detailed results to a CSV file.

    .NOTES
    This function identifies:
    - PowerShell 5.1 specific features (Windows PowerShell Desktop edition)
    - PowerShell 7 specific features (PowerShell Core)
    - Encoding issues that affect cross-version compatibility
    - .NET Framework vs .NET Core dependencies
    - Edition-specific cmdlets and parameters
    - Cross-platform compatibility concerns

    PowerShell 5.1 typically requires:
    - UTF8BOM encoding for proper character handling
    - Windows-specific cmdlets and .NET Framework
    - Desktop edition specific features

    PowerShell 7 supports:
    - UTF8 encoding (BOM optional)
    - Cross-platform cmdlets and .NET Core/.NET 5+
    - Enhanced language features and performance
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [switch] $Recurse,

        [string[]] $ExcludeDirectories = @('.git', '.vs', 'bin', 'obj', 'packages', 'node_modules', '.vscode', 'Artefacts', 'Ignore'),

        [switch] $ShowDetails,

        [string] $ExportPath,

        [switch] $Internal
    )

    if ($Internal) {
        Write-Verbose "Analyzing PowerShell compatibility for: $Path"
        Write-Verbose "Current PowerShell: $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)"
    } else {
        Write-Host "🔍 Analyzing PowerShell compatibility..." -ForegroundColor Cyan
        Write-Host "Path: $Path" -ForegroundColor White
        Write-Host "Current PowerShell: $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)" -ForegroundColor White
    }

    # Validate path
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Path not found: $Path"
    }

    # Determine if we're analyzing a file or directory
    $isFile = (Get-Item -LiteralPath $Path -Force).PSIsContainer -eq $false

    # Get files to analyze
    if ($isFile) {
        if ($Path -notmatch '\.(ps1|psm1|psd1)$') {
            throw "File must be a PowerShell file (.ps1, .psm1, or .psd1)"
        }
        $files = @(Get-Item -LiteralPath $Path -Force)
    } else {
        $searchParams = @{
            Path    = $Path
            Include = @('*.ps1', '*.psm1', '*.psd1')
            File    = $true
            Force   = $true
        }
        if ($Recurse) {
            $searchParams.Recurse = $true
        }

        $files = Get-ChildItem @searchParams | Where-Object {
            $exclude = $false
            foreach ($excludeDir in $ExcludeDirectories) {
                if ($_.DirectoryName -match [regex]::Escape($excludeDir)) {
                    $exclude = $true
                    break
                }
            }
            -not $exclude
        }
    }

    if ($files.Count -eq 0) {
        if ($Internal) {
            Write-Verbose "No PowerShell files found in the specified path."
        } else {
            Write-Warning "No PowerShell files found in the specified path."
        }
        return
    }

    if ($Internal) {
        Write-Verbose "Found $($files.Count) PowerShell files to analyze"
    } else {
        Write-Host "📁 Found $($files.Count) PowerShell files to analyze" -ForegroundColor Yellow
    }

    # Initialize results
    $results = @()
    $totalFiles = $files.Count
    $processedFiles = 0

    foreach ($file in $files) {
        $processedFiles++
        if (-not $Internal) {
            Write-Progress -Activity "Analyzing PowerShell Compatibility" -Status "Processing $($file.Name)" -PercentComplete (($processedFiles / $totalFiles) * 100)
        }

        $analysis = Get-PowerShellFileCompatibility -FilePath $file.FullName
        $results += $analysis
    }

    if (-not $Internal) {
        Write-Progress -Activity "Analyzing PowerShell Compatibility" -Completed
    }

    # Calculate summary statistics
    $ps51Compatible = ($results | Where-Object { $_.PowerShell51Compatible }).Count
    $ps7Compatible = ($results | Where-Object { $_.PowerShell7Compatible }).Count
    $crossCompatible = ($results | Where-Object { $_.PowerShell51Compatible -and $_.PowerShell7Compatible }).Count
    $filesWithIssues = ($results | Where-Object { $_.Issues.Count -gt 0 }).Count

    # Calculate cross compatibility percentage first
    $crossCompatibilityPercentage = [math]::Round(($crossCompatible / $totalFiles) * 100, 1)

    # Determine overall status
    $status = if ($filesWithIssues -eq 0) {
        'Pass'
    } elseif ($crossCompatibilityPercentage -ge 90) {
        'Warning'
    } else {
        'Fail'
    }

    # Create summary report
    $summary = [PSCustomObject]@{
        Status                       = $status
        AnalysisDate                 = Get-Date
        TotalFiles                   = $totalFiles
        PowerShell51Compatible       = $ps51Compatible
        PowerShell7Compatible        = $ps7Compatible
        CrossCompatible              = $crossCompatible
        FilesWithIssues              = $filesWithIssues
        CrossCompatibilityPercentage = $crossCompatibilityPercentage
        Message                      = switch ($status) {
            'Pass' { "All $totalFiles files are cross-compatible" }
            'Warning' { "$filesWithIssues files have compatibility issues but $($crossCompatibilityPercentage)% are cross-compatible" }
            'Fail' { "$filesWithIssues files have compatibility issues, only $($crossCompatibilityPercentage)% are cross-compatible" }
        }
        Recommendations              = if ($filesWithIssues -gt 0) {
            @(
                "Review files with compatibility issues",
                "Consider using UTF8BOM encoding for PowerShell 5.1 support",
                "Replace deprecated cmdlets with modern alternatives",
                "Test code in both PowerShell 5.1 and 7 environments"
            )
        } else { @() }
    }

    # Display summary
    if ($Internal) {
        Write-Verbose "PowerShell Compatibility: $($summary.Status) - $($summary.Message)"
        if ($summary.Status -ne 'Pass') {
            Write-Verbose "Recommendations: $($summary.Recommendations -join '; ')"
        }
    } else {
        Write-Host "`n📊 Compatibility Summary:" -ForegroundColor Green
        Write-Host "Status: $($summary.Status)" -ForegroundColor $(
            switch ($summary.Status) {
                'Pass' { 'Green' }
                'Warning' { 'Yellow' }
                'Fail' { 'Red' }
            }
        )
        Write-Host "Total files analyzed: $totalFiles" -ForegroundColor White
        Write-Host "PowerShell 5.1 compatible: $ps51Compatible" -ForegroundColor White
        Write-Host "PowerShell 7 compatible: $ps7Compatible" -ForegroundColor White
        Write-Host "Cross-compatible: $crossCompatible ($($crossCompatibilityPercentage)%)" -ForegroundColor White
        $ColorToUse = $(if ($filesWithIssues -gt 0) { 'Yellow' } else { 'Green' })
        Write-Host "Files with issues: $filesWithIssues" -ForegroundColor $ColorToUse

        if ($summary.Recommendations.Count -gt 0) {
            Write-Host "`n💡 Recommendations:" -ForegroundColor Cyan
            foreach ($recommendation in $summary.Recommendations) {
                Write-Host "  • $recommendation" -ForegroundColor Yellow
            }
        }
    }

    # Show detailed results if requested
    if ($ShowDetails -and -not $Internal) {
        Write-Host "`n📋 Detailed Analysis:" -ForegroundColor Cyan
        foreach ($result in $results) {
            Write-Host "`n🔸 $($result.RelativePath)" -ForegroundColor White
            Write-Host "  PS 5.1: $(if ($result.PowerShell51Compatible) { '✅' } else { '❌' })" -ForegroundColor $(if ($result.PowerShell51Compatible) { 'Green' } else { 'Red' })
            Write-Host "  PS 7:   $(if ($result.PowerShell7Compatible) { '✅' } else { '❌' })" -ForegroundColor $(if ($result.PowerShell7Compatible) { 'Green' } else { 'Red' })

            if ($result.Issues.Count -gt 0) {
                Write-Host "  Issues:" -ForegroundColor Yellow
                foreach ($issue in $result.Issues) {
                    Write-Host "    • $($issue.Type): $($issue.Description)" -ForegroundColor Red
                    if ($issue.Recommendation) {
                        Write-Host "      → $($issue.Recommendation)" -ForegroundColor Cyan
                    }
                }
            }
        }
    } elseif ($ShowDetails -and $Internal) {
        foreach ($result in $results) {
            if ($result.Issues.Count -gt 0) {
                Write-Verbose "Issues in $($result.RelativePath): $($result.Issues.Type -join ', ')"
            }
        }
    }

    # Export to CSV if requested
    if ($ExportPath) {
        $exportData = $results | Select-Object RelativePath, FullPath, PowerShell51Compatible, PowerShell7Compatible, Encoding, @{
            Name       = 'IssueCount'
            Expression = { $_.Issues.Count }
        }, @{
            Name       = 'IssueTypes'
            Expression = { ($_.Issues.Type -join ', ') }
        }, @{
            Name       = 'IssueDescriptions'
            Expression = { ($_.Issues.Description -join '; ') }
        }

        $exportData | Export-Csv -Path $ExportPath -NoTypeInformation -Encoding UTF8
        if ($Internal) {
            Write-Verbose "Detailed report exported to: $ExportPath"
        } else {
            Write-Host "📄 Detailed report exported to: $ExportPath" -ForegroundColor Green
        }
    }

    # Return results
    [PSCustomObject]@{
        Summary = $summary
        Files   = $results
    }
}
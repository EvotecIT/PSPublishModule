Import-Module .\PSPublishModule.psd1 -Force
$Output = Get-PowerShellCompatibility -Path "C:\Support\Github\ADEssentials" -Recurse
$Output | Format-List
$Output.Summary | Format-Table
$Output.Files | Format-Table 

return

Write-Host "=== PowerShell Compatibility Analysis Examples ===" -ForegroundColor Magenta

# Show current PowerShell version
Write-Host "`nCurrent PowerShell Environment:" -ForegroundColor Yellow
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)" -ForegroundColor Green
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Green

# Example 1: Analyze current module directory
Write-Host "`n=== 1. Analyzing Current Module Directory ===" -ForegroundColor Cyan
Get-PowerShellCompatibility -Path '.\Public' -ShowDetails | Format-List

# Example 2: Analyze a specific file
Write-Host "`n=== 2. Analyzing Specific File ===" -ForegroundColor Cyan
Get-PowerShellCompatibility -Path '.\Public\Get-PowerShellCompatibility.ps1' -ShowDetails | Format-List

# Example 3: Recursive analysis with export
Write-Host "`n=== 3. Recursive Analysis with CSV Export ===" -ForegroundColor Cyan
$reportPath = [IO.Path]::Combine($env:TEMP, "PowerShellCompatibilityReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv")
$result = Get-PowerShellCompatibility -Path '.\' -Recurse -ExportPath $reportPath -ExcludeDirectories @('Artefacts', 'Ignore', '.git', 'Tests')

Write-Host "`n📊 Analysis Summary:" -ForegroundColor Green
Write-Host "Total Files: $($result.Summary.TotalFiles)" -ForegroundColor White
Write-Host "Cross-Compatible: $($result.Summary.CrossCompatible) ($($result.Summary.CrossCompatibilityPercentage)%)" -ForegroundColor White
Write-Host "PowerShell 5.1 Only: $($result.Summary.PowerShell51Compatible - $result.Summary.CrossCompatible)" -ForegroundColor Yellow
Write-Host "PowerShell 7 Only: $($result.Summary.PowerShell7Compatible - $result.Summary.CrossCompatible)" -ForegroundColor Yellow
Write-Host "Files with Issues: $($result.Summary.FilesWithIssues)" -ForegroundColor $(if ($result.Summary.FilesWithIssues -gt 0) { 'Red' } else { 'Green' })

# Example 4: Check for specific compatibility issues
Write-Host "`n=== 4. Checking for Specific Issues ===" -ForegroundColor Cyan
$filesWithIssues = $result.Files | Where-Object { $_.Issues.Count -gt 0 }
if ($filesWithIssues.Count -gt 0) {
    Write-Host "Files with compatibility issues:" -ForegroundColor Yellow
    foreach ($file in $filesWithIssues) {
        Write-Host "  📄 $($file.RelativePath)" -ForegroundColor White
        $issueTypes = $file.Issues.Type | Sort-Object -Unique
        Write-Host "    Issue types: $($issueTypes -join ', ')" -ForegroundColor Red
    }

    # Group issues by type
    $issuesByType = $result.Files |
    ForEach-Object { $_.Issues } |
    Group-Object Type |
    Sort-Object Count -Descending

    Write-Host "`n📈 Issue Statistics:" -ForegroundColor Cyan
    foreach ($issueGroup in $issuesByType) {
        Write-Host "  $($issueGroup.Name): $($issueGroup.Count) occurrences" -ForegroundColor Yellow
    }
} else {
    Write-Host "✅ No compatibility issues found!" -ForegroundColor Green
}

# Example 5: Check encoding distribution
Write-Host "`n=== 5. Encoding Distribution ===" -ForegroundColor Cyan
$encodingStats = $result.Files | Group-Object Encoding | Sort-Object Count -Descending
foreach ($encoding in $encodingStats) {
    $percentage = [math]::Round(($encoding.Count / $result.Summary.TotalFiles) * 100, 1)
    Write-Host "  $($encoding.Name): $($encoding.Count) files ($($percentage)%)" -ForegroundColor White
}

# Example 6: Recommendations
Write-Host "`n=== 6. Recommendations ===" -ForegroundColor Cyan
if ($result.Summary.CrossCompatibilityPercentage -lt 100) {
    Write-Host "🔧 To improve cross-compatibility:" -ForegroundColor Yellow
    Write-Host "  • Review files with compatibility issues" -ForegroundColor White
    Write-Host "  • Consider using UTF8BOM encoding for PowerShell 5.1 support" -ForegroundColor White
    Write-Host "  • Replace deprecated cmdlets with modern alternatives" -ForegroundColor White
    Write-Host "  • Test code in both PowerShell 5.1 and 7 environments" -ForegroundColor White
    Write-Host "  • Consider version-specific conditional logic where needed" -ForegroundColor White
} else {
    Write-Host "✅ All files appear to be cross-compatible!" -ForegroundColor Green
}

Write-Host "`n=== Analysis Complete ===" -ForegroundColor Green
Write-Host "Report exported to: $reportPath" -ForegroundColor Cyan
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)" -ForegroundColor Yellow
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Yellow
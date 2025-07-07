Import-Module .\PSPublishModule.psd1 -Force

# NOW WITH BETTER DEFAULTS! Files and Recommendations are included by default
Write-Host "=== Basic Analysis (now includes files and recommendations by default!) ===" -ForegroundColor Green
$result = Get-FolderEncoding -Path .\Private -Extensions 'ps1'
$result | Format-List

return

Write-Host "`nSummary:" -ForegroundColor Yellow
$result.Summary

Write-Host "`nEncoding Distribution:" -ForegroundColor Yellow
$result.EncodingDistribution

Write-Host "`nRecommendations (now included by default!):" -ForegroundColor Yellow
$result.Recommendations

Write-Host "`nFiles (first 5, now included by default!):" -ForegroundColor Yellow
$result.Files | Select-Object -First 5 | Format-Table RelativePath, CurrentEncoding, RecommendedEncoding, NeedsConversion

# If you want summary only (no files/recommendations), you can disable them
Write-Host "`n=== Summary Only (disabling files and recommendations) ===" -ForegroundColor Green
$summaryOnly = Get-FolderEncoding -Path .\Private -Extensions 'ps1' -ShowFiles:$false -RecommendTarget:$false
Write-Host "Files property: $($summaryOnly.Files -eq $null)"
Write-Host "Recommendations property: $($summaryOnly.Recommendations -eq $null)"

# Beautiful summary display
Write-Host "`n=== Beautiful Summary Display ===" -ForegroundColor Green
$result.DisplaySummary()
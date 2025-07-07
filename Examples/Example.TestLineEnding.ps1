Import-Module .\PSPublishModule.psd1 -Force

# Test line ending analysis
Write-Host "=== Line Ending Analysis Demo ===" -ForegroundColor Cyan
Get-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -GroupByLineEnding -ShowFiles -CheckMixed

# Test PowerShell 5.1 compatibility
Write-Host "`n=== PowerShell Compatibility Test ===" -ForegroundColor Cyan
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)" -ForegroundColor Yellow
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Yellow

# Test with different project types
Write-Host "`n=== Mixed Project Analysis ===" -ForegroundColor Cyan
Get-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType Mixed -ExcludeDirectories 'Artefacts', 'Ignore' -CheckMixed

Write-Host "`n=== Line Ending Analysis Complete! ===" -ForegroundColor Green
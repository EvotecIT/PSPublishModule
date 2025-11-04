Import-Module .\PSPublishModule.psd1 -Force

# Example 1: Preview build cleanup with WhatIf
Write-Host "=== Example 1: Preview build cleanup ===" -ForegroundColor Cyan
Remove-ProjectFiles -ProjectPath '.' -ProjectType Build -WhatIf

Write-Host "`n=== Example 2: Clean HTML files ===" -ForegroundColor Cyan
Remove-ProjectFiles -ProjectPath '.' -ProjectType Html -ShowProgress -WhatIf

Write-Host "`n=== Example 3: Custom cleanup with backup ===" -ForegroundColor Cyan
# Note: For custom patterns, use -IncludePatterns without -ProjectType
Remove-ProjectFiles -ProjectPath '.' -IncludePatterns @('*.tmp', '*.log', 'temp*') -CreateBackups -ShowProgress -WhatIf

Write-Host "`n=== Example 4: Clean all with RecycleBin ===" -ForegroundColor Cyan
Remove-ProjectFiles -ProjectPath '.' -ProjectType All -DeleteMethod RecycleBin -WhatIf

Write-Host "`n=== Example 5: Internal mode (verbose output only) ===" -ForegroundColor Cyan
Remove-ProjectFiles -ProjectPath '.' -ProjectType Temp -Internal -Verbose -WhatIf

Write-Host "`n=== Example 6: With PassThru for detailed results ===" -ForegroundColor Cyan
$results = Remove-ProjectFiles -ProjectPath '.' -ProjectType Logs -PassThru -WhatIf
Write-Host "Summary: $($results.Summary.TotalItems) items would be processed" -ForegroundColor Yellow

Write-Host "`n=== Example 7: Exclude specific patterns ===" -ForegroundColor Cyan
Remove-ProjectFiles -ProjectPath '.' -ProjectType Build -ExcludePatterns @('*.config', '*.json') -WhatIf

Write-Host "`n=== Example 8: Limited recursion depth ===" -ForegroundColor Cyan
Remove-ProjectFiles -ProjectPath '.' -ProjectType Temp -MaxDepth 2 -WhatIf -ShowProgress

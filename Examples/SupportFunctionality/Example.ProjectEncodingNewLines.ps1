Import-Module .\PSPublishModule.psd1 -Force

Write-Host "=== Testing All Project Analysis and Conversion Examples ===" -ForegroundColor Magenta

# Test encoding analysis
Write-Host "`n=== 1. Encoding Analysis ===" -ForegroundColor Cyan
Get-ProjectEncoding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -ExcludeDirectories 'Artefacts', 'Ignore'

# Test line ending analysis
Write-Host "`n=== 2. Line Ending Analysis ===" -ForegroundColor Cyan
Get-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -ExcludeDirectories 'Artefacts', 'Ignore'

# Test encoding conversion (WhatIf)
Write-Host "`n=== 3. Encoding Conversion Preview ===" -ForegroundColor Cyan
Convert-ProjectEncoding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetEncoding UTF8BOM -WhatIf:$true -ExcludeDirectories 'Artefacts', 'Ignore'

# Test line ending conversion (WhatIf)
Write-Host "`n=== 4. Line Ending Conversion Preview ===" -ForegroundColor Cyan
Convert-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetLineEnding CRLF -WhatIf:$true -ExcludeDirectories 'Artefacts', 'Ignore'

Write-Host "`n=== All Examples Completed Successfully! ===" -ForegroundColor Green
Write-Host "PowerShell Edition: $($PSVersionTable.PSEdition)" -ForegroundColor Yellow
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Yellow

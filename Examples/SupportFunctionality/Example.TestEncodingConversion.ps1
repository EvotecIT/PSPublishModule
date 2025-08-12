Import-Module .\PSPublishModule.psd1 -Force

# Example 1: Convert ALL files to UTF8BOM (recommended for PowerShell projects)
Write-Host "=== Convert ALL files to UTF8BOM (default behavior) ===" -ForegroundColor Cyan
Convert-ProjectEncoding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetEncoding UTF8BOM -WhatIf:$true -ExcludeDirectories 'Artefacts' -Verbose

# Example 2: Convert only ASCII files to UTF8BOM
Write-Host "`n=== Convert ONLY ASCII files to UTF8BOM ===" -ForegroundColor Cyan
Convert-ProjectEncoding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -SourceEncoding ASCII -TargetEncoding UTF8BOM -WhatIf:$true -ExcludeDirectories 'Artefacts' -Verbose

# Example 3: Convert from any encoding to UTF8 (no BOM) for cross-platform compatibility (this is going to break PowerShell 5.1 scripts that expect UTF8 with BOM)
Write-Host "`n=== Convert ALL files to UTF8 (no BOM) ===" -ForegroundColor Cyan
Convert-ProjectEncoding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetEncoding UTF8 -WhatIf:$true -ExcludeDirectories 'Artefacts' -Verbose
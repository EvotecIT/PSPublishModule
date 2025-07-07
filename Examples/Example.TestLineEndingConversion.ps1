Import-Module .\PSPublishModule.psd1 -Force

# Example 1: Convert ALL files to CRLF (Windows line endings) - recommended for Windows development
Write-Host "=== Convert ALL files to CRLF (Windows line endings) ===" -ForegroundColor Cyan
Convert-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetLineEnding CRLF -WhatIf:$true -ExcludeDirectories 'Artefacts', 'Ignore' -Verbose

# Example 2: Convert ALL files to LF (Unix/Linux line endings) - recommended for cross-platform
Write-Host "`n=== Convert ALL files to LF (Unix/Linux line endings) ===" -ForegroundColor Cyan
Convert-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetLineEnding LF -WhatIf:$true -ExcludeDirectories 'Artefacts', 'Ignore' -Verbose

# Example 3: Ensure all files have final newlines (POSIX compliance)
Write-Host "`n=== Ensure all files end with newline (POSIX compliance) ===" -ForegroundColor Cyan
Convert-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetLineEnding LF -EnsureFinalNewline -WhatIf:$true -ExcludeDirectories 'Artefacts', 'Ignore' -Verbose

# Example 4: Only fix files missing final newlines (keep current line ending style)
Write-Host "`n=== Fix ONLY files missing final newlines ===" -ForegroundColor Cyan
Convert-ProjectLineEnding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -TargetLineEnding CRLF -OnlyMissingNewline -WhatIf:$true -ExcludeDirectories 'Artefacts', 'Ignore' -Verbose
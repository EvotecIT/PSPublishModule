Import-Module "$PSScriptRoot\..\PSMaintenance.psd1" -Force

# Console-only view (default selection)
Get-ModuleDocumentation -Name 'PSPublishModule'

# Console: show all standard docs
Get-ModuleDocumentation -Name 'PSPublishModule' -Type All

# HTML export with grouped tabs (Scripts/Docs when present) - default opens browser
Show-ModuleDocumentation -Name 'PSPublishModule' -Type All

# Save to a chosen path without opening
Show-ModuleDocumentation -Name 'PSPublishModule' -Type All -Path (Join-Path $env:TEMP 'PSPublishModule-Docs.html') -DoNotShow

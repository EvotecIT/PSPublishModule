Import-Module "$PSScriptRoot\..\PSMaintenance.psd1" -Force

# Prefer latest docs from repository and open combined HTML
Show-ModuleDocumentation -Name 'PSPublishModule' -PreferRepository -OpenHtml

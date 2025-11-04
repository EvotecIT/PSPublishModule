Import-Module "$PSScriptRoot\..\PSMaintenance.psd1" -Force

$dest = Join-Path $env:TEMP 'Docs'
Install-ModuleDocumentation -Name 'PSPublishModule' -Path $dest -Layout ModuleAndVersion -Open -Verbose
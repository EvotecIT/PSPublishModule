Import-Module PowerGuardian -Force

$dest = Join-Path $env:TEMP 'Docs'
Install-ModuleDocumentation -Name 'PSPublishModule' -Path $dest -Layout ModuleAndVersion -Open -Verbose


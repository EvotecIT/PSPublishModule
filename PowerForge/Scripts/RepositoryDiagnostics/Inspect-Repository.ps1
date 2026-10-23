param([string]$Repository, [string]$Tool, [string]$ExpectedUri)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# POWERFORGE_REPOSITORY_DIAGNOSTICS
try {
    $moduleName = if ($Tool -eq 'PSResourceGet') { 'Microsoft.PowerShell.PSResourceGet' } else { 'PowerShellGet' }
    Import-Module $moduleName -ErrorAction Stop | Out-Null
} catch { }
Write-PFRepositoryDiagnostic -Repository $Repository -Tool $Tool -ExpectedUri $ExpectedUri

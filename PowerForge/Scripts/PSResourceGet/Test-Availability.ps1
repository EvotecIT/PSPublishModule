param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  $module = Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop -PassThru
  Write-Output 'PFPSRG::AVAILABLE::1'
  if ($module -and $module.Version) {
    $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$module.Version))
    Write-Output ('PFPSRG::VERSION::' + $b64)
  }
  exit 0
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

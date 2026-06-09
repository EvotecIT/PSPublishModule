param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  $module = Import-Module PowerShellGet -ErrorAction Stop -PassThru
  Write-Output 'PFPWSGET::AVAILABLE::1'
  if ($module -and $module.Version) {
    $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$module.Version))
    Write-Output ('PFPWSGET::VERSION::' + $b64)
  }
  exit 0
} catch {
  $msg = 'PowerShellGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPWSGET::ERROR::' + $b64)
  exit 3
}

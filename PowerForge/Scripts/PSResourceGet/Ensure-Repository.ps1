param(
  [string]$Name,
  [string]$Uri,
  [string]$TrustedFlag,
  [string]$Priority,
  [string]$ApiVersion
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

$existing = $null
try { $existing = Get-PSResourceRepository -Name $Name -ErrorAction SilentlyContinue } catch { $existing = $null }

$commonParams = @{ ErrorAction = 'Stop' }
$commonParams.Name = $Name
if ($TrustedFlag -eq '1') { $commonParams.Trusted = $true }
if (-not [string]::IsNullOrWhiteSpace($Priority)) { $commonParams.Priority = [int]$Priority }
if (-not [string]::IsNullOrWhiteSpace($ApiVersion)) { $commonParams.ApiVersion = $ApiVersion }

$isPSGallery = -not [string]::IsNullOrWhiteSpace($Name) -and ($Name.Trim().ToLowerInvariant() -eq 'psgallery')

try {
  $created = $false

  if ($isPSGallery) {
    if ($existing) {
      Set-PSResourceRepository @commonParams | Out-Null
    } else {
      $created = $true
      Register-PSResourceRepository -PSGallery -Force -ErrorAction Stop | Out-Null
      # Apply settings (Trusted/Priority/ApiVersion) after registration (PSGallery has a predefined Uri).
      Set-PSResourceRepository @commonParams | Out-Null
    }
  } else {
    $params = $commonParams.Clone()
    $params.Uri = $Uri
    if ($existing) {
      Set-PSResourceRepository @params | Out-Null
    } else {
      $created = $true
      Register-PSResourceRepository @params -Force | Out-Null
    }
  }

  Write-Output ('PFPSRG::REPO::CREATED::' + ($(if ($created) { '1' } else { '0' })))
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}


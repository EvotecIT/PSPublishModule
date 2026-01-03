param(
  [string]$Name,
  [string]$SourceUri,
  [string]$PublishUri,
  [string]$TrustedFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Enc([string]$s) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(([string]$s)))
}

try {
  Import-Module PowerShellGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'PowerShellGet not available: ' + $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFPWSGET::ERROR::' + $b64)
  exit 3
}

$existing = $null
try { $existing = Get-PSRepository -Name $Name -ErrorAction SilentlyContinue } catch { $existing = $null }

$policy = if ($TrustedFlag -eq '1') { 'Trusted' } else { 'Untrusted' }

try {
  $created = $false

  if ($Name -eq 'PSGallery') {
    # PSGallery has pre-defined locations; Register-PSRepository requires -Default and Set-PSRepository does not allow PublishLocation.
    if (-not $existing) {
      $created = $true
      Register-PSRepository -Default -ErrorAction Stop | Out-Null
    }
    Set-PSRepository -Name 'PSGallery' -InstallationPolicy $policy -ErrorAction Stop | Out-Null
  } else {
    if ($existing) {
      Set-PSRepository -Name $Name -SourceLocation $SourceUri -PublishLocation $PublishUri -InstallationPolicy $policy -ErrorAction Stop | Out-Null
    } else {
      $created = $true
      Register-PSRepository -Name $Name -SourceLocation $SourceUri -PublishLocation $PublishUri -InstallationPolicy $policy -ErrorAction Stop | Out-Null
    }
  }

  Write-Output ('PFPWSGET::REPO::CREATED::' + ($(if ($created) { '1' } else { '0' })))
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFPWSGET::ERROR::' + $b64)
  exit 1
}


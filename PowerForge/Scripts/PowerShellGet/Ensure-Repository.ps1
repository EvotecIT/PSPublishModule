param(
  [string]$Name,
  [string]$SourceUri,
  [string]$PublishUri,
  [string]$TrustedFlag,
  [string]$CredentialUser,
  [string]$CredentialSecret
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
$credential = $null
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

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
    $restoreParams = $null
    $params = @{
      Name = $Name
      SourceLocation = $SourceUri
      PublishLocation = $PublishUri
      InstallationPolicy = $policy
      ErrorAction = 'Stop'
    }
    if ($null -ne $credential) { $params.Credential = $credential }

    if ($existing) {
      $restoreParams = @{
        Name = $existing.Name
        SourceLocation = $existing.SourceLocation
        PublishLocation = $existing.PublishLocation
        InstallationPolicy = $existing.InstallationPolicy
        ErrorAction = 'Stop'
      }
    } else {
      $created = $true
    }

    try {
      if ($existing) {
        Unregister-PSRepository -Name $Name -ErrorAction SilentlyContinue | Out-Null
        Unregister-PackageSource -Name $Name -ProviderName NuGet -Force -ErrorAction SilentlyContinue | Out-Null
      }

      Register-PSRepository @params | Out-Null
    } catch {
      $originalMessage = $_.Exception.Message

      if ($null -ne $restoreParams) {
        try {
          Register-PSRepository @restoreParams | Out-Null
        } catch {
          $originalMessage = $originalMessage + ' Existing repository restore failed: ' + $_.Exception.Message
        }
      }

      throw $originalMessage
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

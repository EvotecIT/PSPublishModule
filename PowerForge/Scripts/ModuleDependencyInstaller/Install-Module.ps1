param(
  [string]$Name,
  [string]$RequiredVersion,
  [string]$MinimumVersion,
  [string]$Repository,
  [string]$CredentialUser,
  [string]$CredentialSecret
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Enc([string]$s) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(([string]$s)))
}

try {
  $params = @{
    Name = $Name
    Force = $true
    ErrorAction = 'Stop'
    SkipPublisherCheck = $true
    Scope = 'CurrentUser'
    AcceptLicense = $true
  }
  if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
  if (-not [string]::IsNullOrWhiteSpace($RequiredVersion)) { $params.RequiredVersion = $RequiredVersion }
  elseif (-not [string]::IsNullOrWhiteSpace($MinimumVersion)) { $params.MinimumVersion = $MinimumVersion }
  if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
    $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
    $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
  }

  Install-Module @params | Out-Null
  Write-Output 'PFMOD::INSTALL::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}


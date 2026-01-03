param(
  [string]$Path,
  [string]$Repository,
  [string]$ApiKey,
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

$params = @{ ErrorAction = 'Stop' }
$params.Path = $Path
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $params.NuGetApiKey = $ApiKey }
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

try {
  Publish-Module @params | Out-Null
  Write-Output 'PFPWSGET::PUBLISH::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFPWSGET::ERROR::' + $b64)
  exit 1
}


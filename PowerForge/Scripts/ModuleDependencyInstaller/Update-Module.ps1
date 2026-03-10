param(
  [string]$Name,
  [string]$PrereleaseFlag,
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
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 3
}

try {
  $params = @{
    Name = $Name
    Force = $true
    ErrorAction = 'Stop'
    Scope = 'CurrentUser'
    AcceptLicense = $true
  }
  if ($PrereleaseFlag -eq '1') { $params.AllowPrerelease = $true }
  if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
    $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
    $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
  }

  Update-Module @params | Out-Null
  Write-Output 'PFMOD::UPDATE::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}

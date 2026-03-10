param(
  [string]$Name,
  [string]$Repository,
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
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
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
    TrustRepository = $true
    SkipDependencyCheck = $false
    AcceptLicense = $true
    Quiet = $true
  }
  if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
  if ($PrereleaseFlag -eq '1') { $params.Prerelease = $true }
  if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
    $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
    $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
  }

  Update-PSResource @params | Out-Null
  Write-Output 'PFMOD::UPDATE::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}

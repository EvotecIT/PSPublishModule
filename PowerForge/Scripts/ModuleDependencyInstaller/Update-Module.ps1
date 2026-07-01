param(
  [string]$Name,
  [string]$PrereleaseFlag,
  [string]$CredentialUser,
  [string]$CredentialSecret,
  [string]$Scope
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch {
}

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
  }
  if ([string]::IsNullOrWhiteSpace($Scope)) { $params.Scope = 'CurrentUser' } else { $params.Scope = $Scope }
  if ($PrereleaseFlag -eq '1') { $params.AllowPrerelease = $true }
  $updateCommand = Get-Command Update-Module -ErrorAction Stop
  if ($updateCommand.Parameters.ContainsKey('AcceptLicense')) { $params.AcceptLicense = $true }
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

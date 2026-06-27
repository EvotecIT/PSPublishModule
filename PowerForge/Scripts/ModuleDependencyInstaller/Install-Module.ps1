param(
  [string]$Name,
  [string]$RequiredVersion,
  [string]$MinimumVersion,
  [string]$MaximumVersion,
  [string]$Repository,
  [string]$Scope,
  [string]$CredentialUser,
  [string]$CredentialSecret,
  [string]$PrereleaseFlag,
  [string]$AllowClobberFlag
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
    SkipPublisherCheck = $true
    AcceptLicense = $true
  }
  if ([string]::IsNullOrWhiteSpace($Scope)) { $params.Scope = 'CurrentUser' } else { $params.Scope = $Scope }
  if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
  if (-not [string]::IsNullOrWhiteSpace($RequiredVersion)) { $params.RequiredVersion = $RequiredVersion }
  elseif (-not [string]::IsNullOrWhiteSpace($MinimumVersion)) { $params.MinimumVersion = $MinimumVersion }
  if (-not [string]::IsNullOrWhiteSpace($MaximumVersion)) { $params.MaximumVersion = $MaximumVersion }
  if ($PrereleaseFlag -eq '1') {
    $installModuleCommand = Get-Command -Name Install-Module -ErrorAction Stop
    if ($installModuleCommand.Parameters.ContainsKey('AllowPrerelease')) {
      $params.AllowPrerelease = $true
    }
  }
  if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
    $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
    $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
  }
  if ($AllowClobberFlag -eq '1') { $params.AllowClobber = $true }

  Install-Module @params | Out-Null
  Write-Output 'PFMOD::INSTALL::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}

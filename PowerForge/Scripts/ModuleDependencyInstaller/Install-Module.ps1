param(
  [string]$Name,
  [string]$RequiredVersion,
  [string]$MinimumVersion,
  [string]$Repository,
  [string]$CredentialUser,
  [string]$CredentialSecret,
  [string]$AllowClobberFlag
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
  $nugetProvider = Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue
  if ($null -eq $nugetProvider) {
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope CurrentUser -Force -ErrorAction Stop | Out-Null
  }
  Import-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -ErrorAction SilentlyContinue | Out-Null
} catch {
  $msg = 'NuGet package provider is not available for PowerShellGet: ' + $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 4
}

try {
  $params = @{
    Name = $Name
    Force = $true
    ErrorAction = 'Stop'
    SkipPublisherCheck = $true
    Scope = 'CurrentUser'
  }
  if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
  if (-not [string]::IsNullOrWhiteSpace($RequiredVersion)) { $params.RequiredVersion = $RequiredVersion }
  elseif (-not [string]::IsNullOrWhiteSpace($MinimumVersion)) { $params.MinimumVersion = $MinimumVersion }
  $installCommand = Get-Command Install-Module -ErrorAction Stop
  if ($installCommand.Parameters.ContainsKey('AcceptLicense')) { $params.AcceptLicense = $true }
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

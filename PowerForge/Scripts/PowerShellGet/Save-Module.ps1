param(
  [string]$Name,
  [string]$MinimumVersion,
  [string]$RequiredVersion,
  [string]$Repository,
  [string]$Path,
  [string]$PrereleaseFlag,
  [string]$AcceptLicenseFlag,
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

$saveCmd = Get-Command Save-Module -ErrorAction Stop
$params = @{ ErrorAction = 'Stop' }
$params.Name = $Name
$params.Path = $Path
$params.Force = $true
if (-not [string]::IsNullOrWhiteSpace($RequiredVersion) -and $saveCmd.Parameters.ContainsKey('RequiredVersion')) { $params.RequiredVersion = $RequiredVersion }
elseif (-not [string]::IsNullOrWhiteSpace($MinimumVersion) -and $saveCmd.Parameters.ContainsKey('MinimumVersion')) { $params.MinimumVersion = $MinimumVersion }
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }

if ($PrereleaseFlag -eq '1' -and $saveCmd.Parameters.ContainsKey('AllowPrerelease')) { $params.AllowPrerelease = $true }
if ($AcceptLicenseFlag -eq '1' -and $saveCmd.Parameters.ContainsKey('AcceptLicense')) { $params.AcceptLicense = $true }

if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

try {
  Save-Module @params | Out-Null

  $moduleRoot = Join-Path $Path $Name
  $versionDirs = @()
  try { $versionDirs = Get-ChildItem -Path $moduleRoot -Directory -ErrorAction SilentlyContinue } catch { $versionDirs = @() }

  foreach ($d in @($versionDirs)) {
    $n = Enc ([string]$Name)
    $v = Enc ([string]$d.Name)
    Write-Output ('PFPWSGET::SAVE::ITEM::' + $n + '::' + $v)
  }

  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFPWSGET::ERROR::' + $b64)
  exit 1
}


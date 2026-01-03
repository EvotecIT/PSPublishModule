param(
  [string]$Path,
  [string]$IsNupkgFlag,
  [string]$Repository,
  [string]$ApiKey,
  [string]$DestinationPath,
  [string]$SkipDependenciesFlag,
  [string]$SkipManifestFlag,
  [string]$CredentialUser,
  [string]$CredentialSecret
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

$params = @{ ErrorAction = 'Stop' }
if ($IsNupkgFlag -eq '1') { $params.NupkgPath = $Path } else { $params.Path = $Path }
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $params.ApiKey = $ApiKey }
if (-not [string]::IsNullOrWhiteSpace($DestinationPath)) { $params.DestinationPath = $DestinationPath }
if ($SkipDependenciesFlag -eq '1') { $params.SkipDependenciesCheck = $true }
if ($SkipManifestFlag -eq '1') { $params.SkipModuleManifestValidate = $true }
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

try {
  Publish-PSResource @params | Out-Null
  Write-Output 'PFPSRG::PUBLISH::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}


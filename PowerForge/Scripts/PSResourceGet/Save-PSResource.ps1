param(
  [string]$Name,
  [string]$Version,
  [string]$Repository,
  [string]$Path,
  [string]$PrereleaseFlag,
  [string]$TrustRepositoryFlag,
  [string]$SkipDependencyFlag,
  [string]$AcceptLicenseFlag,
  [string]$QuietFlag,
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
$params.Name = $Name
$params.Path = $Path
if (-not [string]::IsNullOrWhiteSpace($Version)) { $params.Version = $Version }
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
if ($PrereleaseFlag -eq '1') { $params.Prerelease = $true }
if ($TrustRepositoryFlag -eq '1') { $params.TrustRepository = $true }
if ($SkipDependencyFlag -eq '1') { $params.SkipDependencyCheck = $true }
if ($AcceptLicenseFlag -eq '1') { $params.AcceptLicense = $true }
if ($QuietFlag -eq '1') { $params.Quiet = $true }
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

try {
  $saved = Save-PSResource @params -PassThru
  foreach ($r in @($saved)) {
    $n = [string]$r.Name
    $v = [string]$r.Version
    $fields = @($n, $v) | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFPSRG::SAVE::ITEM::' + ($fields -join '::'))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}


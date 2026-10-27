param(
  [string]$Name,
  [string]$Version,
  [string]$Repository,
  [string]$Scope,
  [string]$PrereleaseFlag,
  [string]$ReinstallFlag,
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
if (-not [string]::IsNullOrWhiteSpace($Version)) { $params.Version = $Version }
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
if (-not [string]::IsNullOrWhiteSpace($Scope)) { $params.Scope = $Scope }
if ($PrereleaseFlag -eq '1') { $params.Prerelease = $true }
if ($ReinstallFlag -eq '1') { $params.Reinstall = $true }
if ($TrustRepositoryFlag -eq '1') { $params.TrustRepository = $true }
if ($SkipDependencyFlag -eq '1') { $params.SkipDependencyCheck = $true }
if ($AcceptLicenseFlag -eq '1') { $params.AcceptLicense = $true }
if ($QuietFlag -eq '1') { $params.Quiet = $true }
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

# PSResourceGet can report a package-query 404 only on its verbose stream,
# then throw a generic InstallPackageFailure. Keep the cause for retry decisions.
$providerMessages = [System.Collections.Generic.List[string]]::new()
$Error.Clear()
try {
  Install-PSResource @params -Verbose 4>&1 | ForEach-Object {
    if ($_ -is [System.Management.Automation.VerboseRecord]) {
      $providerMessages.Add($_.Message)
    } else {
      $_
    }
  } | Out-Null
  Write-Output 'PFPSRG::INSTALL::OK'
  exit 0
} catch {
  $failure = $_
  $messages = @($failure.Exception.Message) + @($Error | ForEach-Object { $_.Exception.Message }) + @($providerMessages)
  $msg = ($messages | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) -join "`n"
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}


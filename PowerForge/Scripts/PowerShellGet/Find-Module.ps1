param(
  [string]$NamesB64,
  [string]$ReposB64,
  [string]$PrereleaseFlag,
  [string]$CredentialUser,
  [string]$CredentialSecret
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function DecodeLines([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
  return $text -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

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

$names = DecodeLines $NamesB64
$repos = DecodeLines $ReposB64
$prerelease = ($PrereleaseFlag -eq '1')

$params = @{ ErrorAction = 'Stop' }
if ($names.Count -gt 0) { $params.Name = $names }
if ($repos.Count -gt 0) { $params.Repository = $repos[0] }
$params.AllVersions = $true
if ($prerelease) { $params.AllowPrerelease = $true }
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

try {
  $results = Find-Module @params
  foreach ($r in @($results)) {
    $name = [string]$r.Name
    $ver = [string]$r.Version
    $repo = [string]$r.Repository
    $fields = @($name, $ver, $repo) | ForEach-Object { Enc ([string]$_) }
    Write-Output ('PFPWSGET::ITEM::' + ($fields -join '::'))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  if ($msg -match 'No match was found') { exit 0 }
  $b64 = Enc $msg
  Write-Output ('PFPWSGET::ERROR::' + $b64)
  exit 1
}


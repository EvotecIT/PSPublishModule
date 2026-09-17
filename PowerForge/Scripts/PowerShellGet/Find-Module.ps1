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

$names = @(DecodeLines $NamesB64)
$repos = @(DecodeLines $ReposB64)
$prerelease = ($PrereleaseFlag -eq '1')

$commonParams = @{ ErrorAction = 'Stop' }
if ($repos.Count -gt 0) { $commonParams.Repository = $repos[0] }
if ($prerelease) { $commonParams.AllowPrerelease = $true }
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $commonParams.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

try {
  foreach ($requestedName in $names) {
    $params = @{
      ErrorAction = 'Stop'
      Name = $requestedName
      AllVersions = $true
    }
    foreach ($entry in $commonParams.GetEnumerator()) { $params[$entry.Key] = $entry.Value }

    try {
      $results = Find-Module @params
    } catch {
      if ($_.Exception.Message -match 'No match was found') { continue }
      throw
    }

    foreach ($r in @($results)) {
      $name = [string]$r.Name
      $ver = [string]$r.Version
      $repo = [string]$r.Repository
      $guid = [string]$r.Guid
      $fields = @($name, $ver, $repo, $guid) | ForEach-Object { Enc ([string]$_) }
      Write-Output ('PFPWSGET::ITEM::' + ($fields -join '::'))
    }
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFPWSGET::ERROR::' + $b64)
  exit 1
}


param(
  [string]$NamesB64,
  [string]$Version,
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

try {
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

$names = DecodeLines $NamesB64
$repos = DecodeLines $ReposB64
$prerelease = ($PrereleaseFlag -eq '1')

$params = @{ ErrorAction = 'Stop' }
if ($names.Count -gt 0) { $params.Name = $names }
if (-not [string]::IsNullOrWhiteSpace($Version)) { $params.Version = $Version }
if ($repos.Count -gt 0) { $params.Repository = $repos }
if ($prerelease) { $params.Prerelease = $true }
if (-not [string]::IsNullOrWhiteSpace($CredentialUser) -and -not [string]::IsNullOrWhiteSpace($CredentialSecret)) {
  $sec = ConvertTo-SecureString -String $CredentialSecret -AsPlainText -Force
  $params.Credential = New-Object System.Management.Automation.PSCredential($CredentialUser, $sec)
}

try {
  $results = Find-PSResource @params
  foreach ($r in $results) {
    $name = [string]$r.Name
    $ver = [string]$r.Version
    $repo = [string]$r.Repository
    $author = [string]$r.Author
    $desc = [string]$r.Description
    $fields = @($name, $ver, $repo, $author, $desc) | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFPSRG::ITEM::' + ($fields -join '::'))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}


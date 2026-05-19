param(
  [string]$Name,
  [string]$Uri,
  [string]$TrustedFlag,
  [string]$Priority,
  [string]$ApiVersion
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

$existing = $null
try { $existing = Get-PSResourceRepository -Name $Name -ErrorAction SilentlyContinue } catch { $existing = $null }

$commonParams = @{ ErrorAction = 'Stop' }
$commonParams.Name = $Name
if ($TrustedFlag -eq '1') { $commonParams.Trusted = $true }
if (-not [string]::IsNullOrWhiteSpace($Priority)) { $commonParams.Priority = [int]$Priority }
if (-not [string]::IsNullOrWhiteSpace($ApiVersion)) { $commonParams.ApiVersion = $ApiVersion }

$isPSGallery = -not [string]::IsNullOrWhiteSpace($Name) -and ($Name.Trim().ToLowerInvariant() -eq 'psgallery')
$isAzureArtifacts = $Uri -match '^https://pkgs\.dev\.azure\.com/' -or $Uri -match '^https://[^/]+\.pkgs\.visualstudio\.com/'

function Invoke-RepositoryCommand {
  param(
    [string]$CommandName,
    [hashtable]$Parameters
  )

  try {
    & $CommandName @Parameters | Out-Null
  } catch {
    $message = $_.Exception.Message
    if ($Parameters.ContainsKey('CredentialProvider') -and
        ($message -match 'CredentialProvider' -or $message -match 'parameter.*cannot be found' -or $message -match 'named parameter')) {
      $retry = $Parameters.Clone()
      $retry.Remove('CredentialProvider')
      & $CommandName @retry | Out-Null
      return
    }

    throw
  }
}

try {
  $created = $false

  if ($isPSGallery) {
    if ($existing) {
      Set-PSResourceRepository @commonParams | Out-Null
    } else {
      $created = $true
      Register-PSResourceRepository -PSGallery -Force -ErrorAction Stop | Out-Null
      # Apply settings (Trusted/Priority/ApiVersion) after registration (PSGallery has a predefined Uri).
      Set-PSResourceRepository @commonParams | Out-Null
    }
  } else {
    $params = $commonParams.Clone()
    $params.Uri = $Uri
    if ($isAzureArtifacts) {
      $params.CredentialProvider = 'AzArtifacts'
    }

    if ($existing) {
      Invoke-RepositoryCommand -CommandName 'Set-PSResourceRepository' -Parameters $params
    } else {
      $created = $true
      $params.Force = $true
      Invoke-RepositoryCommand -CommandName 'Register-PSResourceRepository' -Parameters $params
    }
  }

  Write-Output ('PFPSRG::REPO::CREATED::' + ($(if ($created) { '1' } else { '0' })))
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}

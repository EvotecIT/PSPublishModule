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
$registeredRepositories = @()
try { $registeredRepositories = @(Get-PSResourceRepository -ErrorAction SilentlyContinue) } catch { $registeredRepositories = @() }

$commonParams = @{ ErrorAction = 'Stop' }
$commonParams.Name = $Name
if ($TrustedFlag -eq '1') { $commonParams.Trusted = $true }
if (-not [string]::IsNullOrWhiteSpace($Priority)) { $commonParams.Priority = [int]$Priority }
if (-not [string]::IsNullOrWhiteSpace($ApiVersion)) { $commonParams.ApiVersion = $ApiVersion }

$isPSGallery = -not [string]::IsNullOrWhiteSpace($Name) -and ($Name.Trim().ToLowerInvariant() -eq 'psgallery')
$isAzureArtifacts = $Uri -match '^https://pkgs\.dev\.azure\.com/' -or $Uri -match '^https://[^/]+\.pkgs\.visualstudio\.com/'
$isMicrosoftArtifactRegistry = -not [string]::IsNullOrWhiteSpace($Uri) -and
  $Uri.TrimEnd('/').ToLowerInvariant() -eq 'https://mcr.microsoft.com' -and
  -not [string]::IsNullOrWhiteSpace($Name) -and
  $Name.Trim().ToLowerInvariant() -eq 'mar' -and
  $ApiVersion -eq 'ContainerRegistry'

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

function Normalize-RepositoryUri {
  param([object]$Value)

  if ($null -eq $Value) { return '' }
  return ([string]$Value).Trim().TrimEnd('/').ToLowerInvariant()
}

function Get-RepositoryName {
  param([object]$Repository)

  if ($null -eq $Repository) { return '' }
  return ([string]$Repository.Name).Trim()
}

function Get-RepositoryUri {
  param([object]$Repository)

  if ($null -eq $Repository) { return '' }
  return Normalize-RepositoryUri $Repository.Uri
}

function Assert-NoRepositoryUriConflict {
  param(
    [string]$TargetName,
    [string]$TargetUri,
    [object]$ExistingRepository,
    [object[]]$Repositories
  )

  $normalizedName = if ([string]::IsNullOrWhiteSpace($TargetName)) { '' } else { $TargetName.Trim() }
  $normalizedUri = Normalize-RepositoryUri $TargetUri
  if ([string]::IsNullOrWhiteSpace($normalizedName) -or [string]::IsNullOrWhiteSpace($normalizedUri)) {
    return
  }

  $conflicts = @($Repositories | Where-Object {
    $repoName = Get-RepositoryName $_
    -not [string]::IsNullOrWhiteSpace($repoName) -and
      -not [string]::Equals($repoName, $normalizedName, [System.StringComparison]::OrdinalIgnoreCase) -and
      [string]::Equals((Get-RepositoryUri $_), $normalizedUri, [System.StringComparison]::OrdinalIgnoreCase)
  })

  if ($conflicts.Count -eq 0) {
    return
  }

  $existingUri = Get-RepositoryUri $ExistingRepository
  if ($null -ne $ExistingRepository -and
      [string]::Equals($existingUri, $normalizedUri, [System.StringComparison]::OrdinalIgnoreCase)) {
    return
  }

  $conflictNames = ($conflicts | ForEach-Object { "'" + (Get-RepositoryName $_) + "'" }) -join ', '
  throw "Repository URI '$TargetUri' is already registered as $conflictNames. Use -RepositoryName with the existing repository name or unregister the duplicate alias before registering '$TargetName'."
}

function Test-RepositoryMatches {
  param(
    [object]$Repository,
    [string]$TargetUri,
    [string]$TrustedFlag,
    [string]$Priority,
    [string]$ApiVersion
  )

  if ($null -eq $Repository) { return $false }

  $normalizedTargetUri = Normalize-RepositoryUri $TargetUri
  if (-not [string]::IsNullOrWhiteSpace($normalizedTargetUri) -and
      -not [string]::Equals((Get-RepositoryUri $Repository), $normalizedTargetUri, [System.StringComparison]::OrdinalIgnoreCase)) {
    return $false
  }

  if ($TrustedFlag -eq '1') {
    try {
      if (-not [bool]$Repository.Trusted) { return $false }
    } catch {
      return $false
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($Priority)) {
    try {
      if ([int]$Repository.Priority -ne [int]$Priority) { return $false }
    } catch {
      return $false
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($ApiVersion)) {
    try {
      if (-not [string]::Equals([string]$Repository.ApiVersion, $ApiVersion, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
    } catch {
      return $false
    }
  }

  return $true
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
  } elseif ($isMicrosoftArtifactRegistry) {
    $marSetParams = $commonParams.Clone()
    $marSetParams.Uri = $Uri
    if ($existing) {
      Set-PSResourceRepository @marSetParams | Out-Null
    } else {
      $created = $true
      $registerCommand = Get-Command -Name 'Register-PSResourceRepository' -ErrorAction Stop
      if ($registerCommand.Parameters.ContainsKey('MAR')) {
        $marParams = @{ MAR = $true; Force = $true; ErrorAction = 'Stop' }
        if ($TrustedFlag -eq '1') { $marParams.Trusted = $true }
        if (-not [string]::IsNullOrWhiteSpace($Priority)) { $marParams.Priority = [int]$Priority }
        Register-PSResourceRepository @marParams | Out-Null
        Set-PSResourceRepository @marSetParams | Out-Null
      } elseif ($registerCommand.Parameters.ContainsKey('MicrosoftArtifactRegistry')) {
        $marParams = @{ MicrosoftArtifactRegistry = $true; Force = $true; ErrorAction = 'Stop' }
        if ($TrustedFlag -eq '1') { $marParams.Trusted = $true }
        if (-not [string]::IsNullOrWhiteSpace($Priority)) { $marParams.Priority = [int]$Priority }
        Register-PSResourceRepository @marParams | Out-Null
        Set-PSResourceRepository @marSetParams | Out-Null
      } else {
        $params = $commonParams.Clone()
        $params.Uri = $Uri
        $params.Force = $true
        Invoke-RepositoryCommand -CommandName 'Register-PSResourceRepository' -Parameters $params
      }
    }
  } else {
    Assert-NoRepositoryUriConflict -TargetName $Name -TargetUri $Uri -ExistingRepository $existing -Repositories $registeredRepositories

    $params = $commonParams.Clone()
    $params.Uri = $Uri
    if ($isAzureArtifacts) {
      $params.CredentialProvider = 'AzArtifacts'
    }

    if ($existing) {
      if (-not (Test-RepositoryMatches -Repository $existing -TargetUri $Uri -TrustedFlag $TrustedFlag -Priority $Priority -ApiVersion $ApiVersion)) {
        Invoke-RepositoryCommand -CommandName 'Set-PSResourceRepository' -Parameters $params
      }
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

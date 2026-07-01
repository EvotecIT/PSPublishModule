param(
  [string]$Name,
  [string]$RequiredVersion,
  [string]$MinimumVersion,
  [string]$MaximumVersion,
  [string]$InstallScope,
  [string]$MinimumVersionInclusive = '1',
  [string]$MaximumVersionInclusive = '1'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Enc([string]$s) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(([string]$s)))
}

function Get-SemanticModuleVersion($Module) {
  if (-not $Module -or -not $Module.Version) { return '' }
  $version = [string]$Module.Version
  $prerelease = [string]$Module.PrivateData.PSData.Prerelease
  if (-not [string]::IsNullOrWhiteSpace($prerelease) -and $version -notmatch '-') {
    return $version + '-' + $prerelease.Trim().TrimStart('-')
  }
  return $version
}

function Test-IsPrereleaseModule($Module) {
  return (Get-SemanticModuleVersion $Module).IndexOf('-') -ge 0
}

function ConvertTo-SemanticModuleVersionParts([string]$VersionText) {
  if ([string]::IsNullOrWhiteSpace($VersionText)) { return $null }
  $value = $VersionText.Trim()
  if ($value.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $value = $value.Substring(1)
  }

  $buildIndex = $value.IndexOf('+')
  if ($buildIndex -ge 0) { $value = $value.Substring(0, $buildIndex) }

  $prereleaseIndex = $value.IndexOf('-')
  $core = if ($prereleaseIndex -ge 0) { $value.Substring(0, $prereleaseIndex) } else { $value }
  $prerelease = if ($prereleaseIndex -ge 0) { $value.Substring($prereleaseIndex + 1) } else { '' }
  $coreParts = $core.Split('.')
  if ($coreParts.Length -lt 2 -or $coreParts.Length -gt 4) { return $null }

  $major = 0
  $minor = 0
  $patch = 0
  $revision = 0
  if (-not [int]::TryParse($coreParts[0], [ref]$major)) { return $null }
  if (-not [int]::TryParse($coreParts[1], [ref]$minor)) { return $null }
  if ($coreParts.Length -ge 3 -and -not [int]::TryParse($coreParts[2], [ref]$patch)) { return $null }
  if ($coreParts.Length -eq 4 -and -not [int]::TryParse($coreParts[3], [ref]$revision)) { return $null }

  $prereleaseParts = @()
  if (-not [string]::IsNullOrWhiteSpace($prerelease)) {
    $prereleaseParts = @($prerelease.Split('.') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  }

  [pscustomobject]@{
    Major = $major
    Minor = $minor
    Patch = $patch
    Revision = $revision
    Prerelease = $prereleaseParts
  }
}

function Compare-NumericText([string]$Left, [string]$Right) {
  $leftTrimmed = $Left.TrimStart('0')
  $rightTrimmed = $Right.TrimStart('0')
  if ($leftTrimmed.Length -eq 0) { $leftTrimmed = '0' }
  if ($rightTrimmed.Length -eq 0) { $rightTrimmed = '0' }
  if ($leftTrimmed.Length -ne $rightTrimmed.Length) {
    return $leftTrimmed.Length.CompareTo($rightTrimmed.Length)
  }
  return [string]::Compare($leftTrimmed, $rightTrimmed, [System.StringComparison]::Ordinal)
}

function Compare-MixedPrereleaseIdentifier([string]$Left, [string]$Right) {
  $leftIndex = 0
  $rightIndex = 0
  while ($leftIndex -lt $Left.Length -and $rightIndex -lt $Right.Length) {
    $leftDigits = [char]::IsDigit($Left[$leftIndex])
    $rightDigits = [char]::IsDigit($Right[$rightIndex])
    if ($leftDigits -ne $rightDigits) {
      if ($leftDigits) { return -1 }
      return 1
    }

    $leftStart = $leftIndex
    while ($leftIndex -lt $Left.Length -and [char]::IsDigit($Left[$leftIndex]) -eq $leftDigits) { $leftIndex++ }
    $rightStart = $rightIndex
    while ($rightIndex -lt $Right.Length -and [char]::IsDigit($Right[$rightIndex]) -eq $rightDigits) { $rightIndex++ }

    $leftPart = $Left.Substring($leftStart, $leftIndex - $leftStart)
    $rightPart = $Right.Substring($rightStart, $rightIndex - $rightStart)
    $comparison = if ($leftDigits) {
      Compare-NumericText $leftPart $rightPart
    } else {
      [string]::Compare($leftPart, $rightPart, [System.StringComparison]::OrdinalIgnoreCase)
    }

    if ($comparison -ne 0) { return $comparison }
  }

  return $Left.Length.CompareTo($Right.Length)
}

function Compare-SemanticModuleVersion([string]$Left, [string]$Right) {
  if ([string]::Equals($Left, $Right, [System.StringComparison]::OrdinalIgnoreCase)) { return 0 }
  $leftVersion = ConvertTo-SemanticModuleVersionParts $Left
  $rightVersion = ConvertTo-SemanticModuleVersionParts $Right
  if (-not $leftVersion -or -not $rightVersion) {
    return [string]::Compare($Left, $Right, [System.StringComparison]::OrdinalIgnoreCase)
  }

  $comparison = $leftVersion.Major.CompareTo($rightVersion.Major)
  if ($comparison -ne 0) { return $comparison }
  $comparison = $leftVersion.Minor.CompareTo($rightVersion.Minor)
  if ($comparison -ne 0) { return $comparison }
  $comparison = $leftVersion.Patch.CompareTo($rightVersion.Patch)
  if ($comparison -ne 0) { return $comparison }
  $comparison = $leftVersion.Revision.CompareTo($rightVersion.Revision)
  if ($comparison -ne 0) { return $comparison }

  $leftStable = $leftVersion.Prerelease.Count -eq 0
  $rightStable = $rightVersion.Prerelease.Count -eq 0
  if ($leftStable -and $rightStable) { return 0 }
  if ($leftStable) { return 1 }
  if ($rightStable) { return -1 }

  $count = [System.Math]::Min($leftVersion.Prerelease.Count, $rightVersion.Prerelease.Count)
  for ($i = 0; $i -lt $count; $i++) {
    $leftPart = [string]$leftVersion.Prerelease[$i]
    $rightPart = [string]$rightVersion.Prerelease[$i]
    $leftNumber = 0
    $rightNumber = 0
    $leftIsNumeric = [int]::TryParse($leftPart, [ref]$leftNumber)
    $rightIsNumeric = [int]::TryParse($rightPart, [ref]$rightNumber)
    if ($leftIsNumeric -and $rightIsNumeric) {
      $comparison = $leftNumber.CompareTo($rightNumber)
    } elseif ($leftIsNumeric -ne $rightIsNumeric) {
      $comparison = if ($leftIsNumeric) { -1 } else { 1 }
    } else {
      $comparison = Compare-MixedPrereleaseIdentifier $leftPart $rightPart
    }

    if ($comparison -ne 0) { return $comparison }
  }

  return $leftVersion.Prerelease.Count.CompareTo($rightVersion.Prerelease.Count)
}

function Select-LatestModule($Modules) {
  $latest = $null
  $latestVersion = ''
  foreach ($module in $Modules) {
    $version = Get-SemanticModuleVersion $module
    if (-not $latest -or (Compare-SemanticModuleVersion $version $latestVersion) -gt 0) {
      $latest = $module
      $latestVersion = $version
    }
  }
  return $latest
}

try {
  $mods = Get-Module -ListAvailable -Name $Name -ErrorAction SilentlyContinue
  if ($mods) {
    $filtered = $mods

    $allowsPrerelease = $false
    foreach ($requestedVersion in @($RequiredVersion, $MinimumVersion, $MaximumVersion)) {
      if (-not [string]::IsNullOrWhiteSpace($requestedVersion) -and $requestedVersion.IndexOf('-') -ge 0) {
        $allowsPrerelease = $true
      }
    }
    if (-not $allowsPrerelease) {
      $filtered = $filtered | Where-Object { -not (Test-IsPrereleaseModule $_) }
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallScope)) {
      $scopeRoots = @()
      if ($InstallScope -eq 'CurrentUser') {
        $documents = [Environment]::GetFolderPath('MyDocuments')
        if ($documents) {
          $scopeRoots += (Join-Path $documents 'PowerShell/Modules')
          $scopeRoots += (Join-Path $documents 'WindowsPowerShell/Modules')
        }
        if ($HOME) {
          $scopeRoots += (Join-Path $HOME '.local/share/powershell/Modules')
        }
      } elseif ($InstallScope -eq 'AllUsers') {
        $programFiles = [Environment]::GetFolderPath('ProgramFiles')
        if ($programFiles) {
          $scopeRoots += (Join-Path $programFiles 'PowerShell/Modules')
          $scopeRoots += (Join-Path $programFiles 'WindowsPowerShell/Modules')
        }
        $scopeRoots += '/usr/local/share/powershell/Modules'
        $scopeRoots += '/usr/share/powershell/Modules'
        $scopeRoots += '/opt/microsoft/powershell/7/Modules'
      }

      $normalizedRoots = @($scopeRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        try { [System.IO.Path]::GetFullPath($_).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) } catch { $null }
      } | Where-Object { $_ })
      if ($normalizedRoots.Count -gt 0) {
        $filtered = $filtered | Where-Object {
          if (-not $_.ModuleBase) { return $false }
          $moduleBase = [System.IO.Path]::GetFullPath($_.ModuleBase).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
          foreach ($root in $normalizedRoots) {
            if ($moduleBase.Equals($root, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
            $rootWithSeparator = $root + [System.IO.Path]::DirectorySeparatorChar
            if ($moduleBase.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
            $rootWithAltSeparator = $root + [System.IO.Path]::AltDirectorySeparatorChar
            if ($rootWithAltSeparator -ne $rootWithSeparator -and $moduleBase.StartsWith($rootWithAltSeparator, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
          }
          return $false
        }
      } else {
        $filtered = @()
      }
    }

    if (-not [string]::IsNullOrWhiteSpace($RequiredVersion)) {
      $filtered = $filtered | Where-Object { (Compare-SemanticModuleVersion (Get-SemanticModuleVersion $_) $RequiredVersion) -eq 0 }
    } else {
      if (-not [string]::IsNullOrWhiteSpace($MinimumVersion)) {
        if ($MinimumVersionInclusive -eq '0') {
          $filtered = $filtered | Where-Object { (Compare-SemanticModuleVersion (Get-SemanticModuleVersion $_) $MinimumVersion) -gt 0 }
        } else {
          $filtered = $filtered | Where-Object { (Compare-SemanticModuleVersion (Get-SemanticModuleVersion $_) $MinimumVersion) -ge 0 }
        }
      }
      if (-not [string]::IsNullOrWhiteSpace($MaximumVersion)) {
        if ($MaximumVersionInclusive -eq '0') {
          $filtered = $filtered | Where-Object { (Compare-SemanticModuleVersion (Get-SemanticModuleVersion $_) $MaximumVersion) -lt 0 }
        } else {
          $filtered = $filtered | Where-Object { (Compare-SemanticModuleVersion (Get-SemanticModuleVersion $_) $MaximumVersion) -le 0 }
        }
      }
    }

    $latest = Select-LatestModule $filtered
    if ($latest -and $latest.ModuleBase) {
      $ver = Get-SemanticModuleVersion $latest
      Write-Output ('PFMODLOC::FOUND::' + (Enc $ver) + '::' + (Enc $latest.ModuleBase))
    }
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  Write-Output ('PFMODLOC::ERROR::' + (Enc $msg))
  exit 1
}

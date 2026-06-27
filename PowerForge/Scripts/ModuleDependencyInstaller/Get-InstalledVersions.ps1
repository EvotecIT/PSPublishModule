param(
  [string]$NamesB64
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

function Get-SemanticModuleVersion($Module) {
  if (-not $Module -or -not $Module.Version) { return '' }
  $version = [string]$Module.Version
  $prerelease = [string]$Module.PrivateData.PSData.Prerelease
  if (-not [string]::IsNullOrWhiteSpace($prerelease) -and $version -notmatch '-') {
    return $version + '-' + $prerelease.Trim().TrimStart('-')
  }
  return $version
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
  if ($coreParts.Length -lt 2 -or $coreParts.Length -gt 3) { return $null }

  $major = 0
  $minor = 0
  $patch = 0
  if (-not [int]::TryParse($coreParts[0], [ref]$major)) { return $null }
  if (-not [int]::TryParse($coreParts[1], [ref]$minor)) { return $null }
  if ($coreParts.Length -eq 3 -and -not [int]::TryParse($coreParts[2], [ref]$patch)) { return $null }

  $prereleaseParts = @()
  if (-not [string]::IsNullOrWhiteSpace($prerelease)) {
    $prereleaseParts = @($prerelease.Split('.') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  }

  [pscustomobject]@{
    Major = $major
    Minor = $minor
    Patch = $patch
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
  $names = DecodeLines $NamesB64
  foreach ($n in $names) {
    $mods = Get-Module -ListAvailable -Name $n -ErrorAction SilentlyContinue
    $ver = ''
    if ($mods) {
      $latest = Select-LatestModule $mods
      if ($latest -and $latest.Version) { $ver = Get-SemanticModuleVersion $latest }
    }
    Write-Output ('PFMOD::ITEM::' + (Enc $n) + '::' + (Enc $ver))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = Enc $msg
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}

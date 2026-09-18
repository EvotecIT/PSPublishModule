param(
  [string]$NamesB64,
  [string]$ReferencesB64
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function DecodeLines([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
  return $text -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function DecodeText([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return '' }
  return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
}

function Get-ReferenceSpecs([string]$b64, [string[]]$fallbackNames) {
  if ([string]::IsNullOrWhiteSpace($b64)) {
    return @($fallbackNames | ForEach-Object {
      [pscustomobject]@{ Name = $_; ModuleVersion = ''; RequiredVersion = ''; MaximumVersion = ''; Guid = '' }
    })
  }

  try {
    $json = DecodeText $b64
    $items = @($json | ConvertFrom-Json)
    return @($items | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
  } catch {
    return @($fallbackNames | ForEach-Object {
      [pscustomobject]@{ Name = $_; ModuleVersion = ''; RequiredVersion = ''; MaximumVersion = ''; Guid = '' }
    })
  }
}

function Get-ModuleVersionText($module) {
  if (-not $module) { return '' }
  $baseVersion = [string]$module.Version
  $prerelease = ''
  try { $prerelease = [string]$module.PrivateData.PSData.Prerelease } catch { $prerelease = '' }
  $prerelease = $prerelease.Trim().TrimStart('-')
  if ([string]::IsNullOrWhiteSpace($prerelease)) { return $baseVersion }
  return $baseVersion + '-' + $prerelease
}

function Convert-ComparableModuleVersion([string]$value) {
  if ([string]::IsNullOrWhiteSpace($value)) { return $null }
  $normalized = $value.Trim()
  if ($normalized -ieq 'Auto' -or $normalized -ieq 'Latest') { return $null }
  if ($normalized.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalized = $normalized.Substring(1)
  }
  $buildSeparator = $normalized.IndexOf('+')
  if ($buildSeparator -ge 0) { $normalized = $normalized.Substring(0, $buildSeparator) }
  $prereleaseSeparator = $normalized.IndexOf('-')
  $coreText = if ($prereleaseSeparator -ge 0) { $normalized.Substring(0, $prereleaseSeparator) } else { $normalized }
  $prereleaseText = if ($prereleaseSeparator -ge 0) { $normalized.Substring($prereleaseSeparator + 1) } else { '' }
  try { $core = [version]$coreText } catch { return $null }
  $identifiers = if ([string]::IsNullOrWhiteSpace($prereleaseText)) { @() } else { @($prereleaseText -split '\.') }
  return [pscustomobject]@{ Core = $core; Prerelease = $identifiers }
}

function Compare-PrereleaseIdentifier([string]$left, [string]$right) {
  $leftNumeric = $left -match '^[0-9]+$'
  $rightNumeric = $right -match '^[0-9]+$'
  if ($leftNumeric -and $rightNumeric) {
    $leftNormalized = $left.TrimStart('0'); if ($leftNormalized.Length -eq 0) { $leftNormalized = '0' }
    $rightNormalized = $right.TrimStart('0'); if ($rightNormalized.Length -eq 0) { $rightNormalized = '0' }
    if ($leftNormalized.Length -ne $rightNormalized.Length) { return $leftNormalized.Length.CompareTo($rightNormalized.Length) }
    return [string]::CompareOrdinal($leftNormalized, $rightNormalized)
  }
  if ($leftNumeric) { return -1 }
  if ($rightNumeric) { return 1 }
  return [string]::CompareOrdinal($left, $right)
}

function Compare-ComparableModuleVersion($left, $right) {
  $coreComparison = $left.Core.CompareTo($right.Core)
  if ($coreComparison -ne 0) { return $coreComparison }
  $leftPrerelease = @($left.Prerelease)
  $rightPrerelease = @($right.Prerelease)
  if ($leftPrerelease.Count -eq 0 -and $rightPrerelease.Count -eq 0) { return 0 }
  if ($leftPrerelease.Count -eq 0) { return 1 }
  if ($rightPrerelease.Count -eq 0) { return -1 }
  $count = [Math]::Min($leftPrerelease.Count, $rightPrerelease.Count)
  for ($index = 0; $index -lt $count; $index++) {
    $comparison = Compare-PrereleaseIdentifier ([string]$leftPrerelease[$index]) ([string]$rightPrerelease[$index])
    if ($comparison -ne 0) { return $comparison }
  }
  return $leftPrerelease.Count.CompareTo($rightPrerelease.Count)
}

function Test-ModuleVersionConstraint([string]$candidateText, $reference) {
  $candidate = Convert-ComparableModuleVersion $candidateText
  if (-not $candidate) { return $false }
  $resolved = Convert-ComparableModuleVersion ([string]$reference.ResolvedVersion)
  if ($resolved -and (Compare-ComparableModuleVersion $candidate $resolved) -ne 0) { return $false }
  $resolvedMinimum = Convert-ComparableModuleVersion ([string]$reference.ResolvedMinimumVersion)
  if ($resolvedMinimum -and (Compare-ComparableModuleVersion $candidate $resolvedMinimum) -lt 0) { return $false }
  $matchPrereleaseByBaseVersion = [bool]$reference.MatchPrereleaseByBaseVersion
  $required = Convert-ComparableModuleVersion ([string]$reference.RequiredVersion)
  $minimum = Convert-ComparableModuleVersion ([string]$reference.ModuleVersion)
  $maximum = Convert-ComparableModuleVersion ([string]$reference.MaximumVersion)
  if ($required) {
    if ($matchPrereleaseByBaseVersion) {
      if ($candidate.Core.CompareTo($required.Core) -ne 0) { return $false }
    } elseif ((Compare-ComparableModuleVersion $candidate $required) -ne 0) {
      return $false
    }
  }
  if ($minimum) {
    $comparison = if ($matchPrereleaseByBaseVersion) { $candidate.Core.CompareTo($minimum.Core) } else { Compare-ComparableModuleVersion $candidate $minimum }
    if ($comparison -lt 0) { return $false }
  }
  if ($maximum) {
    $comparison = if ($matchPrereleaseByBaseVersion) { $candidate.Core.CompareTo($maximum.Core) } else { Compare-ComparableModuleVersion $candidate $maximum }
    if ($comparison -gt 0) { return $false }
  }
  return $true
}

$names = DecodeLines $NamesB64
$references = Get-ReferenceSpecs $ReferencesB64 $names
foreach ($ref in $references) {
  $n = [string]$ref.Name
  try {
    $modules = @(Get-Module -ListAvailable -Name $n)
    $guid = [string]$ref.Guid
    if (-not [string]::IsNullOrWhiteSpace($guid) -and $guid -ne 'Auto') {
      $modules = @($modules | Where-Object { [string]$_.Guid -ieq $guid })
    }

    $m = $null
    $ver = ''
    $selectedComparableVersion = $null
    foreach ($candidate in $modules) {
      $candidateVersion = Get-ModuleVersionText $candidate
      if (-not (Test-ModuleVersionConstraint $candidateVersion $ref)) { continue }
      $candidateComparableVersion = Convert-ComparableModuleVersion $candidateVersion
      if (-not $m -or (Compare-ComparableModuleVersion $candidateComparableVersion $selectedComparableVersion) -gt 0) {
        $m = $candidate
        $ver = $candidateVersion
        $selectedComparableVersion = $candidateComparableVersion
      }
    }
    $guid = if ($m) { [string]$m.Guid } else { '' }
    $moduleBase = if ($m) { [string]$m.ModuleBase } else { '' }
    $fields = @($n, $ver, $guid, $moduleBase) | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFMODINFO::ITEM::' + ($fields -join '::'))
  } catch {
    $fields = @($n, '', '', '') | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFMODINFO::ITEM::' + ($fields -join '::'))
  }
}
exit 0

param($name, $ReferenceB64)
$ErrorActionPreference = 'Stop'

function Encode([string]$value) {
  if ($null -eq $value) { $value = '' }
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$value))
}

function DecodeText([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return '' }
  return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
}

function Get-ReferenceSpec {
  param(
    [string]$FallbackName,
    [string]$B64
  )

  if (-not [string]::IsNullOrWhiteSpace($B64)) {
    try {
      $json = DecodeText $B64
      $item = $json | ConvertFrom-Json
      if ($null -ne $item -and -not [string]::IsNullOrWhiteSpace($item.Name)) {
        return $item
      }
    } catch {
      # Fall back to name-only lookup below.
    }
  }

  [pscustomobject]@{
    Name = $FallbackName
    ModuleVersion = ''
    RequiredVersion = ''
    MaximumVersion = ''
    Guid = ''
  }
}

function Convert-VersionOrNull([string]$value) {
  if ([string]::IsNullOrWhiteSpace($value)) { return $null }
  try { return [version]$value } catch { return $null }
}

function Get-ManifestPath {
  param($Module)

  if ($Module.Path -and [System.IO.Path]::GetExtension([string]$Module.Path) -ieq '.psd1' -and (Test-Path -LiteralPath $Module.Path)) {
    return [string]$Module.Path
  }

  if ($Module.ModuleBase) {
    $candidate = Join-Path -Path ([string]$Module.ModuleBase) -ChildPath ($Module.Name + '.psd1')
    if (Test-Path -LiteralPath $candidate) {
      return [string]$candidate
    }

    $manifest = Get-ChildItem -LiteralPath ([string]$Module.ModuleBase) -Filter '*.psd1' -File -ErrorAction SilentlyContinue |
      Select-Object -First 1
    if ($null -ne $manifest) {
      return [string]$manifest.FullName
    }
  }

  return $null
}

function Get-EntryValue {
  param(
    $Entry,
    [string[]]$Keys
  )

  foreach ($key in $Keys) {
    if ($Entry.ContainsKey($key)) {
      $value = $Entry[$key]
      if ($null -ne $value) {
        return [string]$value
      }
    }
  }

  return ''
}

function ConvertTo-RequiredModuleRecord {
  param($Entry)

  if ($null -eq $Entry) {
    return $null
  }

  if ($Entry -is [string]) {
    if ([string]::IsNullOrWhiteSpace($Entry)) {
      return $null
    }

    return [pscustomobject]@{
      ModuleName = [string]$Entry
      ModuleVersion = ''
      RequiredVersion = ''
      MaximumVersion = ''
      Guid = ''
    }
  }

  if ($Entry -is [System.Collections.IDictionary]) {
    $moduleName = Get-EntryValue -Entry $Entry -Keys @('ModuleName', 'Name')
    if ([string]::IsNullOrWhiteSpace($moduleName)) {
      return $null
    }

    return [pscustomobject]@{
      ModuleName = $moduleName
      ModuleVersion = Get-EntryValue -Entry $Entry -Keys @('ModuleVersion', 'Version')
      RequiredVersion = Get-EntryValue -Entry $Entry -Keys @('RequiredVersion')
      MaximumVersion = Get-EntryValue -Entry $Entry -Keys @('MaximumVersion')
      Guid = Get-EntryValue -Entry $Entry -Keys @('Guid')
    }
  }

  $moduleName = if ($Entry.Name) { [string]$Entry.Name } else { '' }
  if ([string]::IsNullOrWhiteSpace($moduleName)) {
    return $null
  }

  return [pscustomobject]@{
    ModuleName = $moduleName
    ModuleVersion = if ($Entry.Version) { [string]$Entry.Version } else { '' }
    RequiredVersion = if ($Entry.RequiredVersion) { [string]$Entry.RequiredVersion } else { '' }
    MaximumVersion = if ($Entry.MaximumVersion) { [string]$Entry.MaximumVersion } else { '' }
    Guid = if ($Entry.Guid) { [string]$Entry.Guid } else { '' }
  }
}

function Write-RequiredModuleRecord {
  param($Record)

  if ($null -eq $Record) {
    return
  }

  $fields = @(
    [string]$Record.ModuleName,
    [string]$Record.ModuleVersion,
    [string]$Record.RequiredVersion,
    [string]$Record.MaximumVersion,
    [string]$Record.Guid
  ) | ForEach-Object { Encode $_ }
  Write-Output ('PFREQMOD::ITEM::' + ($fields -join '::'))
}

$ref = Get-ReferenceSpec -FallbackName $name -B64 $ReferenceB64
$moduleName = [string]$ref.Name
$modules = @(Get-Module -ListAvailable -Name $moduleName)
$required = Convert-VersionOrNull ([string]$ref.RequiredVersion)
$minimum = Convert-VersionOrNull ([string]$ref.ModuleVersion)
$maximum = Convert-VersionOrNull ([string]$ref.MaximumVersion)
$guid = [string]$ref.Guid
if ($required) { $modules = @($modules | Where-Object { $_.Version -eq $required }) }
if ($minimum) { $modules = @($modules | Where-Object { $_.Version -ge $minimum }) }
if ($maximum) { $modules = @($modules | Where-Object { $_.Version -le $maximum }) }
if (-not [string]::IsNullOrWhiteSpace($guid) -and $guid -ne 'Auto') {
  $modules = @($modules | Where-Object { [string]$_.Guid -ieq $guid })
}

$mod = $modules |
  Sort-Object Version -Descending |
  Select-Object -First 1
if ($null -eq $mod) { return @() }

$manifestPath = Get-ManifestPath -Module $mod
if ($manifestPath) {
  try {
    $manifestData = Import-PowerShellDataFile -Path $manifestPath
    $rawRequiredModules = $manifestData.RequiredModules
    if ($null -ne $rawRequiredModules) {
      foreach ($entry in @($rawRequiredModules)) {
        Write-RequiredModuleRecord -Record (ConvertTo-RequiredModuleRecord -Entry $entry)
      }
      return
    }
  } catch {
    # Fall back to the module metadata below when raw manifest parsing is unavailable.
  }
}

$req = $mod.RequiredModules
if ($null -eq $req) { return @() }
$req | ForEach-Object {
  Write-RequiredModuleRecord -Record (ConvertTo-RequiredModuleRecord -Entry $_)
}

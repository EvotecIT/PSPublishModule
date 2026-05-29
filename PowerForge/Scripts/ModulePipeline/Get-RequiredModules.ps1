param($name)
$ErrorActionPreference = 'Stop'

function Encode([string]$value) {
  if ($null -eq $value) { $value = '' }
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$value))
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

$mod = Get-Module -ListAvailable -Name $name |
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

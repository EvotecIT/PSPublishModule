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
      [pscustomobject]@{ Name = $_; ModuleVersion = ''; RequiredVersion = ''; MaximumVersion = '' }
    })
  }

  try {
    $json = DecodeText $b64
    $items = @($json | ConvertFrom-Json)
    return @($items | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
  } catch {
    return @($fallbackNames | ForEach-Object {
      [pscustomobject]@{ Name = $_; ModuleVersion = ''; RequiredVersion = ''; MaximumVersion = '' }
    })
  }
}

function Convert-VersionOrNull([string]$value) {
  if ([string]::IsNullOrWhiteSpace($value)) { return $null }
  try { return [version]$value } catch { return $null }
}

$names = DecodeLines $NamesB64
$references = Get-ReferenceSpecs $ReferencesB64 $names
foreach ($ref in $references) {
  $n = [string]$ref.Name
  try {
    $modules = @(Get-Module -ListAvailable -Name $n)
    $required = Convert-VersionOrNull ([string]$ref.RequiredVersion)
    $minimum = Convert-VersionOrNull ([string]$ref.ModuleVersion)
    $maximum = Convert-VersionOrNull ([string]$ref.MaximumVersion)
    if ($required) { $modules = @($modules | Where-Object { $_.Version -eq $required }) }
    if ($minimum) { $modules = @($modules | Where-Object { $_.Version -ge $minimum }) }
    if ($maximum) { $modules = @($modules | Where-Object { $_.Version -le $maximum }) }

    $m = $modules | Sort-Object Version -Descending | Select-Object -First 1
    $ver = if ($m) { [string]$m.Version } else { '' }
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

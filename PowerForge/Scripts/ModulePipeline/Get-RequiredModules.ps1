param($name)
$ErrorActionPreference = 'Stop'

function Encode([string]$value) {
  if ($null -eq $value) { $value = '' }
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$value))
}

$mod = Get-Module -ListAvailable -Name $name |
  Sort-Object Version -Descending |
  Select-Object -First 1
if ($null -eq $mod) { return @() }
$req = $mod.RequiredModules
if ($null -eq $req) { return @() }
$req | ForEach-Object {
  $moduleName = [string]$_.Name
  $moduleVersion = if ($_.Version) { [string]$_.Version } else { '' }
  $requiredVersion = if ($_.RequiredVersion) { [string]$_.RequiredVersion } else { '' }
  $maximumVersion = if ($_.MaximumVersion) { [string]$_.MaximumVersion } else { '' }
  $guid = if ($_.Guid) { [string]$_.Guid } else { '' }
  $fields = @($moduleName, $moduleVersion, $requiredVersion, $maximumVersion, $guid) | ForEach-Object { Encode $_ }
  Write-Output ('PFREQMOD::ITEM::' + ($fields -join '::'))
}

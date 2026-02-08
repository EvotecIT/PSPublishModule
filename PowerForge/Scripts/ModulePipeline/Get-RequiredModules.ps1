param($name)
$mod = Get-Module -ListAvailable -Name $name |
  Sort-Object Version -Descending |
  Select-Object -First 1
if ($null -eq $mod) { return @() }
$req = $mod.RequiredModules
if ($null -eq $req) { return @() }
$req | ForEach-Object { $_.Name }

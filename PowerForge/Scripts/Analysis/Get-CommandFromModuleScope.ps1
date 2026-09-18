param($moduleBase, $commandName)
$m = Get-Module | Where-Object { $_.ModuleBase -eq $moduleBase } | Select-Object -First 1
if (-not $m) { throw "Imported module was not found at '$moduleBase'." }
& $m { param($c) Get-Command $c -ErrorAction Stop -Verbose:$false } $commandName

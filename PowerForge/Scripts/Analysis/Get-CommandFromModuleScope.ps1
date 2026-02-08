param($moduleName, $commandName)
$m = Import-Module -Name $moduleName -PassThru -ErrorAction Stop -Verbose:$false
& $m { param($c) Get-Command $c -ErrorAction Stop -Verbose:$false } $commandName

Import-Module ".\PSPublishModule.psm1" -Force


$Module = Get-Module -Name PSWritehtml -ListAvailable
$ModulePSM1 = [System.IO.Path]::Combine($Module.ModuleBase, $Module.RootModule)

Get-MissingFunctions -FilePath $ModulePSM1 -Summary | ft -AutoSize
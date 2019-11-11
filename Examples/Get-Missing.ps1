Import-Module "C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1" -Force

$Module = Get-Module -Name ADEssentials -ListAvailable
$ModulePSM1 = [System.IO.Path]::Combine($Module.ModuleBase, $Module.RootModule)

Get-MissingFunctions -FilePath $ModulePSM1 -Summary | ft -AutoSize
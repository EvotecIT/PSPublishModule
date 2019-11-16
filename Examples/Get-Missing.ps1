Import-Module "C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1" -Force

$Module = Get-Module -Name ADEssentials -ListAvailable
$ModulePSM1 = [System.IO.Path]::Combine($Module.ModuleBase, $Module.RootModule)

$ModulePSM1 = "$PSScriptRoot\Test.ps1"

$ModulePSM1 = 'C:\Users\przemyslaw.klys\AppData\Local\Temp\PSPasswordExpiryNotifications\PSPasswordExpiryNotifications.psm1'

#Get-MissingFunctions -FilePath $ModulePSM1 -Summary | ft -AutoSize
#$Commands1 = Get-ScriptCommandsOld  -FilePath $ModulePSM1


#$Commands = Get-ScriptCommands -FilePath $ModulePSM1


$Functions = Get-MissingFunctions -FilePath $ModulePSM1 -SummaryWithCommands


#$Commands.Count
#$Commands1.Count

#$Commands1 | Format-Table -AutoSize


#$Commands | Format-Table -AutoSize


$Functions | Format-Table -AutoSize

$Functions.Summary | Format-Table -AutoSize
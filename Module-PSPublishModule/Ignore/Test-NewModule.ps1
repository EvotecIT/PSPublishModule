$apikey = '7b8aafe1-d097-4b86-82a5-6c5a80a8b73c'
$DeleteModulePath = "C:\Users\pklys\Documents\WindowsPowerShell\Modules"
$ModulePath = "C:\Program Files\WindowsPowerShell\Modules"
$ProjectPath = "C:\Support\GitHub"

Clear-Host
Import-Module "C:\Support\GitHub\PSPublishModule\PSPublishModule.psm1" -Force
$projectName = 'PSWriteExcel'

#New-CreateModule -ProjectName $projectName -ModulePath $modulePath -ProjectPath $projectPath
#New-PrepareManifest -ProjectName $projectName -modulePath $modulePath -projectPath $projectPath -functionToExport '' -projectUrl "https://github.com/EvotecIT/$projectName"
#New-PrepareModule -projectName $projectName -modulePath $modulePath -projectPath $projectPath -DeleteModulePath $DeleteModulePath -AdditionalModulePath $AdditionalModule

#New-PublishModule -projectName $projectName -apikey $apikey
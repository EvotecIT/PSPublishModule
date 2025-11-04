echo "# PSOutlookProfile" >> README.md
git init
git add README.md
git commit -m "first commit"
git remote add origin https://github.com/EvotecIT/PSOutlookProfile.git
git push -u origin master


#$env:PSModulePath.split(';')

<#
C:\Program Files\WindowsPowerShell\Modules
C:\WINDOWS\system32\WindowsPowerShell\v1.0\Modules
C:\Program Files\Intel\
C:\Program Files (x86)\Microsoft SQL Server\130\Tools\PowerShell\Modules\
C:\Program Files\SharePoint Online Management Shell\
C:\Program Files (x86)\AutoIt3\AutoItX
C:\Program Files\WindowsPowerShell\Modules\
C:\Program Files (x86)\Microsoft SDKs\Azure\PowerShell\ResourceManager\AzureResourceManager\
C:\Program Files (x86)\Microsoft SDKs\Azure\PowerShell\ServiceManagement\
C:\Program Files (x86)\Microsoft SDKs\Azure\PowerShell\Storage\
C:\Program Files (x86)\SharePoint Online Management Shell\
C:\Program Files\Common Files\Skype for Business Online\Modules\
C:\Program Files (x86)\Windows Kits\10\Microsoft Application Virtualization\Sequencer\AppvPkgConverter
C:\Program Files (x86)\Windows Kits\10\Microsoft Application Virtualization\Sequencer\AppvSequencer
C:\Program Files (x86)\Windows Kits\10\Microsoft Application Virtualization\
C:\Users\pklys\.vscode\extensions\ms-vscode.powershell-1.7.0-insiders-634\modules
#>


<#
PS C:\Users\pklys\OneDrive - Evotec\Support\GitHub\ImportExcel\__tests__> Get-PSRepository

Name                      InstallationPolicy   SourceLocation
----                      ------------------   --------------
PSGallery                 Untrusted            https://www.powershellgallery.com/api/v2/
#>

Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted
#Register-PSRepository -Name PSGallery -SourceLocation https://www.powershellgallery.com/api/v2/ -PublishLocation https://www.powershellgallery.com/api/v2/package/ -ScriptSourceLocation https://www.powershellgallery.com/api/v2/items/psscript/ -ScriptPublishLocation https://www.powershellgallery.com/api/v2/package/ -InstallationPolicy Trusted -PackageManagementProvider NuGet
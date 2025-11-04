

Find-Module Microsoft.PowerShell.PSResourceGet
Install-Module Microsoft.PowerShell.PSResourceGet
Get-InstalledModule Microsoft.PowerShell.PSResourceGet

Get-PSResouceRepository
Set-PSResouceRepository -Name PSGallery -Trusted
Get-PSResouceRepository

#Find Modules in PowerShell Gallery
Find-PSResource pswritehtml

#Check Installed Module
Get-InstalledPSResource pswritehtml -Scope AllUsers
Get-InstalledPSResource pswritehtml


$start = Get-Date
Install-PSResource Microsoft.Graph -Scope AllUsers
Install-PSResource Microsoft.Graph.Beta -Scope AllUsers
$End = Get-Date
$TimeSpan = New-TimeSpan -Start $start -End $End
$TimeSpan


#Microsoft.Graph and Microsoft.Graph.Beta Modules
$start = Get-Date
Get-InstalledPSResource Microsoft.Graph -Scope AllUsers -ErrorAction SilentlyContinue | Uninstall-PSResource -Scope AllUsers -SkipDependencyCheck
Get-InstalledPSResource Microsoft.Graph* -Scope AllUsers -ErrorAction SilentlyContinue | Uninstall-PSResource -Scope AllUsers -SkipDependencyCheck
$End = Get-Date
$TimeSpan = New-TimeSpan -Start $start -End $End
$TimeSpan
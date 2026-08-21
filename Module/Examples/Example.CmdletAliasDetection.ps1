Import-Module .\PSPublishModule.psd1 -Force

#Get-PowerShellAssemblyMetadata -Path "C:\Support\GitHub\Mailozaurr\Sources\Mailozaurr.PowerShell\bin\Release\net472\Mailozaurr.PowerShell.dll" | Format-Table
#Get-PowerShellAssemblyMetadata -Path "C:\Support\GitHub\Mailozaurr\Sources\Mailozaurr.PowerShell\bin\Release\net7.0\Mailozaurr.PowerShell.dll"
#Get-PowerShellAssemblyMetadata -Path "C:\Support\GitHub\EventViewerX\Sources\PSEventViewer\bin\Debug\net8.0-windows\PSEventViewer.dll" | Format-Table
Get-PowerShellAssemblyMetadata -Path "C:\Support\GitHub\EventViewerX\Sources\PSEventViewer\bin\Debug\net472\PSEventViewer.dll" -Verbose | Format-Table

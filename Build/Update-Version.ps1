﻿#Import-Module C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1 -Force

Get-ProjectVersion -Path "C:\Support\GitHub\DnsClientX" -ExcludeFolders @('C:\Support\GitHub\DnsClientX\Module\Artefacts')

#Set-ProjectVersion -Path "C:\Support\GitHub\DnsClientX" -NewVersion "0.4.0"
#Set-ProjectVersion -Path "C:\Support\GitHub\DnsClientX" -VersionType Build

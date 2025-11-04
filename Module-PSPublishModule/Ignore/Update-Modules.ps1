Update-Module -Verbose #-AllowPrerelease
Get-InstalledModule | ForEach-Object {
    $CurrentVersion = $_.Version
    Get-InstalledModule -Name $_.Name -AllVersions | Where-Object -Property Version -LT -Value $CurrentVersion
} | Uninstall-Module -Verbose -Force #-WhatIf
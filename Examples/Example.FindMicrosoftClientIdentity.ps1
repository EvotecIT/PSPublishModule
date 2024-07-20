$Folder = $Env:PSModulePath -split ";"
foreach ($F in $Folder) {
    if (Test-Path -Path $F) {
        Get-ChildItem -LiteralPath $F -Recurse -Filter "*.dll" | ForEach-Object {
            if ($_.Name -like "Microsoft.Identity.Client.dll") {
                [PSCustomObject] @{
                    Name    = $_.FullName
                    Version = $_.VersionInfo.FileVersion
                }
            }
        }
    }
}

# Could not load type 'Microsoft.Identity.Client.Extensions.Msal.MsalCachePersistenceException
foreach ($F in $Folder) {
    if (Test-Path -Path $F) {
        Get-ChildItem -LiteralPath $F -Recurse -Filter "*.dll" | ForEach-Object {
            if ($_.Name -like "Microsoft.Identity.Client.*") {
                [PSCustomObject] @{
                    Name    = $_.FullName
                    Version = $_.VersionInfo.FileVersion
                }
            }
        }
    }
}
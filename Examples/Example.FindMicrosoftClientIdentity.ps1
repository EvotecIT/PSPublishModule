# $Folder = $Env:PSModulePath -split ";"
# $Output = foreach ($F in $Folder) {
#     if (Test-Path -Path $F) {
#         Get-ChildItem -LiteralPath $F -Recurse -Filter "*.dll" | ForEach-Object {
#             if ($_.Name -like "Microsoft.Identity.Client.dll") {
#                 [PSCustomObject] @{
#                     Name    = $_.FullName
#                     Version = $_.VersionInfo.FileVersion
#                 }
#             }
#         }
#     }
# }
# $Output | Format-Table -AutoSize

$Folder = $Env:PSModulePath -split ";"
$Output1 = foreach ($F in $Folder) {
    if (Test-Path -Path $F) {
        Get-ChildItem -LiteralPath $F -Recurse -Filter "*.dll" | ForEach-Object {
            if ($_.Name -like "Microsoft.IdentityModel.Abstractions.dll") {
                [PSCustomObject] @{
                    Name    = $_.FullName
                    Version = $_.VersionInfo.FileVersion
                }
            }
        }
    }
}
$Output1 | Format-Table -AutoSize

# Could not load type 'Microsoft.Identity.Client.Extensions.Msal.MsalCachePersistenceException
$Output2 = foreach ($F in $Folder) {
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
$Output2 | Format-Table -AutoSize
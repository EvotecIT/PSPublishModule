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
                    Name    = $_.Name
                    Path    = $_.FullName
                    Version = $_.VersionInfo.FileVersion
                }
            }
        }
    }
}
$Output1 | Group-Object -Property Name | ForEach-Object {
    "DLL Name: $($_.Name)"
    $_.Group | Format-Table -AutoSize -Property Version, Path
    ''
}

$Output2 = foreach ($F in $Folder) {
    if (Test-Path -Path $F) {
        Get-ChildItem -LiteralPath $F -Recurse -Filter "*.dll" | ForEach-Object {
            if ($_.Name -like "Microsoft.Identity.Client.*") {
                [PSCustomObject] @{
                    Name    = $_.Name
                    Path    = $_.FullName
                    Version = $_.VersionInfo.FileVersion
                }
            }
        }
    }
}
$Output2 | Group-Object -Property Name | ForEach-Object {
    "DLL Name: $($_.Name)"
    $_.Group | Format-Table -AutoSize -Property Version, Path
    ''
}
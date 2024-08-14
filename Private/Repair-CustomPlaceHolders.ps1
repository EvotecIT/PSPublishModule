function Repair-CustomPlaceHolders {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [System.Collections.IDictionary] $Configuration
    )

    $ModuleName = $Configuration.Information.ModuleName
    $ModuleVersion = $Configuration.Information.Manifest.ModuleVersion
    $TagName = "v$($ModuleVersion)"
    if ($Configuration.CurrentSettings.PreRelease) {
        $ModuleVersionWithPreRelease = "$($ModuleVersion)-$($Configuration.CurrentSettings.PreRelease)"
        $TagModuleVersionWithPreRelease = "v$($ModuleVersionWithPreRelease)"
    } else {
        $ModuleVersionWithPreRelease = $ModuleVersion
        $TagModuleVersionWithPreRelease = "v$($ModuleVersion)"
    }

    $BuiltinPlaceHolders = @(
        @{ Find = '{ModuleName}'; Replace = $ModuleName }
        @{ Find = '<ModuleName>'; Replace = $ModuleName }
        @{ Find = '{ModuleVersion}'; Replace = $ModuleVersion }
        @{ Find = '<ModuleVersion>'; Replace = $ModuleVersion }
        @{ Find = '{ModuleVersionWithPreRelease}'; Replace = $ModuleVersionWithPreRelease }
        @{ Find = '<ModuleVersionWithPreRelease>'; Replace = $ModuleVersionWithPreRelease }
        @{ Find = '{TagModuleVersionWithPreRelease}'; Replace = $TagModuleVersionWithPreRelease }
        @{ Find = '<TagModuleVersionWithPreRelease>'; Replace = $TagModuleVersionWithPreRelease }
        @{ Find = '{TagName}'; Replace = $TagName }
        @{ Find = '<TagName>'; Replace = $TagName }
    )

    Write-TextWithTime -Text "Replacing built-in and custom placeholders" -Color Yellow {
        $PSM1Content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 -ErrorAction Stop

        # Replace built-in placeholders
        if ($Configuration.PlaceHolderOption.SkipBuiltinReplacements -ne $true) {
            foreach ($PlaceHolder in $BuiltinPlaceHolders) {
                $PSM1Content = $PSM1Content.Replace($PlaceHolder.Find, $PlaceHolder.Replace)
            }
        }
        # Replace custom placeholders provided by the user
        foreach ($PlaceHolder in $Configuration.PlaceHolders) {
            $PSM1Content = $PSM1Content.Replace($PlaceHolder.Find, $PlaceHolder.Replace)
        }

        Set-Content -LiteralPath $Path -Value $PSM1Content -Encoding UTF8 -ErrorAction Stop -Force
    } -PreAppend Plus -SpacesBefore '   '
}
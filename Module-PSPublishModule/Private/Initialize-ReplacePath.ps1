function Initialize-ReplacePath {
    [CmdletBinding()]
    param(
        [string] $ReplacementPath,
        [string] $ModuleName,
        [string] $ModuleVersion,
        [System.Collections.IDictionary] $Configuration
    )

    $TagName = "v$($ModuleVersion)"
    if ($Configuration.CurrentSettings.PreRelease) {
        $ModuleVersionWithPreRelease = "$($ModuleVersion)-$($Configuration.CurrentSettings.PreRelease)"
        $TagModuleVersionWithPreRelease = "v$($ModuleVersionWithPreRelease)"
    } else {
        $ModuleVersionWithPreRelease = $ModuleVersion
        $TagModuleVersionWithPreRelease = "v$($ModuleVersion)"
    }

    $ReplacementPath = $ReplacementPath.Replace('<TagName>', $TagName)
    $ReplacementPath = $ReplacementPath.Replace('{TagName}', $TagName)
    $ReplacementPath = $ReplacementPath.Replace('<ModuleVersion>', $ModuleVersion)
    $ReplacementPath = $ReplacementPath.Replace('{ModuleVersion}', $ModuleVersion)
    $ReplacementPath = $ReplacementPath.Replace('<ModuleVersionWithPreRelease>', $ModuleVersionWithPreRelease)
    $ReplacementPath = $ReplacementPath.Replace('{ModuleVersionWithPreRelease}', $ModuleVersionWithPreRelease)
    $ReplacementPath = $ReplacementPath.Replace('<TagModuleVersionWithPreRelease>', $TagModuleVersionWithPreRelease)
    $ReplacementPath = $ReplacementPath.Replace('{TagModuleVersionWithPreRelease}', $TagModuleVersionWithPreRelease)
    $ReplacementPath = $ReplacementPath.Replace('<ModuleName>', $ModuleName)
    $ReplacementPath = $ReplacementPath.Replace('{ModuleName}', $ModuleName)
    $ReplacementPath
}
function New-PrepareModule {
    [CmdletBinding()]
    param (
        [System.Collections.IDictionary] $Configuration
    )
    if (-not $Configuration) {
        return
    }
    if (-not $Configuration.Information.DirectoryModulesCore) {
        $Configuration.Information.DirectoryModulesCore = "$Env:USERPROFILE\Documents\PowerShell\Modules"

    }
    if (-not $Configuration.Information.DirectoryModules) {
        $Configuration.Information.DirectoryModules = "$Env:USERPROFILE\Documents\WindowsPowerShell\Modules"
    }

    if ($Configuration.Steps.BuildModule.Enable) {
        Start-ModuleBuilding -Configuration $Configuration -Core:$false
    }
    if ($Configuration.Steps.BuildModule.EnableDesktop) {
        Start-ModuleBuilding -Configuration $Configuration -Core:$false
    }
    if ($Configuration.Steps.BuildModule.EnableCore) {
        Start-ModuleBuilding -Configuration $Configuration -Core:$true
    }
}
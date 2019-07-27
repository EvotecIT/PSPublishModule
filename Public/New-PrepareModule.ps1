function New-PrepareModule {
    [CmdletBinding()]
    param (
        [System.Collections.IDictionary] $Configuration
    )

    if (-not $Configuration) {
        return
    }
    $GlobalTime = [System.Diagnostics.Stopwatch]::StartNew()
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
    $Execute = "$($GlobalTime.Elapsed.Days) days, $($GlobalTime.Elapsed.Hours) hours, $($GlobalTime.Elapsed.Minutes) minutes, $($GlobalTime.Elapsed.Seconds) seconds, $($GlobalTime.Elapsed.Milliseconds) milliseconds"
    Write-Host "[Module Building]" -NoNewline -ForegroundColor Cyan
    Write-Host "[Time Total: $Execute]" -ForegroundColor Green
}
[CmdletBinding()]
param([string] $ImportPath, [string] $ParametersPath, [string] $InventoryPath, [string] $ResultPath)
$ErrorActionPreference='Stop'
$inventory=Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
$parameters=Get-Content -LiteralPath $ParametersPath -Raw | ConvertFrom-Json -AsHashtable
$breakpoints=Set-PSBreakpoint -Command @($inventory.Helpers.Name) -Action {}
$exitCode=0
try { & $ImportPath @parameters }
catch { $exitCode=1; [Console]::Error.WriteLine($_.ToString()) }
finally {
    $counts=@($breakpoints | Sort-Object Command | ForEach-Object {[ordered]@{name=$_.Command;calls=$_.HitCount}})
    [ordered]@{schemaVersion=1;classification='External command breakpoints in one diagnostic-only original process; no source edits or timing claim';helpers=$counts;all19Observed=(@($counts | Where-Object {$_.calls -gt 0}).Count -eq 19);exitCode=$exitCode} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
    $breakpoints | Remove-PSBreakpoint
}
exit $exitCode

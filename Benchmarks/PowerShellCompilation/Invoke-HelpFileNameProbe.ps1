param([Parameter(Mandatory)][string]$ModulePath)
$ErrorActionPreference='Stop'
$PSModuleAutoLoadingPreference='None'
Import-Module Microsoft.PowerShell.Utility,Microsoft.PowerShell.Management
Import-Module -Name $ModulePath -Force
$global:LocalizedData=@{ModuleNotFoundFromCommand='{0}: no module for {1}';MultipleModulesFoundFromCommand='{0}: multiple modules for {1}'}
function global:Get-OfflineHelpCommand { 'never execute' }
Set-Alias -Name OfflineHelpAlias -Value Get-OfflineHelpCommand -Scope Global
$dynamic=New-Module -Name OfflineDynamicHelp -ScriptBlock {function Get-OfflineDynamicCommand {'never execute'};Export-ModuleMember Get-OfflineDynamicCommand}
$samples=@($null,(Get-Command Get-Date),(Get-Command Get-OfflineHelpCommand),(Get-Command OfflineHelpAlias),$dynamic.ExportedCommands['Get-OfflineDynamicCommand'])
foreach($index in 0..4) {
    foreach($extra in $false,$true) {
        $warnings=[Collections.Generic.List[string]]::new()
        $failure=$null
        $records=[Collections.Generic.List[object]]::new()
        try {
            # Basic authored functions accept unused arguments; preserve that ABI.
            if($extra){GetHelpFileName -CommandInfo $samples[$index] -UnusedFlag 'ignored' 3>&1 | ForEach-Object {if($_ -is [Management.Automation.WarningRecord]){$warnings.Add($_.Message)}else{$records.Add($_)}}}
            else {GetHelpFileName -CommandInfo $samples[$index] 3>&1 | ForEach-Object {if($_ -is [Management.Automation.WarningRecord]){$warnings.Add($_.Message)}else{$records.Add($_)}}}
        } catch { $failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName} }
        [pscustomobject]@{case=$index;extra=$extra;records=@($records.ToArray());warning=@($warnings.ToArray());failure=$failure}|ConvertTo-Json -Depth 8 -Compress
    }
}

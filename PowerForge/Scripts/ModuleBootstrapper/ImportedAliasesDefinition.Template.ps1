# Define the aliases the loader collected at the module scope. The alias provider needs no cmdlet module.
foreach ($PowerForgeImportedAlias in $script:PowerForgeImportedAliases) {
    $null = $ExecutionContext.SessionState.InvokeProvider.Item.Set([string[]]@('Alias:' + $PowerForgeImportedAlias.Name), $PowerForgeImportedAlias.Definition, $true, $true)
}
if ($script:PowerForgeExportImportedAliases -and $script:PowerForgeImportedAliases.Count -gt 0) {
    Export-ModuleMember -Alias ([string[]]@(foreach ($PowerForgeImportedAlias in $script:PowerForgeImportedAliases) { $PowerForgeImportedAlias.Name }))
}
foreach ($PowerForgeImportedAliasVariable in @('PowerForgeImportedAlias', 'PowerForgeImportedAliases', 'PowerForgeExportImportedAliases')) {
    try {
        $ExecutionContext.SessionState.PSVariable.Remove($PowerForgeImportedAliasVariable)
    } catch {
        # Also keeps PowerShell from optimizing these variables into locals that the API cannot remove.
        Remove-Variable -Name $PowerForgeImportedAliasVariable -Scope Script -Force -ErrorAction SilentlyContinue
    }
}
$ExecutionContext.SessionState.PSVariable.Remove('PowerForgeImportedAliasVariable')

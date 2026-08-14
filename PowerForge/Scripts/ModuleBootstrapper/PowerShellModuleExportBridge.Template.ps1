# Import-Module -Assembly loads the inner binary module into its own module object. PowerShell has no
# public API to copy those exported cmdlets back to the script-module wrapper, so this uses the same
# private PSModuleInfo hook used by community ALC loaders.
$AddExportedCmdlet = [System.Management.Automation.PSModuleInfo].GetMethod(
    'AddExportedCmdlet',
    [System.Reflection.BindingFlags]'Instance, NonPublic'
)
if ($null -ne $AddExportedCmdlet) {
    foreach ($Cmd in {{InnerModuleExpression}}.ExportedCmdlets.Values) {
        $AddExportedCmdlet.Invoke($ExecutionContext.SessionState.Module, @(, $Cmd)) | Out-Null
    }
    $AddExportedAlias = [System.Management.Automation.PSModuleInfo].GetMethod(
        'AddExportedAlias',
        [System.Reflection.BindingFlags]'Instance, NonPublic'
    )
    if ($null -ne $AddExportedAlias) {
        foreach ($Alias in {{InnerModuleExpression}}.ExportedAliases.Values) {
            $AliasTarget = if ([string]::IsNullOrWhiteSpace($Alias.Definition)) { $Alias.ResolvedCommandName } else { $Alias.Definition }
            try {
                # The alias must exist in this module scope before the private export table can reference it.
                Set-Alias -Name $Alias.Name -Value $AliasTarget -Scope Local -Force -ErrorAction Stop
                $ExportedAlias = $ExecutionContext.SessionState.InvokeCommand.GetCommand($Alias.Name, [System.Management.Automation.CommandTypes]::Alias)
                if ($null -ne $ExportedAlias) {
                    $AddExportedAlias.Invoke($ExecutionContext.SessionState.Module, @(, $ExportedAlias)) | Out-Null
                } else {
                    Write-Warning -Message "Alias '$($Alias.Name)' from {{LibraryName}} was created but could not be resolved for export."
                }
            } catch {
                Write-Warning -Message "Alias '$($Alias.Name)' from {{LibraryName}} could not be re-exported: $($_.Exception.Message)"
            }
        }
    } else {
        Write-Warning -Message "AddExportedAlias is not available on this PowerShell version. Aliases from {{LibraryName}} will not be re-exported to the module scope."
    }
} else {
    Write-Warning -Message "{{UnavailableMessage}}"{{FallbackImportBlock}}
}

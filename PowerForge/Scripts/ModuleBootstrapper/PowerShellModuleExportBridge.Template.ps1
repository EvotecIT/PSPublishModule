# Import-Module -Assembly loads the inner binary module into its own module object. PowerShell has no
# public API to copy those exported cmdlets back to the script-module wrapper, so this uses the same
# private PSModuleInfo hook used by community ALC loaders.
$AddExportedCmdlet = [System.Management.Automation.PSModuleInfo].GetMethod(
    'AddExportedCmdlet',
    [System.Reflection.BindingFlags]'Instance, NonPublic'
)
$PowerForgeOuterModule = $ExecutionContext.SessionState.Module
$PowerForgeIsModuleWrapper = $false
if ($null -ne $PowerForgeOuterModule -and
    -not [string]::IsNullOrWhiteSpace($PowerForgeOuterModule.Path) -and
    -not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
    try {
        [StringComparison] $PowerForgePathComparison = & {
            param([string] $PowerForgeProbePath)

            # Case behavior belongs to the containing filesystem. Windows can opt individual
            # directories into case sensitivity, while macOS volumes may be case-insensitive.
            # Inspect existing names without requiring write access; ambiguity fails closed.
            try {
                $PowerForgeProbeDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($PowerForgeProbePath))
                $PowerForgeNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
                foreach ($PowerForgeEntry in [IO.Directory]::EnumerateFileSystemEntries($PowerForgeProbeDirectory)) {
                    [void] $PowerForgeNames.Add([IO.Path]::GetFileName($PowerForgeEntry))
                }

                foreach ($PowerForgeName in $PowerForgeNames) {
                    foreach ($PowerForgeAlternateName in @($PowerForgeName.ToUpperInvariant(), $PowerForgeName.ToLowerInvariant())) {
                        if ([string]::Equals($PowerForgeName, $PowerForgeAlternateName, [StringComparison]::Ordinal) -or
                            $PowerForgeNames.Contains($PowerForgeAlternateName)) {
                            continue
                        }

                        $PowerForgeAlternatePath = [IO.Path]::Combine($PowerForgeProbeDirectory, $PowerForgeAlternateName)
                        if ([IO.File]::Exists($PowerForgeAlternatePath) -or [IO.Directory]::Exists($PowerForgeAlternatePath)) {
                            return [StringComparison]::OrdinalIgnoreCase
                        }

                        return [StringComparison]::Ordinal
                    }
                }
            } catch {
                # Ordinal comparison prevents an uncertain probe from exporting into a caller.
            }

            return [StringComparison]::Ordinal
        } $PSCommandPath
        $PowerForgeIsModuleWrapper = [string]::Equals(
            [IO.Path]::GetFullPath($PowerForgeOuterModule.Path),
            [IO.Path]::GetFullPath($PSCommandPath),
            $PowerForgePathComparison
        )
    } catch {
        $PowerForgeIsModuleWrapper = $false
    }
}
if (-not $PowerForgeIsModuleWrapper) {
    # A Script/ScriptPacked entry point, including one dot-sourced by another module, must not mutate
    # that caller's export table. The imported inner module's commands remain available to this script.
} elseif ($null -ne $AddExportedCmdlet) {
    foreach ($Cmd in {{InnerModuleExpression}}.ExportedCmdlets.Values) {
        $AddExportedCmdlet.Invoke($PowerForgeOuterModule, @(, $Cmd)) | Out-Null
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
                    $AddExportedAlias.Invoke($PowerForgeOuterModule, @(, $ExportedAlias)) | Out-Null
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

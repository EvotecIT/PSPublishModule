# Import-Module -Assembly inside this script module imports the inner binary module as a nested module,
# so its cmdlets and aliases are visible here and Export-ModuleMember can publish them on this module.
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
} else {
    Export-ModuleMember -Cmdlet ([string[]]@({{InnerModuleExpression}}.ExportedCmdlets.Keys)) -Alias ([string[]]@({{InnerModuleExpression}}.ExportedAliases.Keys))
}
